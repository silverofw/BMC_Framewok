using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace BMC.AI
{
    /// <summary>
    /// 專門用來對接 LM Studio (OpenAI 相容 API) 的會話機制
    /// 繼承自 BaseChatSession，確保大腦可以無縫切換
    /// </summary>
    [Serializable]
    public class LMStudioSession : BaseChatSession
    {
        public string EndpointUrl = "http://127.0.0.1:1234/v1/chat/completions";
        public string ModelName = "local-model"; // LM Studio 通常會忽略這個，但格式上需要

        // 呼叫基底建構子，傳入 Custom 代表非官方預設模型
        public LMStudioSession() : base(GeminiGameAgent.GeminiModelType.Custom) { }

        public override async UniTask<GeminiResponseData> ChatAsync(string userPrompt, string systemInstruction = null, bool useHistory = false, byte[] imageBytes = null, CancellationToken ct = default)
        {
            // 建立符合 OpenAI API 規範的 Messages 結構
            var messages = new List<object>();

            // 1. 注入系統指令 (System Prompt)
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                messages.Add(new { role = "system", content = systemInstruction });
            }

            // 2. 注入使用者指令與圖片 (User Prompt + Vision)
            if (imageBytes != null && imageBytes.Length > 0)
            {
                string base64Image = Convert.ToBase64String(imageBytes);
                messages.Add(new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userPrompt },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{base64Image}" } }
                    }
                });
            }
            else
            {
                // 純文字對話
                messages.Add(new { role = "user", content = userPrompt });
            }

            // 3. 封裝 Request Payload
            var payload = new
            {
                model = ModelName,
                messages = messages,
                temperature = Temperature,
                max_tokens = MaxOutputTokens
            };

            string jsonData = JsonConvert.SerializeObject(payload, GeminiGameAgent.JsonSettings);
            float startTime = Time.realtimeSinceStartup;

            try
            {
                using (UnityWebRequest request = new UnityWebRequest(EndpointUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    // 送出請求並等待
                    await request.SendWebRequest().WithCancellation(ct);

                    float duration = Time.realtimeSinceStartup - startTime;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        // 解析 LM Studio (OpenAI) 的回傳格式
                        var res = JsonConvert.DeserializeObject<OAIResponse>(request.downloadHandler.text, GeminiGameAgent.JsonSettings);
                        string outText = res?.choices?[0]?.message?.content ?? string.Empty;

                        var responseData = new GeminiResponseData
                        {
                            Text = outText,
                            ResponseTime = duration,
                            FinishReason = res?.choices?[0]?.finish_reason
                        };
                        _lastResponse = responseData;
                        return responseData;
                    }

                    // 錯誤處理
                    string errorMsg = request.downloadHandler?.text ?? request.error;
                    var errorData = new GeminiResponseData { Error = errorMsg, ResponseTime = duration };
                    _lastResponse = errorData;
                    return errorData;
                }
            }
            catch (Exception ex)
            {
                return new GeminiResponseData { Error = ex.Message, ResponseTime = Time.realtimeSinceStartup - startTime };
            }
        }

        public override UniTask SummarizeAsync(CancellationToken ct = default)
        {
            // 由於最小迴圈版本 useHistory = false，暫時不需要實作總結功能
            return UniTask.CompletedTask;
        }

        #region OpenAI 回傳格式的資料結構
        [Serializable]
        public class OAIResponse { public List<OAIChoice> choices; }

        [Serializable]
        public class OAIChoice { public OAIMessage message; public string finish_reason; }

        [Serializable]
        public class OAIMessage { public string content; }
        #endregion
    }
}