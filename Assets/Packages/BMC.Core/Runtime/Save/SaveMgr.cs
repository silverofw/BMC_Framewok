using System;
using System.IO;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace BMC.Core
{
    /// <summary>
    /// SaveMgr: 處理基於 Protobuf 的多存檔插槽管理系統
    /// 承襲專案既有的 Singleton 類別，適用於 Steam Cloud 與本地路徑
    /// </summary>
    public class SaveMgr : Singleton<SaveMgr>
    {
        [Header("存檔設定")]
        public int CurrentSlot = 0;
        public const string SaveFilePrefix = "ps_";
        public const string SaveFileExtension = ".dat";

        private PlayerSave _currentSaveData = new PlayerSave();

        #region 核心存取接口 (Core, Items, Progress)

        // --- Core Data (核心數值) ---
        public string GetCore(string key, string defaultValue = "") =>
            _currentSaveData.CoreData.TryGetValue(key, out var v) ? v : defaultValue;

        public int GetCoreInt(string key, int defaultValue = 0) =>
            int.TryParse(GetCore(key), out int result) ? result : defaultValue;

        public bool GetCoreBool(string key, bool defaultValue = false) =>
            bool.TryParse(GetCore(key), out bool result) ? result : defaultValue;

        public void SetCore(string key, object value) =>
            _currentSaveData.CoreData[key] = value.ToString();


        // --- Items (道具資料) ---
        public string GetItem(string key, string defaultValue = "") =>
            _currentSaveData.Items.TryGetValue(key, out var v) ? v : defaultValue;

        // 新增：取得道具數量或整數值
        public int GetItemInt(string key, int defaultValue = 0) =>
            int.TryParse(GetItem(key), out int result) ? result : defaultValue;

        public void SetItem(string key, object value) =>
            _currentSaveData.Items[key] = value.ToString();

        public bool HasItem(string key) => _currentSaveData.Items.ContainsKey(key);


        // --- Progress (遊戲進度) ---
        public string GetProgress(string key, string defaultValue = "") =>
            _currentSaveData.Progress.TryGetValue(key, out var v) ? v : defaultValue;

        public bool GetProgressBool(string key, bool defaultValue = false) =>
            bool.TryParse(GetProgress(key), out bool result) ? result : defaultValue;

        public void SetProgress(string key, object value) =>
            _currentSaveData.Progress[key] = value.ToString();

        #endregion

        #region 檔案操作邏輯 (Steam 友善)

        /// <summary>
        /// 取得指定插槽的完整檔案路徑
        /// </summary>
        public string GetPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"{SaveFilePrefix}{slot}{SaveFileExtension}");
        }

        /// <summary>
        /// 切換並讀取存檔插槽
        /// </summary>
        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);

            if (File.Exists(path))
            {
                try
                {
                    using (var input = File.OpenRead(path))
                    {
                        _currentSaveData = PlayerSave.Parser.ParseFrom(input);
                        Debug.Log($"<color=cyan>[SaveMgr] 成功載入存檔位 {slot}</color>");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveMgr] 讀取存檔位 {slot} 失敗，檔案可能損壞: {e.Message}");
                    _currentSaveData = new PlayerSave();
                }
            }
            else
            {
                _currentSaveData = new PlayerSave();
                Debug.Log($"<color=yellow>[SaveMgr] 存檔位 {slot} 不存在，已初始化新資料</color>");
            }
        }

        /// <summary>
        /// 儲存當前資料 (使用原子寫入機制：先寫臨時檔再覆蓋，防止存檔毀損)
        /// </summary>
        public void SaveCurrentSlot()
        {
            string path = GetPath(CurrentSlot);
            string tempPath = path + ".tmp";

            try
            {
                // 1. 寫入臨時檔案
                using (var output = File.Create(tempPath))
                {
                    _currentSaveData.WriteTo(output);
                }

                // 2. 寫入成功後覆蓋正式檔案
                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);

                Debug.Log($"<color=green>[SaveMgr] 資料已成功儲存至 Slot {CurrentSlot}</color>");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveMgr] 儲存失敗: {e.Message}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        /// <summary>
        /// 刪除指定存檔
        /// </summary>
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

        #region 工具類功能

        /// <summary>
        /// 快速掃描所有存檔插槽的狀態 (供 UI 顯示使用)
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