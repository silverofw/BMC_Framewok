using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BMC.Core
{
    /// <summary>
    /// 通用 API 基底。
    /// 所有的具體 API 服務 (如 LoginApi, PlayerDataApi) 都應該繼承此類別。
    /// 負責統一處理：網址組合、Token 取用、資料序列化與反序列化。
    /// </summary>
    public abstract class ApiBase
    {
        /// <summary>
        /// 子類必須覆寫此屬性，定義該 API 的主要路徑 (例如: "api/v1/users")
        /// </summary>
        protected abstract string Endpoint { get; }

        /// <summary>
        /// 取得驗證 Token 的方法。
        /// 子類可覆寫，或在此處統一與你的「使用者管理器 (UserMgr)」對接。
        /// </summary>
        protected virtual string GetAuthToken()
        {
            // TODO: 在這裡回傳你儲存在遊戲中的 Token。
            // 範例: return UserDataMgr.Instance.AccessToken;
            return string.Empty;
        }

        /// <summary>
        /// 輔助方法：組合完整的 API 網址
        /// </summary>
        protected string GetFullUrl(string subPath = "")
        {
            string finalPath = string.IsNullOrEmpty(subPath) ? Endpoint : $"{Endpoint}/{subPath}";
            return ServerMgr.Instance.GetApiUrl(finalPath);
        }

        /// <summary>
        /// 發送 GET 請求，並將回傳的 JSON 自動解析為指定型別 T
        /// </summary>
        protected async UniTask<T> SendGetRequest<T>(string subPath = "", int timeout = 15)
        {
            string url = GetFullUrl(subPath);
            string token = GetAuthToken();

            string responseJson = await NetworkHelper.GetAsync(url, token, timeout);

            // 使用 JsonUtility 反序列化 (傳入型別 T 需加上 [Serializable] 標籤)
            return JsonUtility.FromJson<T>(responseJson);
        }

        /// <summary>
        /// 發送 POST 請求，將 Request 資料轉 JSON 送出，並把 Response JSON 解析為型別 TResponse
        /// </summary>
        protected async UniTask<TResponse> SendPostRequest<TRequest, TResponse>(TRequest requestData, string subPath = "", int timeout = 15)
        {
            string url = GetFullUrl(subPath);
            string token = GetAuthToken();

            // 序列化 Request 資料
            string requestJson = JsonUtility.ToJson(requestData);

            // 發送網路請求
            string responseJson = await NetworkHelper.PostAsync(url, requestJson, token, timeout);

            // 反序列化 Response 資料
            return JsonUtility.FromJson<TResponse>(responseJson);
        }
    }
}