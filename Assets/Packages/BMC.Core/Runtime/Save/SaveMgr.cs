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
        // 主資料檔案偽裝
        private const string DATA_PREFIX = "pkg_";
        private const string DATA_EXT = ".dat";
        private const string DATA_SALT = "Bmc_Data_Unique_Salt_#99";

        // 校驗簽名檔 (vinfo) 偽裝 - 讓它看起來像系統索引或清單
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

        private string GetActiveKey() => UseCloudKey ? _dynamicCloudKey : _localFallbackKey;

        /// <summary>
        /// 計算主資料檔名：使用 Data Salt 產生 10 位哈希
        /// </summary>
        public string GetPath(int slot)
        {
            string hash = GetHash(slot.ToString() + DATA_SALT).Substring(0, 10);
            return Path.Combine(Application.persistentDataPath, $"{DATA_PREFIX}{hash}{DATA_EXT}");
        }

        /// <summary>
        /// 計算校驗檔 (vinfo) 檔名：使用 Sig Salt 產生 16 位哈希，
        /// 並且在雜湊中混入位移運算，使其與主檔案完全不對稱。
        /// </summary>
        public string GetSigPath(int slot)
        {
            // 使用非線性運算進一步打亂索引關係
            string chaoticInput = ((slot * 31) ^ 0x5F3759DF).ToString() + SIG_SALT;
            string hash = GetHash(chaoticInput).Substring(5, 16).ToLower();
            return Path.Combine(Application.persistentDataPath, $"{SIG_PREFIX}{hash}{SIG_EXT}");
        }

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

        public bool IsSaveTampered() => _isTampered;

        public string GetItem(string key, string defaultValue = "") => _currentSaveData.Items.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetItem(string key, object value) => _currentSaveData.Items[key] = value.ToString();
        public int GetItemInt(string key, int defaultValue = 0) => int.TryParse(GetItem(key), out int result) ? result : defaultValue;
        public string GetProgress(string key, string defaultValue = "") => _currentSaveData.Progress.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetProgress(string key, object value) => _currentSaveData.Progress[key] = value.ToString();

        #endregion

        #region 混淆讀取與寫入

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

                    // 校驗雜湊與隱藏的竄改旗標
                    _isTampered = !CompareHashes(storedHash, SHA256.Create().ComputeHash(rawData)) || decryptedMeta[44] == 1;

                    if (_isTampered) Debug.LogWarning("<color=red>[SaveMgr] 校驗失敗：發現不一致的環境參數。</color>");
                    else Debug.Log($"<color=cyan>[SaveMgr] 插槽 {slot} 成功載入。</color>");
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

                // 生成 10-50 bytes 的隨機噪音
                int junkSize = UnityEngine.Random.Range(10, 50);
                byte[] junkData = new byte[junkSize];
                new System.Random().NextBytes(junkData);

                // 加密 Meta: [Hash(32)][Size(8)][Count(4)][Tamper(1)][JunkSize(1)]
                byte[] hash = SHA256.Create().ComputeHash(rawData);
                byte[] meta = new byte[46];
                Buffer.BlockCopy(hash, 0, meta, 0, 32);
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawData.Length), 0, meta, 32, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(newCount), 0, meta, 40, 4);
                meta[44] = (byte)(_isTampered ? 1 : 0);
                meta[45] = (byte)junkSize;

                byte[] encryptedSig = EncryptSignature(meta, key);

                // 寫入主檔案 (噪音 + 資料)
                string path = GetPath(CurrentSlot);
                using (var fs = File.Create(path + ".tmp"))
                {
                    fs.Write(junkData, 0, junkData.Length);
                    fs.Write(rawData, 0, rawData.Length);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);

                // 寫入校驗檔 (Sig Path)
                string sigPath = GetSigPath(CurrentSlot);
                File.WriteAllBytes(sigPath + ".tmp", encryptedSig);
                if (File.Exists(sigPath)) File.Delete(sigPath);
                File.Move(sigPath + ".tmp", sigPath);

                Debug.Log($"<color=green>[SaveMgr] 插槽 {CurrentSlot} 存檔成功 (已更新混亂校驗)。</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveMgr] 儲存失敗: {e.Message}");
            }
        }

        private void InitializeNewSave(int slot)
        {
            _currentSaveData = new PlayerSave();
            _isTampered = false;
            string now = DateTime.Now.Ticks.ToString();
            SetCore(KEY_CREATED_AT, now);
            SetCore(KEY_LAST_SAVE_AT, now);
            SetCore(KEY_SAVE_COUNT, 0);
        }

        private bool CompareHashes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        #endregion

        #region AES 實作

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