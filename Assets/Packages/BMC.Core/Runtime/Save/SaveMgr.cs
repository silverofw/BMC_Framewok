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
    /// SaveMgr: 處理基於 Protobuf 的多存檔插槽管理系統
    /// 採用「明文資料」與「獨立加密簽名檔 (Signature File)」分離機制。
    /// 竄改標記鎖在加密的簽名檔內，主存檔保持純淨的明碼格式。
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        public int CurrentSlot { get; private set; } = 0;

        public const string SaveFilePrefix = "ps_";
        public const string SaveFileExtension = ".dat";
        public const string SigFileSuffix = "_s"; // 簽名檔後綴

        [Tooltip("是否啟用雲端動態金鑰？關閉則使用本地預設金鑰")]
        public bool UseCloudKey = false;

        private const string _localFallbackKey = "Bmc_Local_Fallback_Key_2026_!#99";

        // 存檔字典內部的常數 Key
        private const string KEY_CREATED_AT = "created_at";
        private const string KEY_LAST_SAVE_AT = "last_save_at";
        private const string KEY_SAVE_COUNT = "save_count";
        private const string KEY_ENCRYPTION_ID = "encryption_id";

        private string _dynamicCloudKey = string.Empty;
        private PlayerSave _currentSaveData = new PlayerSave();

        // 僅存於記憶體的狀態，讀取時會從獨立的簽名檔中還原
        private bool _isTampered = false;

        #region 金鑰管理

        private string GetActiveKey()
        {
            return UseCloudKey ? _dynamicCloudKey : _localFallbackKey;
        }

        public void SetDynamicKey(string key, string keyVersion)
        {
            if (!UseCloudKey) return;
            _dynamicCloudKey = key;
            SetCore(KEY_ENCRYPTION_ID, keyVersion);
            Debug.Log($"<color=orange>[SaveMgr] 雲端金鑰已注入。版本: {keyVersion}</color>");
        }

        public void ClearDynamicKey() => _dynamicCloudKey = string.Empty;

        #endregion

        #region 核心存取接口

        public string GetCore(string key, string defaultValue = "") =>
            _currentSaveData.CoreData.TryGetValue(key, out var v) ? v : defaultValue;

        public int GetCoreInt(string key, int defaultValue = 0) =>
            int.TryParse(GetCore(key), out int result) ? result : defaultValue;

        public bool GetCoreBool(string key, bool defaultValue = false) =>
            bool.TryParse(GetCore(key), out bool result) ? result : defaultValue;

        public void SetCore(string key, object value) =>
            _currentSaveData.CoreData[key] = value.ToString();

        public DateTime GetCreatedAtDateTime()
        {
            string ticksStr = GetCore(KEY_CREATED_AT);
            return long.TryParse(ticksStr, out long ticks) ? new DateTime(ticks) : DateTime.MinValue;
        }

        public DateTime GetLastSaveAtDateTime()
        {
            string ticksStr = GetCore(KEY_LAST_SAVE_AT);
            return long.TryParse(ticksStr, out long ticks) ? new DateTime(ticks) : DateTime.MinValue;
        }

        public int GetSaveCount() => GetCoreInt(KEY_SAVE_COUNT, 0);

        /// <summary>
        /// 檢查此存檔是否曾被外部竄改 (此狀態加密存儲於獨立的簽名檔中)
        /// </summary>
        public bool IsSaveTampered() => _isTampered;

        public string GetItem(string key, string defaultValue = "") =>
            _currentSaveData.Items.TryGetValue(key, out var v) ? v : defaultValue;

        public int GetItemInt(string key, int defaultValue = 0) =>
            int.TryParse(GetItem(key), out int result) ? result : defaultValue;

        public void SetItem(string key, object value) =>
            _currentSaveData.Items[key] = value.ToString();

        public bool HasItem(string key) => _currentSaveData.Items.ContainsKey(key);

        public string GetProgress(string key, string defaultValue = "") =>
            _currentSaveData.Progress.TryGetValue(key, out var v) ? v : defaultValue;

        public bool GetProgressBool(string key, bool defaultValue = false) =>
            bool.TryParse(GetProgress(key), out bool result) ? result : defaultValue;

        public void SetProgress(string key, object value) =>
            _currentSaveData.Progress[key] = value.ToString();

        #endregion

        #region 檔案操作與校驗

        /// <summary>
        /// 取得主資料檔路徑
        /// </summary>
        public string GetPath(int slot) => Path.Combine(Application.persistentDataPath, $"{SaveFilePrefix}{slot}{SaveFileExtension}");

        /// <summary>
        /// 取得簽名校驗檔路徑
        /// </summary>
        public string GetSigPath(int slot) => Path.Combine(Application.persistentDataPath, $"{SaveFilePrefix}{slot}{SigFileSuffix}{SaveFileExtension}");

        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);
            string sigPath = GetSigPath(slot);

            if (File.Exists(path))
            {
                try
                {
                    // 1. 讀取主資料檔案 (明文 Protobuf)
                    byte[] rawData = File.ReadAllBytes(path);
                    _currentSaveData = PlayerSave.Parser.ParseFrom(rawData);

                    // 2. 嘗試讀取並驗證簽名檔
                    if (File.Exists(sigPath))
                    {
                        byte[] signatureBlock = File.ReadAllBytes(sigPath);
                        string key = GetActiveKey();
                        byte[] decryptedCheckpoint = DecryptSignature(signatureBlock, key);

                        _isTampered = !ValidateIntegrity(rawData, decryptedCheckpoint);

                        // 檢查歷史竄改記錄 (存於加密塊第 45 字節)
                        if (!_isTampered && decryptedCheckpoint.Length >= 45)
                        {
                            _isTampered = decryptedCheckpoint[44] == 1;
                        }
                    }
                    else
                    {
                        // 缺少簽名檔直接判定為異常
                        _isTampered = true;
                    }

                    if (_isTampered)
                    {
                        Debug.LogWarning($"<color=red>[SaveMgr] 插槽 {slot} 校驗異常，已標記內部狀態。</color>");
                    }
                    else
                    {
                        Debug.Log($"<color=cyan>[SaveMgr] 插槽 {slot} 驗證成功並載入。</color>");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveMgr] 載入插槽 {slot} 失敗: {e.Message}");
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

            string path = GetPath(CurrentSlot);
            string sigPath = GetSigPath(slot: CurrentSlot);

            string tempPath = path + ".tmp";
            string tempSigPath = sigPath + ".tmp";

            try
            {
                // 更新統計
                int newCount = GetSaveCount() + 1;
                SetCore(KEY_SAVE_COUNT, newCount);
                SetCore(KEY_LAST_SAVE_AT, DateTime.Now.Ticks.ToString());
                if (!UseCloudKey) SetCore(KEY_ENCRYPTION_ID, "local_default");

                // 1. 序列化主資料
                byte[] rawData = _currentSaveData.ToByteArray();

                // 2. 建立檢查點內容
                byte[] hash = SHA256.Create().ComputeHash(rawData);
                byte[] checkpoint = new byte[45];
                Buffer.BlockCopy(hash, 0, checkpoint, 0, 32);
                Buffer.BlockCopy(BitConverter.GetBytes((long)rawData.Length), 0, checkpoint, 32, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(newCount), 0, checkpoint, 40, 4);
                checkpoint[44] = (byte)(_isTampered ? 1 : 0);

                // 3. 加密檢查點生成簽名塊
                byte[] encryptedSignature = EncryptSignature(checkpoint, key);

                // 4. 分別寫入兩個檔案
                File.WriteAllBytes(tempPath, rawData);
                File.WriteAllBytes(tempSigPath, encryptedSignature);

                // 原子覆蓋主檔案
                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);

                // 原子覆蓋簽名檔
                if (File.Exists(sigPath)) File.Delete(sigPath);
                File.Move(tempSigPath, sigPath);

                Debug.Log($"<color=green>[SaveMgr] 存檔成功 (Slot {CurrentSlot})，資料與簽名已分離儲存。</color>");
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
            string nowTicks = DateTime.Now.Ticks.ToString();
            SetCore(KEY_CREATED_AT, nowTicks);
            SetCore(KEY_LAST_SAVE_AT, nowTicks);
            SetCore(KEY_SAVE_COUNT, 0);
            Debug.Log($"<color=yellow>[SaveMgr] 插槽 {slot} 初始化。</color>");
        }

        private bool ValidateIntegrity(byte[] rawData, byte[] checkpoint)
        {
            if (checkpoint.Length < 44) return false;

            byte[] storedHash = new byte[32];
            Buffer.BlockCopy(checkpoint, 0, storedHash, 0, 32);
            long storedLength = BitConverter.ToInt64(checkpoint, 32);

            if (rawData.Length != storedLength) return false;

            byte[] currentHash = SHA256.Create().ComputeHash(rawData);
            for (int i = 0; i < 32; i++)
            {
                if (currentHash[i] != storedHash[i]) return false;
            }
            return true;
        }

        #endregion

        #region AES 簽名加密實作

        private byte[] EncryptSignature(byte[] checkpoint, string key)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.GenerateIV();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(checkpoint, 0, checkpoint.Length);
                    byte[] combined = new byte[aes.IV.Length + encrypted.Length];
                    Buffer.BlockCopy(aes.IV, 0, combined, 0, 16);
                    Buffer.BlockCopy(encrypted, 0, combined, 16, encrypted.Length);
                    return combined;
                }
            }
        }

        private byte[] DecryptSignature(byte[] signature, string key)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                byte[] iv = new byte[16];
                byte[] ciphertext = new byte[signature.Length - 16];
                Buffer.BlockCopy(signature, 0, iv, 0, 16);
                Buffer.BlockCopy(signature, 16, ciphertext, 0, ciphertext.Length);
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
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
            string path = GetPath(slot);
            string sigPath = GetSigPath(slot);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(sigPath)) File.Delete(sigPath);
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