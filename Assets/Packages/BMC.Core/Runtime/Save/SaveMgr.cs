using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using UnityEngine;

namespace BMC.Core
{
    /// <summary>
    /// SaveMgr: 高階混淆存檔管理系統
    /// 採用「混沌命名規則」與「噪音注入」，使存檔與校驗檔在系統中看起來完全不相關。
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        public int CurrentSlot { get; private set; } = 0;

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
        private const string KEY_ENCRYPTION_ID = "encryption_id";

        private string _dynamicCloudKey = string.Empty;
        private PlayerSave _currentSaveData = new PlayerSave();
        private bool _isTampered = false;

        #region 混沌路徑邏輯

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

        #endregion

        #region 核心存取接口

        public string GetCore(string key, string defaultValue = "") =>
            _currentSaveData.CoreData.TryGetValue(key, out var v) ? v : defaultValue;

        public int GetCoreInt(string key, int defaultValue = 0) =>
            int.TryParse(GetCore(key), out int result) ? result : defaultValue;

        public void SetCore(string key, object value) =>
            _currentSaveData.CoreData[key] = value.ToString();

        /// <summary>
        /// 流程：直接回傳記憶體中記錄的竄改標記，此標記在載入時由校驗邏輯決定
        /// </summary>
        public bool IsSaveTampered() => _isTampered;

        public string GetItem(string key, string defaultValue = "") => _currentSaveData.Items.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetItem(string key, object value) => _currentSaveData.Items[key] = value.ToString();
        public int GetItemInt(string key, int defaultValue = 0) => int.TryParse(GetItem(key), out int result) ? result : defaultValue;
        public string GetProgress(string key, string defaultValue = "") => _currentSaveData.Progress.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetProgress(string key, object value) => _currentSaveData.Progress[key] = value.ToString();

        #endregion

        #region 混淆讀取與寫入

        /// <summary>
        /// 讀取流程：
        /// 1. 計算混淆後的資料路徑與簽名路徑
        /// 2. 檢查檔案是否存在，缺失簽名檔則判定為竄改
        /// 3. 解密簽名檔（Metadata），取得原始資料大小與 Junk Data（噪音）長度
        /// 4. 讀取主檔案，跳過 Junk Data 位移，提取真正的 Protobuf 資料
        /// 5. 比對資料雜湊值與簽名檔內的雜湊是否一致
        /// 6. 更新記憶體中的 _isTampered 狀態供遊戲後續判定
        /// </summary>
        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);
            string sigPath = GetSigPath(slot);

            if (File.Exists(path))
            {
                try
                {
                    if (!File.Exists(sigPath)) throw new Exception("Signature Block Missing");

                    byte[] sigBytes = File.ReadAllBytes(sigPath);
                    byte[] decryptedMeta = DecryptSignature(sigBytes, GetActiveKey());

                    if (decryptedMeta.Length < 46) throw new Exception("Meta Corrupted");

                    byte[] storedHash = new byte[32];
                    Buffer.BlockCopy(decryptedMeta, 0, storedHash, 0, 32);
                    long dataSize = BitConverter.ToInt64(decryptedMeta, 32);
                    int junkSize = decryptedMeta[45];

                    byte[] allFileData = File.ReadAllBytes(path);
                    if (allFileData.Length < junkSize + dataSize) throw new Exception("Data Size Error");

                    byte[] rawData = new byte[dataSize];
                    Buffer.BlockCopy(allFileData, junkSize, rawData, 0, (int)dataSize);

                    _currentSaveData = PlayerSave.Parser.ParseFrom(rawData);

                    _isTampered = !CompareHashes(storedHash, SHA256.Create().ComputeHash(rawData)) || decryptedMeta[44] == 1;

                    if (_isTampered) Debug.LogWarning("<color=red>[SaveMgr] 校驗失敗：發現不一致的環境參數。</color>");
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
        /// 1. 更新內部核心數據（儲存次數、時間戳）
        /// 2. 將資料序列化為 Byte 陣列
        /// 3. 生成隨機長度（10-50 bytes）的 Junk Data 噪音
        /// 4. 計算資料雜湊，打包 Metadata（雜湊、大小、次數、竄改旗標、Junk長度）
        /// 5. 使用 AES 加密 Metadata 並獨立寫入簽名檔
        /// 6. 將 [Junk Data + 原始資料] 合併寫入主檔案，完成物理層的混淆
        /// </summary>
        public void SaveCurrentSlot()
        {
            string key = GetActiveKey();
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                int newCount = GetCoreInt(KEY_SAVE_COUNT, 0) + 1;
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

                string path = GetPath(CurrentSlot);
                using (var fs = File.Create(path + ".tmp"))
                {
                    fs.Write(junkData, 0, junkData.Length);
                    fs.Write(rawData, 0, rawData.Length);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);

                string sigPath = GetSigPath(CurrentSlot);
                File.WriteAllBytes(sigPath + ".tmp", encryptedSig);
                if (File.Exists(sigPath)) File.Delete(sigPath);
                File.Move(sigPath + ".tmp", sigPath);
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