using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BMC.Core
{
    /// <summary>
    /// SaveMgr：單檔存檔管理系統（格式 v2）
    ///
    /// 設計取捨：存檔加密擋不住有心人（金鑰必定以明文躺在 global-metadata.dat 裡），
    /// 所以不再為「防竄改」付出效能代價。v2 改成效能優先 + 結構簡化：
    ///   - 一個 slot 只有一個檔案（砍掉獨立簽章檔），寫入從 6 次檔案操作降到 2 次。
    ///   - 砍掉 AES（原本只為了加密 47 bytes 的 meta 就建一整套 AES provider）。
    ///   - 砍掉 SHA256 完整性雜湊與隨機 junk 噪音，改用查表法 CRC32。
    ///   - 保留 XOR 混淆，讓檔案在記事本中仍是不可讀亂碼（勸退等級，不是安全機制）。
    ///
    /// v1 舊存檔仍可讀（見「舊格式相容」region），讀進來後不會立刻改寫，
    /// 等下一次 SaveCurrentSlot() 自然寫成 v2。
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        public int CurrentSlot { get; private set; } = 0;

        [Header("偵錯設定")]
        [Tooltip("是否顯示存檔/讀檔耗時等偵錯日誌")]
        public bool EnableDebugLogs = false;

        [Tooltip("是否啟用檔案內容混淆 (XOR)")]
        public bool EnableObfuscation = true;

        // --- 混亂命名設定 ---
        private const string DATA_PREFIX = "pkg_";
        private const string DATA_EXT = ".dat";
        private const string DATA_SALT = "Bmc_Data_Unique_Salt_#99";

        private const string SIG_PREFIX = "manifest_";
        private const string SIG_EXT = ".idx";
        private const string SIG_SALT = "Bmc_Sig_Chaos_Salt_@77_X";

        [Tooltip("是否啟用雲端動態金鑰？")]
        public bool UseCloudKey = false;

        private const string _localFallbackKey = "Bmc_Local_Fallback_Key_2026_!#99";

        // 核心字典 Key
        private const string KEY_CREATED_AT = "created_at";
        private const string KEY_LAST_SAVE_AT = "last_save_at";
        private const string KEY_SAVE_COUNT = "save_count";

        // 注意：目前整個框架沒有任何地方寫入 _dynamicCloudKey。
        // 也就是說 UseCloudKey = true 時 GetActiveKey() 必定回傳空字串，
        // SaveCurrentSlot() 開頭的金鑰檢查會讓它「明確失敗並記 log」，
        // 而不是靜悄悄寫出一份用空金鑰混淆的壞檔。雲端金鑰尚未實作。
        private string _dynamicCloudKey = string.Empty;
        private PlayerSave _currentSaveData = new PlayerSave();
        private bool _isTampered = false;

        // 這次讀檔走的是 v1 舊格式：下一次存檔寫成 v2 之後，順手把孤兒簽章檔刪掉。
        private bool _pendingLegacyCleanup = false;

        #region v2 檔案格式

        //  [0..3]   magic "BMCS"
        //  [4]      version = 2
        //  [5..8]   uint32 crc32(payload)   little-endian
        //  [9..12]  int32  payloadLen       little-endian
        //  [13]     flags: bit0 = payload 有做 XOR, bit1 = 竄改/毀損旗標 (sticky)
        //  [14..]   payload = protobuf PlayerSave，依 bit0 決定是否 XOR
        private const byte FORMAT_VERSION = 2;
        private const int HEADER_SIZE = 14;
        private const byte FLAG_OBFUSCATED = 0x01;
        private const byte FLAG_TAMPERED = 0x02;

        /// <summary>
        /// CRC32 查表（標準 IEEE 多項式 0xEDB88320），靜態建構子產生一次。
        /// </summary>
        private static readonly uint[] _crc32Table = new uint[256];

        static SaveMgr()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }
                _crc32Table[i] = c;
            }
        }

        /// <summary>
        /// 標準 IEEE CRC32。
        ///
        /// 【這不是安全機制】CRC32 只用來抓「寫入中斷、磁碟壞軌造成的半截檔或位元翻轉」。
        /// 它完全擋不住蓄意竄改——任何人改完內容重算一次 CRC 就過了。
        /// 不要把它當成防作弊，也不要在它上面疊任何安全假設。
        /// </summary>
        private static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc = _crc32Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// 流程：檢查檔頭是否為 v2 的 "BMCS" magic 且版本相符
        /// </summary>
        private static bool IsNewFormat(byte[] file)
        {
            return file.Length >= 5
                && file[0] == 0x42 && file[1] == 0x4D && file[2] == 0x43 && file[3] == 0x53
                && file[4] == FORMAT_VERSION;
        }

        #endregion

        #region 混沌路徑與 XOR 邏輯

        /// <summary>
        /// 流程：根據當前設定（雲端或本地）回傳相對應的金鑰字串
        /// </summary>
        private string GetActiveKey() => UseCloudKey ? _dynamicCloudKey : _localFallbackKey;

        // 路徑記憶化：GetPath / GetSigPath 原本每次呼叫都算一次 SHA256，
        // GetAllSlotsInfo(5) 就是 5 次。順便繞開 Application.persistentDataPath
        // 不能在主執行緒外取用的限制（第一次在主執行緒算完就固定了）。
        private readonly Dictionary<int, string> _pathCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _sigPathCache = new Dictionary<int, string>();

        /// <summary>
        /// 流程：將 Slot 索引結合資料鹽值進行 SHA256 雜湊，取前 10 位作為混淆檔名，組合成主存檔路徑（結果記憶化）
        /// </summary>
        public string GetPath(int slot)
        {
            if (_pathCache.TryGetValue(slot, out var cached)) return cached;

            string hash = GetHash(slot.ToString() + DATA_SALT).Substring(0, 10);
            string path = Path.Combine(Application.persistentDataPath, $"{DATA_PREFIX}{hash}{DATA_EXT}");
            _pathCache[slot] = path;
            return path;
        }

        /// <summary>
        /// 流程：使用非線性運算混淆 Slot 索引，結合簽名鹽值進行雜湊，產生 v1 舊格式校驗檔的路徑（結果記憶化）。
        /// v2 已不再寫出這個檔，但讀舊存檔與 DeleteSlot 清理時仍需要它。
        /// </summary>
        public string GetSigPath(int slot)
        {
            if (_sigPathCache.TryGetValue(slot, out var cached)) return cached;

            string chaoticInput = ((slot * 31) ^ 0x5F3759DF).ToString() + SIG_SALT;
            string hash = GetHash(chaoticInput).Substring(5, 16).ToLower();
            string path = Path.Combine(Application.persistentDataPath, $"{SIG_PREFIX}{hash}{SIG_EXT}");
            _sigPathCache[slot] = path;
            return path;
        }

        /// <summary>
        /// 流程：標準 SHA256 雜湊計算，回傳大寫十六進位字串。
        /// 僅供檔名雜湊使用；因為上面兩個路徑函式都有記憶化，每個 slot 只會各跑一次。
        /// </summary>
        private string GetHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        // XOR 金鑰位元組快取：原本每次 ApplyXorInPlace 都重新 GetBytes 配置 32 bytes。
        private string _cachedKeySource;
        private byte[] _cachedKeyBytes;

        /// <summary>
        /// 流程：取得目前金鑰的 UTF8 位元組；金鑰字串沒變就直接重用快取，不重新配置
        /// </summary>
        private byte[] GetKeyBytes()
        {
            string key = GetActiveKey();
            if (!string.Equals(_cachedKeySource, key, StringComparison.Ordinal))
            {
                _cachedKeySource = key;
                _cachedKeyBytes = Encoding.UTF8.GetBytes(key);
            }
            return _cachedKeyBytes;
        }

        /// <summary>
        /// 流程：使用 XOR 演算法對資料區段進行混淆/還原。
        /// 這是輕量級的處理，目的是讓檔案在記事本中顯示為不可讀的亂碼，不是加密。
        /// </summary>
        private void ApplyXorInPlace(byte[] data, int offset, int count)
        {
            byte[] keyBytes = GetKeyBytes();
            if (keyBytes == null || keyBytes.Length == 0) return;

            for (int i = 0; i < count; i++)
            {
                data[offset + i] ^= keyBytes[i % keyBytes.Length];
            }
        }

        #endregion

        #region 核心存取接口
        // 1. 底層統一出口
        private string GetRawString(string key, string defaultValue = "") =>
            _currentSaveData.CoreData.TryGetValue(key, out var v) ? v : defaultValue;

        public T Get<T>(string key, T defaultValue = default)
        {
            string val = GetRawString(key);
            if (string.IsNullOrEmpty(val)) return defaultValue;

            Type targetType = typeof(T);

            try
            {
                if (targetType == typeof(DateTime))
                {
                    return long.TryParse(val, out long ticks) ? (T)(object)new DateTime(ticks) : (T)(object)DateTime.MinValue;
                }

                if (targetType == typeof(bool))
                {
                    return (T)(object)(val == "1");
                }

                if (targetType.IsEnum) return (T)Enum.Parse(targetType, val);

                // 使用 InvariantCulture 確保浮點數解析一致
                return (T)Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                if (EnableDebugLogs) Debug.LogWarning($"[SaveMgr] Get Error: {key} - {e}");
                return defaultValue;
            }
        }

        public void Set<T>(string key, T value)
        {
            // 防止傳入 null
            if (value == null)
            {
                _currentSaveData.CoreData.Remove(key);
                return;
            }

            string stringValue;

            if (value is bool b)
            {
                stringValue = b ? "1" : "0";
            }
            else if (value is DateTime dt)
            {
                stringValue = dt.Ticks.ToString();
            }
            else if (value is IFormattable formattable)
            {
                // 確保數字格式統一 (例如 float 1.5 不會變成 1,5)
                stringValue = formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            else
            {
                stringValue = value.ToString();
            }

            _currentSaveData.CoreData[key] = stringValue;
        }

        /// <summary>
        /// 流程：取得存檔最初建立的時間
        /// </summary>
        public DateTime GetCreatedAt() => Get<DateTime>(KEY_CREATED_AT);

        /// <summary>
        /// 流程：取得最後一次紀錄的時間
        /// </summary>
        public DateTime GetLastSaveAt() => Get<DateTime>(KEY_LAST_SAVE_AT);

        /// <summary>
        /// 流程：取得此存檔累計的儲存次數
        /// </summary>
        public int GetSaveCount() => Get(KEY_SAVE_COUNT, 0);

        /// <summary>
        /// 流程：直接回傳記憶體中記錄的竄改/毀損標記，此標記在載入時由 CRC 校驗決定
        /// </summary>
        public bool IsSaveTampered() => _isTampered;

        // --- Items 存取接口 (根據 map<int, int> 更新) ---

        /// <summary>
        /// 流程：一次性取得所有道具數據（ID 與 數量）供 UI 或系統邏輯遍歷
        /// </summary>
        public IDictionary<int, int> GetAllItems() => _currentSaveData.Items;

        /// <summary>
        /// 流程：根據 ID 取得特定道具數量
        /// </summary>
        /// <param name="itemId">道具 ID</param>
        public int GetItem(int itemId, int defaultAmount = 0) =>
            _currentSaveData.Items.TryGetValue(itemId, out var amount) ? amount : defaultAmount;

        /// <summary>
        /// 流程：設定特定道具數量
        /// </summary>
        public void SetItem(int itemId, int amount) =>
            _currentSaveData.Items[itemId] = amount;

        /// <summary>
        /// 流程：檢查是否擁有該道具 ID 的記錄
        /// </summary>
        public bool HasItem(int itemId) =>
            _currentSaveData.Items.ContainsKey(itemId);

        // --- Progress 存取接口 ---

        public string GetProgress(string key, string defaultValue = "") => _currentSaveData.Progress.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetProgress(string key, object value) => _currentSaveData.Progress[key] = value.ToString();

        public bool GetProgressBool(string key, bool defaultValue = false)
        {
            string val = GetProgress(key);
            if (string.IsNullOrEmpty(val)) return defaultValue;
            return val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void SetProgressBool(string key, bool value) => _currentSaveData.Progress[key] = value ? "1" : "0";

        #endregion

        #region 讀取與寫入

        /// <summary>
        /// 讀取流程：
        /// 1. 啟動計時器 (若開啟 EnableDebugLogs)
        /// 2. 一次讀進整個主檔案
        /// 3. 檢查檔頭：是 "BMCS" + version 2 就走 v2，否則退回 v1 舊格式解析
        /// 4. v2：校驗 payloadLen、比對 CRC32、依 flags bit0 還原 XOR、解析 Protobuf
        /// 5. 更新記憶體中的 _isTampered 狀態供遊戲後續判定
        /// 6. 停止計時並打印耗時
        ///
        /// 讀到 v1 舊存檔時「不會」當場改寫檔案（讀檔時做 IO 有風險），
        /// 只標記 _pendingLegacyCleanup，等下一次 SaveCurrentSlot() 自然升級。
        /// </summary>
        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);

            System.Diagnostics.Stopwatch sw = null;
            if (EnableDebugLogs)
            {
                sw = new System.Diagnostics.Stopwatch();
                sw.Start();
            }

            if (File.Exists(path))
            {
                try
                {
                    byte[] file = File.ReadAllBytes(path);

                    if (IsNewFormat(file))
                    {
                        _pendingLegacyCleanup = false;
                        LoadFromV2(file);
                    }
                    else
                    {
                        // 舊存檔：解完之後標記，下次存檔時升級並清掉孤兒簽章檔
                        LoadFromLegacyV1(slot, file);
                        _pendingLegacyCleanup = true;
                        if (EnableDebugLogs) Debug.Log("<color=yellow>[SaveMgr] 讀到 v1 舊格式存檔，將於下次存檔時升級為 v2。</color>");
                    }

                    if (_isTampered) Debug.LogWarning("<color=red>[SaveMgr] 校驗失敗：存檔內容與校驗碼不符（可能是寫入中斷或檔案毀損）。</color>");

                    if (EnableDebugLogs && sw != null)
                    {
                        sw.Stop();
                        Debug.Log($"<color=cyan>[SaveMgr] Slot {slot} 讀檔成功。總耗時: {sw.Elapsed.TotalMilliseconds:F2} ms</color>");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveMgr] 載入異常: {e}");
                    InitializeNewSave(slot);
                }
            }
            else
            {
                InitializeNewSave(slot);
            }
        }

        /// <summary>
        /// 流程：解析 v2 單檔格式。長度不合法直接丟例外交給上層重建存檔；
        /// CRC 不符只標記 _isTampered（仍嘗試解析，讓玩家至少救回大部分進度）。
        /// </summary>
        private void LoadFromV2(byte[] file)
        {
            if (file.Length < HEADER_SIZE) throw new Exception("V2 Header Truncated");

            uint storedCrc = (uint)(file[5] | (file[6] << 8) | (file[7] << 16) | (file[8] << 24));
            int payloadLen = file[9] | (file[10] << 8) | (file[11] << 16) | (file[12] << 24);
            byte flags = file[13];

            if (payloadLen < 0 || HEADER_SIZE + (long)payloadLen > file.Length)
            {
                throw new Exception($"V2 Payload Length Error (len={payloadLen}, file={file.Length})");
            }

            uint actualCrc = Crc32(file, HEADER_SIZE, payloadLen);
            bool crcMismatch = actualCrc != storedCrc;

            // 竄改/毀損旗標是 sticky 的：一旦出現過就跟著存檔走
            _isTampered = crcMismatch || (flags & FLAG_TAMPERED) != 0;

            byte[] payload = new byte[payloadLen];
            Buffer.BlockCopy(file, HEADER_SIZE, payload, 0, payloadLen);

            if ((flags & FLAG_OBFUSCATED) != 0)
            {
                ApplyXorInPlace(payload, 0, payload.Length);
            }

            _currentSaveData = PlayerSave.Parser.ParseFrom(payload);
        }

        /// <summary>
        /// 儲存流程：
        /// 1. 啟動計時器 (若開啟 EnableDebugLogs)
        /// 2. 序列化 Protobuf，依開關決定是否 XOR 混淆
        /// 3. 組出 14 bytes 檔頭（magic / version / crc32 / payloadLen / flags）
        /// 4. 寫入 tmp 後原子替換到正式路徑（共 2 次檔案操作；v1 時代是主檔+簽章檔各 3 次，合計 6 次）
        /// 5. 若上次是從 v1 舊格式讀進來的，順手刪掉孤兒簽章檔
        /// 6. 停止計時並打印耗時
        /// </summary>
        /// <returns>
        /// 是否儲存成功。失敗時只會寫入錯誤日誌而不拋出例外，
        /// 呼叫端務必檢查回傳值——否則玩家會以為已經存檔，實際上資料已經掉了。
        /// </returns>
        public bool SaveCurrentSlot()
        {
            // 金鑰為空就直接失敗。目前沒有任何地方寫入 _dynamicCloudKey，
            // 所以 UseCloudKey = true 一定會走到這裡並明確報錯，而不是寫出壞檔。
            string key = GetActiveKey();
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[SaveMgr] 儲存失敗: 取不到有效的存檔金鑰");
                return false;
            }

            System.Diagnostics.Stopwatch sw = null;
            if (EnableDebugLogs)
            {
                sw = new System.Diagnostics.Stopwatch();
                sw.Start();
            }

            try
            {
                int newCount = GetSaveCount() + 1;
                Set(KEY_SAVE_COUNT, newCount);
                Set(KEY_LAST_SAVE_AT, DateTime.Now);
                byte[] rawData = _currentSaveData.ToByteArray();

                byte[] buf = new byte[HEADER_SIZE + rawData.Length];
                Buffer.BlockCopy(rawData, 0, buf, HEADER_SIZE, rawData.Length);

                byte flags = 0;
                if (EnableObfuscation)
                {
                    ApplyXorInPlace(buf, HEADER_SIZE, rawData.Length);
                    flags |= FLAG_OBFUSCATED;
                }
                if (_isTampered) flags |= FLAG_TAMPERED;

                // CRC 算的是「落地後的 payload」（混淆過就是混淆後的內容），
                // 才抓得到寫入中斷造成的半截檔。
                uint crc = Crc32(buf, HEADER_SIZE, rawData.Length);

                buf[0] = 0x42; buf[1] = 0x4D; buf[2] = 0x43; buf[3] = 0x53; // "BMCS"
                buf[4] = FORMAT_VERSION;
                buf[5] = (byte)(crc & 0xFF);
                buf[6] = (byte)((crc >> 8) & 0xFF);
                buf[7] = (byte)((crc >> 16) & 0xFF);
                buf[8] = (byte)((crc >> 24) & 0xFF);
                buf[9] = (byte)(rawData.Length & 0xFF);
                buf[10] = (byte)((rawData.Length >> 8) & 0xFF);
                buf[11] = (byte)((rawData.Length >> 16) & 0xFF);
                buf[12] = (byte)((rawData.Length >> 24) & 0xFF);
                buf[13] = flags;

                string path = GetPath(CurrentSlot);
                string tmpPath = path + ".tmp";
                File.WriteAllBytes(tmpPath, buf);

                // 落地方式：先寫 tmp，再原子替換，全程不會有「主檔不存在」的空窗。
                //
                // 不用 File.Move(src, dest, overwrite)：Unity 的 netstandard2.1 參考組件
                // 沒有那個三引數多載，編譯會噴 CS1501。
                // 不用 Delete + Move：兩者之間有一瞬間主檔是不存在的，那時斷電就整份存檔消失。
                //
                // 【前提】File.Replace 要求 tmp 與目的檔位於「同一個磁碟區」，跨磁碟區會失敗。
                // 目前 tmpPath 就是 path + ".tmp"，兩者都在 Application.persistentDataPath 底下，
                // 所以成立。日後若有人把 tmp 改到系統 temp 目錄（Path.GetTempPath()），
                // 這行就會壞掉——要改請連這段邏輯一起改。
                //
                // File.Replace 在目的檔不存在時會丟 FileNotFoundException，
                // 所以第一次存檔的分支不能省。兩條路徑都是 2 次檔案操作。
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }

                // v1 舊存檔升級完成，清掉已經沒人讀的簽章檔
                if (_pendingLegacyCleanup)
                {
                    _pendingLegacyCleanup = false;
                    try
                    {
                        string sigPath = GetSigPath(CurrentSlot);
                        if (File.Exists(sigPath)) File.Delete(sigPath);
                    }
                    catch (Exception cleanupEx)
                    {
                        // 清不掉只是留下一個沒人讀的垃圾檔，不該讓存檔本身算失敗
                        Debug.LogWarning($"[SaveMgr] 舊簽章檔清理失敗（不影響存檔）: {cleanupEx.Message}");
                    }
                }

                if (EnableDebugLogs && sw != null)
                {
                    sw.Stop();
                    Debug.Log($"<color=green>[SaveMgr] Slot {CurrentSlot} 存檔成功。總耗時: {sw.Elapsed.TotalMilliseconds:F2} ms</color>");
                }

                return true;
            }
            catch (Exception e)
            {
                // 印出完整例外而非只有 Message：像組件載入失敗這類問題，
                // 真正的線索都在內層例外與堆疊裡，只看 Message 查不出來。
                Debug.LogError($"[SaveMgr] 儲存失敗: {e}");
                return false;
            }
        }

        /// <summary>
        /// 流程：清空當前記憶體數據，重置竄改標記，並設定初始時間戳
        /// </summary>
        private void InitializeNewSave(int slot)
        {
            _currentSaveData = new PlayerSave();
            _isTampered = false;
            _pendingLegacyCleanup = false;
            var now = DateTime.Now;
            Set(KEY_CREATED_AT, now);
            Set(KEY_LAST_SAVE_AT, now);
            Set(KEY_SAVE_COUNT, 0);
        }

        #endregion

        #region 舊格式相容
        // 【唯讀】這整個 region 只為了把 v1 存檔轉上來，不要再擴充。
        // v1 版面：主檔 = XOR([junk(10~49) + protobuf])，
        //          簽章檔 = AES-256-CBC(meta)，meta =
        //          [0..31] SHA256(protobuf) / [32..39] int64 dataSize /
        //          [40..43] int32 saveCount / [44] 竄改旗標 / [45] junkSize / [46] 是否混淆。

        /// <summary>
        /// 流程：讀 v1 簽章檔 → AES 解密 meta → XOR 還原主檔 → 跳過 junk 取 dataSize → SHA256 校驗
        /// </summary>
        private void LoadFromLegacyV1(int slot, byte[] fileData)
        {
            string sigPath = GetSigPath(slot);
            if (!File.Exists(sigPath)) throw new Exception("Signature Block Missing");

            byte[] sigBytes = File.ReadAllBytes(sigPath);
            byte[] decryptedMeta = DecryptSignature(sigBytes, GetActiveKey());

            if (decryptedMeta.Length < 46) throw new Exception("Meta Corrupted");

            byte[] storedHash = new byte[32];
            Buffer.BlockCopy(decryptedMeta, 0, storedHash, 0, 32);
            long dataSize = BitConverter.ToInt64(decryptedMeta, 32);
            int junkSize = decryptedMeta[45];

            // 判斷該存檔當初是否有開啟混淆 (更舊的版本只有 46 bytes，預設為 true)
            bool isObfuscated = true;
            if (decryptedMeta.Length >= 47)
            {
                isObfuscated = decryptedMeta[46] == 1;
            }

            if (isObfuscated)
            {
                ApplyXorInPlace(fileData, 0, fileData.Length);
            }

            if (fileData.Length < junkSize + dataSize) throw new Exception("Data Size Error");

            byte[] rawData = new byte[dataSize];
            Buffer.BlockCopy(fileData, junkSize, rawData, 0, (int)dataSize);

            _currentSaveData = PlayerSave.Parser.ParseFrom(rawData);

            using (SHA256 sha256 = SHA256.Create())
            {
                _isTampered = !CompareHashes(storedHash, sha256.ComputeHash(rawData)) || decryptedMeta[44] == 1;
            }
        }

        /// <summary>
        /// 流程：提取資料前 16 位作為 IV，剩餘作為密文，進行 AES 解密後回傳原始數據
        /// </summary>
        private byte[] DecryptSignature(byte[] data, string key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                byte[] iv = new byte[16];
                byte[] cipher = new byte[data.Length - 16];
                Buffer.BlockCopy(data, 0, iv, 0, 16);
                Buffer.BlockCopy(data, 16, cipher, 0, cipher.Length);
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) return dec.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        /// <summary>
        /// 流程：逐位元組比對雜湊值，用於判定 v1 存檔的資料完整性
        /// </summary>
        private bool CompareHashes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        #endregion

        #region 工具類

        /// <summary>
        /// 流程：巡訪所有可能的插槽路徑，讀取檔案的基本資訊（如最後修改時間）供 UI 顯示
        /// </summary>
        public List<SlotMetadata> GetAllSlotsInfo(int maxSlots = 5)
        {
            var list = new List<SlotMetadata>();
            for (int i = 0; i < maxSlots; i++)
            {
                string path = GetPath(i);
                bool exists = File.Exists(path);
                var meta = new SlotMetadata { SlotIndex = i, IsExists = exists };
                if (exists)
                {
                    FileInfo info = new FileInfo(path);
                    meta.LastSaveTime = info.LastWriteTime.ToString("yyyy/MM/dd HH:mm");
                }
                list.Add(meta);
            }
            return list;
        }

        /// <summary>
        /// 流程：根據插槽索引移除主存檔檔案，並一併清掉可能殘留的 v1 簽名檔
        /// </summary>
        public void DeleteSlot(int slot)
        {
            string p = GetPath(slot); string s = GetSigPath(slot);
            if (File.Exists(p)) File.Delete(p);
            if (File.Exists(s)) File.Delete(s);
        }

        #endregion
    }

    [Serializable]
    public struct SlotMetadata
    {
        public int SlotIndex;
        public bool IsExists;
        public string LastSaveTime;
        public string Level;
    }
}
