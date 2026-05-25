using System;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.Core
{
    // 對應單筆 Server 的資料結構 (可後續任意擴充欄位)
    [Serializable]
    public class ServerData
    {
        public string serverType;
        public string url;
        public int port;
        public string cdnUrl;

        // 未來擴充範例：
        // public string version; 
        // public bool isMaintenance;
    }

    // 對應整個 JSON 的資料結構
    [Serializable]
    public class ServerConfig
    {
        public string defaultServerType;
        public List<ServerData> servers;
    }

    public class ServerMgr : Singleton<ServerMgr>
    {
        // 儲存解析後的完整設定
        public ServerConfig Config { get; private set; }

        // 儲存當前正在使用的 Server 資訊
        public ServerData CurrentServer { get; private set; }

        /// <summary>
        /// 初始化並讀取 ServerConfig.json
        /// </summary>
        public void Init(string resourcePath = "ServerConfig")
        {
            // 1. 從 Resources 讀取 JSON
            TextAsset jsonText = Resources.Load<TextAsset>(resourcePath);
            if (jsonText == null)
            {
                Debug.LogError($"[ServerMgr] 找不到設定檔: Resources/{resourcePath}.json");
                return;
            }

            // 2. 解析 JSON
            Config = JsonUtility.FromJson<ServerConfig>(jsonText.text);

            // 3. 根據 defaultServerType 找出當前的 Server
            CurrentServer = Config.servers.Find(s => s.serverType == Config.defaultServerType);

            if (CurrentServer == null)
            {
                Debug.LogError($"[ServerMgr] 在設定檔中找不到預設的伺服器類型: {Config.defaultServerType}");
            }
            else
            {
                Debug.Log($"[ServerMgr] 初始化成功！當前伺服器: {CurrentServer.serverType} | 網址: {CurrentServer.url}:{CurrentServer.port}");
            }
        }

        /// <summary>
        /// 取得完整 API 網址的輔助方法
        /// </summary>
        public string GetApiUrl(string endpoint)
        {
            if (CurrentServer == null) return string.Empty;

            // 處理 Port (如果是 80 或 443 通常不用特別加在網址後，但為了彈性這裡照加或做判斷)
            string portStr = (CurrentServer.port == 80 || CurrentServer.port == 443)
                ? ""
                : $":{CurrentServer.port}";

            return $"{CurrentServer.url}{portStr}/{endpoint}";
        }
        public void SwitchServer(string targetServerType)
        {
            if (Config == null || Config.servers == null) return;

            var targetServer = Config.servers.Find(s => s.serverType == targetServerType);
            if (targetServer != null)
            {
                CurrentServer = targetServer;
                Debug.Log($"[ServerMgr] 已切換伺服器至: {CurrentServer.serverType} ({CurrentServer.url})");
            }
            else
            {
                Debug.LogError($"[ServerMgr] 切換失敗，找不到名為 {targetServerType} 的伺服器設定！");
            }
        }
    }
}