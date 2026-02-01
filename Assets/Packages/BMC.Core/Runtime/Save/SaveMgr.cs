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
    /// 支援動態雲端金鑰開關、AES 隨機 IV 加密、高精度時間記錄與存檔次數統計
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        public int CurrentSlot { get; private set; } = 0;

        public const string SaveFilePrefix = "ps_";
        public const string SaveFileExtension = ".dat";

        // --- 雲端金鑰配置 ---
        [Tooltip("是否啟用雲端動態金鑰？關閉則使用本地預設金鑰")]
        public bool UseCloudKey = false;

        // 本地備援金鑰 (當 UseCloudKey 為 false 時使用)
        // 建議正式發布前修改此字串
        private const string _localFallbackKey = "Bmc_Local_Fallback_Key_2026_!#";

        // 存檔字典內部的常數 Key
        private const string KEY_CREATED_AT = "created_ticks";
        private const string KEY_LAST_SAVE_AT = "last_save_ticks";
        private const string KEY_SAVE_COUNT = "save_count";
        private const string KEY_ENCRYPTION_ID = "key_version";

        // 動態金鑰變數
        private string _dynamicCloudKey = string.Empty;

        private PlayerSave _currentSaveData = new PlayerSave();

        #region 金鑰管理

        /// <summary>
        /// 取得目前應該使用的加解密金鑰
        /// </summary>
        private string GetActiveKey()
        {
            if (UseCloudKey)
            {
                return _dynamicCloudKey;
            }
            return _localFallbackKey;
        }

        /// <summary>
        /// 從雲端取得金鑰後注入此處
        /// </summary>
        public void SetDynamicKey(string key, string keyVersion)
        {
            if (!UseCloudKey)
            {
                Debug.LogWarning("[SaveMgr] 已關閉雲端金鑰功能，注入動作已被忽略。");
                return;
            }
            _dynamicCloudKey = key;
            SetCore(KEY_ENCRYPTION_ID, keyVersion);
            Debug.Log($"<color=orange>[SaveMgr] 雲端金鑰已注入。版本: {keyVersion}</color>");
        }

        /// <summary>
        /// 抹除記憶體中的動態金鑰
        /// </summary>
        public void ClearDynamicKey()
        {
            _dynamicCloudKey = string.Empty;
        }

        #endregion

        #region 核心存取接口 (Core, Items, Progress)

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

        #region 檔案操作與加密邏輯

        public string GetPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"{SaveFilePrefix}{slot}{SaveFileExtension}");
        }

        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);

            if (File.Exists(path))
            {
                try
                {
                    string key = GetActiveKey();
                    if (string.IsNullOrEmpty(key))
                    {
                        throw new Exception("加密金鑰尚未就緒 (雲端模式可能尚未注入金鑰)。");
                    }

                    byte[] encryptedData = File.ReadAllBytes(path);
                    byte[] decryptedData = Decrypt(encryptedData, key);

                    _currentSaveData = PlayerSave.Parser.ParseFrom(decryptedData);
                    Debug.Log($"<color=cyan>[SaveMgr] 插槽 {slot} 載入成功。金鑰模式: {(UseCloudKey ? "Cloud" : "Local")}</color>");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveMgr] 讀取或解密插槽 {slot} 失敗: {e.Message}");
                    InitializeNewSave(slot);
                }
            }
            else
            {
                InitializeNewSave(slot);
            }
        }

        private void InitializeNewSave(int slot)
        {
            _currentSaveData = new PlayerSave();

            string nowTicks = DateTime.Now.Ticks.ToString();
            SetCore(KEY_CREATED_AT, nowTicks);
            SetCore(KEY_LAST_SAVE_AT, nowTicks);
            SetCore(KEY_SAVE_COUNT, 0);

            if (!UseCloudKey)
            {
                SetCore(KEY_ENCRYPTION_ID, "local_default");
            }

            Debug.Log($"<color=yellow>[SaveMgr] 存檔位 {slot} 已初始化。</color>");
        }

        public void SaveCurrentSlot()
        {
            string key = GetActiveKey();
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[SaveMgr] 儲存失敗：當前無可用金鑰。");
                return;
            }

            string path = GetPath(CurrentSlot);
            string tempPath = path + ".tmp";

            try
            {
                int newCount = GetSaveCount() + 1;
                SetCore(KEY_SAVE_COUNT, newCount);
                SetCore(KEY_LAST_SAVE_AT, DateTime.Now.Ticks.ToString());

                // 如果是本地模式，確保版本欄位正確
                if (!UseCloudKey) SetCore(KEY_ENCRYPTION_ID, "local_default");

                byte[] rawData = _currentSaveData.ToByteArray();
                byte[] encryptedData = Encrypt(rawData, key);

                File.WriteAllBytes(tempPath, encryptedData);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);

                Debug.Log($"<color=green>[SaveMgr] 存檔成功 (Slot {CurrentSlot})。</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveMgr] 加密儲存失敗: {e.Message}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public void DeleteSlot(int slot)
        {
            string path = GetPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SaveMgr] 插槽 {slot} 檔案已移除");
            }
        }

        #endregion

        #region AES 隨機 IV 加密/解密實作

        private byte[] Encrypt(byte[] data, string key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.GenerateIV();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
                    byte[] combined = new byte[aes.IV.Length + encrypted.Length];
                    Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
                    Buffer.BlockCopy(encrypted, 0, combined, aes.IV.Length, encrypted.Length);
                    return combined;
                }
            }
        }

        private byte[] Decrypt(byte[] data, string key)
        {
            if (data.Length < 16) throw new Exception("無效的加密資料");

            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                byte[] iv = new byte[16];
                byte[] ciphertext = new byte[data.Length - 16];
                Buffer.BlockCopy(data, 0, iv, 0, 16);
                Buffer.BlockCopy(data, 16, ciphertext, 0, ciphertext.Length);

                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
            }
        }

        #endregion

        #region 工具類功能

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