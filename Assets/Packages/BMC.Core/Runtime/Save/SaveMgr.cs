using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Google.Protobuf;
using UnityEngine;
using Debug = UnityEngine.Debug; // 確保使用 Unity 的 Debug

namespace BMC.Core
{
    /// <summary>
    /// SaveMgr: 高階混淆存檔管理系統
    /// 採用「混沌命名」、「動態噪音注入」以及「XOR 全檔案混淆」，確保檔案在任何文字編輯器中均為不可讀亂碼。
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        public int CurrentSlot { get; private set; } = 0;

        [Header("偵錯設定")]
        [Tooltip("是否顯示存檔/讀檔耗時等偵錯日誌")]
        public bool EnableDebugLogs = false;

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

        private string _dynamicCloudKey = string.Empty;
        private PlayerSave _currentSaveData = new PlayerSave();
        private bool _isTampered = false;

        #region 混沌路徑與 XOR 邏輯

        /// <summary>
        /// 流程：根據當前設定（雲端或本地）回傳相對應的 AES 金鑰字串
        /// </summary>
        private string GetActiveKey() => UseCloudKey ? _dynamicCloudKey : _localFallbackKey;

        /// <summary>
        /// 流程：將 Slot 索引結合資料鹽值進行 SHA256 雜湊，取前 10 位作為混淆檔名，組合成主存檔路徑
        /// </summary>
        public string GetPath(int slot)
        {
            string hash = GetHash(slot.ToString() + DATA_SALT).Substring(0, 10);
            return Path.Combine(Application.persistentDataPath, $"{DATA_PREFIX}{hash}{DATA_EXT}");
        }

        /// <summary>
        /// 流程：使用非線性運算混淆 Slot 索引，結合簽名鹽值進行雜湊，產生與主存檔完全無關聯的校驗檔路徑
        /// </summary>
        public string GetSigPath(int slot)
        {
            string chaoticInput = ((slot * 31) ^ 0x5F3759DF).ToString() + SIG_SALT;
            string hash = GetHash(chaoticInput).Substring(5, 16).ToLower();
            return Path.Combine(Application.persistentDataPath, $"{SIG_PREFIX}{hash}{SIG_EXT}");
        }

