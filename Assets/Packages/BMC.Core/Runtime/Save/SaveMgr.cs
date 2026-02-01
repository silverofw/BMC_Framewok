using System.IO;
using Google.Protobuf;
using UnityEngine;

namespace BMC.Core
{
    public class SaveMgr : Singleton<SaveMgr>
    {
        // 當前玩家正在使用的插槽 (0, 1, 2...)
        public int CurrentSlot = 0;

        // 取得指定插槽的路徑
        private string GetPath(int slot)
        {
            // 檔名範例：player_save_0.dat, player_save_1.dat
            return Path.Combine(Application.persistentDataPath, $"ps_{slot}.dat");
        }

        private PlayerSave _currentSaveData = new PlayerSave();

        // --- 核心：切換並讀取存檔 ---
        public void SwitchAndLoadSlot(int slot)
        {
            CurrentSlot = slot;
            string path = GetPath(slot);

            if (File.Exists(path))
            {
                using (var input = File.OpenRead(path))
                {
                    _currentSaveData = PlayerSave.Parser.ParseFrom(input);
                    Debug.Log($"<color=cyan>成功載入存檔位 {slot}</color>");
                }
            }
            else
            {
                // 如果檔案不存在，初始化一個空的資料對象
                _currentSaveData = new PlayerSave();
                Debug.Log($"<color=yellow>存檔位 {slot} 為空，已初始化新資料</color>");
            }
        }

        // --- 儲存當前資料 ---
        public void SaveCurrentSlot()
        {
            string path = GetPath(CurrentSlot);
            using (var output = File.Create(path))
            {
                _currentSaveData.WriteTo(output);
            }
            Debug.Log($"<color=green>資料已儲存至存檔位 {CurrentSlot}</color>");
        }

        // --- 刪除存檔 ---
        public void DeleteSlot(int slot)
        {
            string path = GetPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"存檔位 {slot} 已刪除");
            }
        }
    }
}