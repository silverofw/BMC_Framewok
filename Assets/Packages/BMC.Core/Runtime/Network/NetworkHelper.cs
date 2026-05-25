using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace BMC.Core
{
    public static class NetworkHelper
    {
        // 預設請求超時時間 (秒)
        private const int DefaultTimeout = 15;

        /// <summary>
        /// 發送 HTTP GET 請求
        /// </summary>
        /// <param name="url">請求網址</param>
        /// <param name="token">Bearer Token (可選)</param>
        /// <param name="timeout">超時時間 (秒)</param>
        public static async UniTask<string> GetAsync(string url, string token = null, int timeout = DefaultTimeout)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                // 設定超時時間
                req.timeout = timeout;

                // 若有設定 Token，則加入 Authorization 標頭
                if (!string.IsNullOrEmpty(token))
                {
                    req.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                // 等待請求完成
                await req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[NetworkHelper] GET Error: {req.error} | URL: {url}");
                    throw new System.Exception(req.error);
                }

                return req.downloadHandler.text;
            }
        }

        /// <summary>
        /// 發送 HTTP POST 請求 (傳遞 JSON)
        /// </summary>
        /// <param name="url">請求網址</param>
        /// <param name="jsonData">JSON 格式的字串資料</param>
        /// <param name="token">Bearer Token (可選)</param>
        /// <param name="timeout">超時時間 (秒)</param>
        public static async UniTask<string> PostAsync(string url, string jsonData, string token = null, int timeout = DefaultTimeout)
        {
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                // 將 json 字串轉為 byte array
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();

                // 設定 Header
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(token))
                {
                    req.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                // 設定超時時間
                req.timeout = timeout;

                // 等待請求完成
                await req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[NetworkHelper] POST Error: {req.error} | URL: {url}");
                    throw new System.Exception(req.error);
                }

                return req.downloadHandler.text;
            }
        }
    }
}