        /// <summary>
        /// 流程：標準 SHA256 雜湊計算，回傳大寫十六進位字串
        /// </summary>
        private string GetHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        /// <summary>
        /// 流程：使用 XOR 演算法對資料進行混淆/還原。
        /// 這是輕量級的處理，目的是讓檔案在記事本中顯示為不可讀的亂碼。
        /// </summary>
        private void ApplyXorInPlace(byte[] data)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(GetActiveKey());
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= keyBytes[i % keyBytes.Length];
            }
        }

        #endregion

        #region 核心存取接口

        public string GetCore(string key, string defaultValue = "") =>
            _currentSaveData.CoreData.TryGetValue(key, out var v) ? v : defaultValue;

        public int GetCoreInt(string key, int defaultValue = 0) =>
            int.TryParse(GetCore(key), out int result) ? result : defaultValue;

        public void SetCore(string key, object value) =>
            _currentSaveData.CoreData[key] = value.ToString();

        /// <summary>
        /// 流程：從 CoreData 中取得時間 Ticks 字串並轉換為 DateTime 格式
        /// </summary>
        public DateTime GetCoreDatetime(string key)
        {
            string ticksStr = GetCore(key);
            return long.TryParse(ticksStr, out long ticks) ? new DateTime(ticks) : DateTime.MinValue;
        }

        /// <summary>
        /// 流程：取得存檔最初建立的時間
        /// </summary>
        public DateTime GetCreatedAt() => GetCoreDatetime(KEY_CREATED_AT);

        /// <summary>
        /// 流程：取得最後一次紀錄的時間
        /// </summary>
        public DateTime GetLastSaveAt() => GetCoreDatetime(KEY_LAST_SAVE_AT);

        /// <summary>
        /// 流程：取得此存檔累計的儲存次數
        /// </summary>
        public int GetSaveCount() => GetCoreInt(KEY_SAVE_COUNT, 0);

        /// <summary>
        /// 流程：直接回傳記憶體中記錄的竄改標記，此標記在載入時由校驗邏輯決定
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
        public bool GetProgressBool(string key, bool defaultValue = false) => bool.TryParse(GetProgress(key), out bool result) ? result : defaultValue;

        #endregion

        #region 混淆讀取與寫入

        /// <summary>
        /// 讀取流程：
        /// 1. 啟動計時器 (若開啟 EnableDebugLogs)
        /// 2. 取得檔案路徑並讀取校驗檔（Metadata）
        /// 3. 解密校驗檔取得 JunkSize 與原始雜湊
        /// 4. 讀取主存檔並進行 XOR 反混淆，使其恢復為 [Junk + Proto] 格式
        /// 5. 跳過 Junk 位移提取 Protobuf，進行雜湊校驗
        /// 6. 更新記憶體中的 _isTampered 狀態供遊戲後續判定
        /// 7. 停止計時並打印耗時
        /// </summary>
        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);
            string sigPath = GetSigPath(slot);

            Stopwatch sw = null;
            if (EnableDebugLogs)
            {
                sw = new Stopwatch();
                sw.Start();
            }

            if (File.Exists(path))
            {
                try
                {
                    if (!File.Exists(sigPath)) throw new Exception("Signature Block Missing");

                    // 1. 讀取並解密校驗檔
                    byte[] sigBytes = File.ReadAllBytes(sigPath);
                    byte[] decryptedMeta = DecryptSignature(sigBytes, GetActiveKey());

                    if (decryptedMeta.Length < 46) throw new Exception("Meta Corrupted");

                    byte[] storedHash = new byte[32];
                    Buffer.BlockCopy(decryptedMeta, 0, storedHash, 0, 32);
                    long dataSize = BitConverter.ToInt64(decryptedMeta, 32);
                    int junkSize = decryptedMeta[45];

                    // 2. 讀取主檔案並執行 XOR 還原
                    byte[] obfuscatedData = File.ReadAllBytes(path);
                    ApplyXorInPlace(obfuscatedData);

                    if (obfuscatedData.Length < junkSize + dataSize) throw new Exception("Data Size Error");

                    // 3. 提取真正的 Protobuf 資料
                    byte[] rawData = new byte[dataSize];
                    Buffer.BlockCopy(obfuscatedData, junkSize, rawData, 0, (int)dataSize);

                    _currentSaveData = PlayerSave.Parser.ParseFrom(rawData);

                    // 4. 校驗完整性
                    _isTampered = !CompareHashes(storedHash, SHA256.Create().ComputeHash(rawData)) || decryptedMeta[44] == 1;

                    if (_isTampered) Debug.LogWarning("<color=red>[SaveMgr] 校驗失敗：發現不一致的環境參數。</color>");

                    if (EnableDebugLogs && sw != null)
                    {
                        sw.Stop();
                        Debug.Log($"<color=cyan>[SaveMgr] Slot {slot} 讀檔成功。總耗時: {sw.Elapsed.TotalMilliseconds:F2} ms</color>");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveMgr] 載入異常: {e.Message}");
                    InitializeNewSave(slot);
                }
            }
            else
            {
                InitializeNewSave(slot);
            }
        }

        /// <summary>
        /// 儲存流程：
        /// 1. 啟動計時器 (若開啟 EnableDebugLogs)
        /// 2. 序列化資料並生成隨機 Junk 噪音
        /// 3. 計算雜湊並加密儲存在獨立的校驗檔中
        /// 4. 合併 [Junk + Data] 並執行 XOR 混淆處理
        /// 5. 寫入主檔案，使其內容在記事本中完全無法辨識
        /// 6. 停止計時並打印耗時
        /// </summary>
        public void SaveCurrentSlot()
        {
            string key = GetActiveKey();
            if (string.IsNullOrEmpty(key)) return;

            Stopwatch sw = null;
            if (EnableDebugLogs)
            {
                sw = new Stopwatch();
                sw.Start();
            }

            try
            {
                int newCount = GetSaveCount() + 1;
                SetCore(KEY_SAVE_COUNT, newCount);
                SetCore(KEY_LAST_SAVE_AT, DateTime.Now.Ticks.ToString());
                byte[] rawData = _currentSaveData.ToByteArray();

                int junkSize = UnityEngine.Random.Range(10, 50);
                byte[] junkData = new byte[junkSize];
                new System.Random().NextBytes(junkData);

                byte[] hash = SHA256.Create().ComputeHash(rawData);
                byte[] meta = new byte[46];
                Buffer.BlockCopy(hash, 0, meta, 0, 32);
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawData.Length), 0, meta, 32, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(newCount), 0, meta, 40, 4);
                meta[44] = (byte)(_isTampered ? 1 : 0);
                meta[45] = (byte)junkSize;

                byte[] encryptedSig = EncryptSignature(meta, key);

                byte[] finalPayload = new byte[junkData.Length + rawData.Length];
                Buffer.BlockCopy(junkData, 0, finalPayload, 0, junkData.Length);
                Buffer.BlockCopy(rawData, 0, finalPayload, junkData.Length, rawData.Length);

                ApplyXorInPlace(finalPayload);

                string path = GetPath(CurrentSlot);
                File.WriteAllBytes(path + ".tmp", finalPayload);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);

                string sigPath = GetSigPath(CurrentSlot);
                File.WriteAllBytes(sigPath + ".tmp", encryptedSig);
                if (File.Exists(sigPath)) File.Delete(sigPath);
                File.Move(sigPath + ".tmp", sigPath);

                if (EnableDebugLogs && sw != null)
                {
                    sw.Stop();
                    Debug.Log($"<color=green>[SaveMgr] Slot {CurrentSlot} 存檔成功。總耗時: {sw.Elapsed.TotalMilliseconds:F2} ms</color>");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveMgr] 儲存失敗: {e.Message}");
            }
        }

        /// <summary>
        /// 流程：清空當前記憶體數據，重置竄改標記，並設定初始時間戳
        /// </summary>
        private void InitializeNewSave(int slot)
        {
            _currentSaveData = new PlayerSave();
            _isTampered = false;
            string now = DateTime.Now.Ticks.ToString();
            SetCore(KEY_CREATED_AT, now);
            SetCore(KEY_LAST_SAVE_AT, now);
            SetCore(KEY_SAVE_COUNT, 0);
        }

        /// <summary>
        /// 流程：逐位元組比對雜湊值，用於判定資料完整性
        /// </summary>
        private bool CompareHashes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        #endregion

        #region AES 實作

        /// <summary>
        /// 流程：建立 AES 加密器，生成隨機 IV，將 IV 附加在密文前方並回傳完整 Byte 陣列
        /// </summary>
        private byte[] EncryptSignature(byte[] data, string key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.GenerateIV();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var enc = aes.CreateEncryptor())
                {
                    byte[] encrypted = enc.TransformFinalBlock(data, 0, data.Length);
                    byte[] combined = new byte[aes.IV.Length + encrypted.Length];
                    Buffer.BlockCopy(aes.IV, 0, combined, 0, 16);
                    Buffer.BlockCopy(encrypted, 0, combined, 16, encrypted.Length);
                    return combined;
                }
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
        /// 流程：根據插槽索引同時移除主存檔檔案與對應的加密簽名檔
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