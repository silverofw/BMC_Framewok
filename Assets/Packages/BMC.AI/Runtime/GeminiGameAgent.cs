using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Serialization;

namespace BMC.AI
{
    /// <summary>
    /// GeminiResponseData: 封裝單次 API 回應的完整數據 (已序列化)
    /// </summary>
    [Serializable]
    public class GeminiResponseData
    {
        public string Text;
        public int TotalTokens;
        public int PromptTokens;
        public int ResponseTokens;
        public int CachedTokens;
        public float ResponseTime;
        public string FinishReason;
        public bool IsSuccess => !string.IsNullOrEmpty(Text);
    }

    /// <summary>
    /// GeminiHistoryTurn: 封裝一輪完整的對話紀錄 (已序列化)
    /// </summary>
    [Serializable]
    public class GeminiHistoryTurn
    {
        public string UserPrompt;
        public GeminiResponseData Response;
        public string Timestamp;

        public GeminiHistoryTurn()
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    /// <summary>
    /// GeminiGameAgent: 靜態底層引擎，負責 API 通訊與全域設定
    /// </summary>
    public static class GeminiGameAgent
    {
        public enum GeminiModelType
        {
            Flash_2_5, Pro_2_5, Flash_2_5_Lite, Flash_2_0, Flash_2_0_Lite,
            Flash_1_5, Flash_1_5_8b, Pro_1_5, Latest_Flash, Latest_Pro,
            Latest_Flash_Lite, Gemma_3_27B, Gemma_3_12B, Gemma_3_4B, Gemma_3_1B,
            Deep_Research, Computer_Use_Preview, Custom
        }

        public static string ApiKey { get; private set; } = "";
        public const string Host = "https://generativelanguage.googleapis.com";
        private const int MaxRetries = 5;

        private static readonly Dictionary<GeminiModelType, string> DefaultModelMap = new Dictionary<GeminiModelType, string>
        {
            { GeminiModelType.Flash_2_5, "gemini-2.5-flash" },
            { GeminiModelType.Pro_2_5, "gemini-2.5-pro" },
            { GeminiModelType.Flash_2_5_Lite, "gemini-2.5-flash-lite" },
            { GeminiModelType.Flash_2_0, "gemini-2.0-flash" },
            { GeminiModelType.Flash_2_0_Lite, "gemini-2.0-flash-lite" },
            { GeminiModelType.Flash_1_5, "gemini-1.5-flash" },
            { GeminiModelType.Flash_1_5_8b, "gemini-1.5-flash-8b" },
            { GeminiModelType.Pro_1_5, "gemini-1.5-pro" },
            { GeminiModelType.Latest_Flash, "gemini-flash-latest" },
            { GeminiModelType.Latest_Pro, "gemini-pro-latest" },
            { GeminiModelType.Latest_Flash_Lite, "gemini-flash-lite-latest" },
            { GeminiModelType.Gemma_3_27B, "gemma-3-27b-it" },
            { GeminiModelType.Gemma_3_12B, "gemma-3-12b-it" },
            { GeminiModelType.Gemma_3_4B, "gemma-3-4b-it" },
            { GeminiModelType.Gemma_3_1B, "gemma-3-1b-it" },
            { GeminiModelType.Deep_Research, "deep-research-pro-preview-12-2025" },
            { GeminiModelType.Computer_Use_Preview, "gemini-2.5-computer-use-preview-10-2025" }
        };

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver { NamingStrategy = null }
        };

        public static void Initialize(string key) => ApiKey = key;

        public static string GetModelId(GeminiModelType type) => DefaultModelMap[type];

        /// <summary> 判斷是否為不支援 system_instruction 的模型 (如 Gemma) </summary>
        public static bool IsGemmaModel(GeminiModelType type) => type.ToString().Contains("Gemma");

        public static async UniTask<(GeminiResponse response, float time, string error)> PostRequestAsync(string modelId, GeminiRequest payload, CancellationToken ct)
        {
            string url = $"{Host}/v1beta/models/{modelId}:generateContent?key={ApiKey}";
            string jsonData = JsonConvert.SerializeObject(payload, JsonSettings);
            int retryCount = 0;

            while (retryCount <= MaxRetries)
            {
                float startTime = Time.realtimeSinceStartup;
                try
                {
                    using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
                    {
                        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");

                        await request.SendWebRequest().WithCancellation(ct);
                        float duration = Time.realtimeSinceStartup - startTime;

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            var res = JsonConvert.DeserializeObject<GeminiResponse>(request.downloadHandler.text, JsonSettings);
                            return (res, duration, null);
                        }

                        if (request.responseCode == 429)
                        {
                            float waitTime = ParseRetryDelay(request.downloadHandler.text);
                            float finalWait = waitTime > 0 ? waitTime : Mathf.Pow(2, retryCount + 1);
                            Debug.LogWarning($"<color=yellow>[Gemini Agent]</color> 頻率限制，等待 {finalWait:F1} 秒...");
                            await UniTask.Delay(TimeSpan.FromSeconds(finalWait), cancellationToken: ct);
                            retryCount++;
                            continue;
                        }
                        return (null, duration, request.downloadHandler.text);
                    }
                }
                catch (Exception ex)
                {
                    if (retryCount >= MaxRetries) return (null, 0, ex.Message);
                    retryCount++;
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Pow(2, retryCount)), cancellationToken: ct);
                }
            }
            return (null, 0, "Max retries exceeded");
        }

        private static float ParseRetryDelay(string errorJson)
        {
            try
            {
                var errorRes = JsonConvert.DeserializeObject<ErrorResponseWrapper>(errorJson, JsonSettings);
                if (errorRes?.error?.details != null)
                {
                    foreach (var detail in errorRes.error.details)
                    {
                        if (detail.type == "type.googleapis.com/google.rpc.RetryInfo" && !string.IsNullOrEmpty(detail.retryDelay))
                        {
                            string delayStr = detail.retryDelay.TrimEnd('s');
                            if (float.TryParse(delayStr, out float seconds)) return seconds;
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
    }

    #region Chat Session Architecture

    /// <summary>
    /// 抽象化的對話 Session 基底，負責封裝共同狀態與解析流程
    /// </summary>
    [Serializable]
    public abstract class BaseChatSession
    {
        public GeminiGameAgent.GeminiModelType ModelType = GeminiGameAgent.GeminiModelType.Flash_2_5;
        public int MaxOutputTokens = 2048;
        [Range(0f, 2f)] public float Temperature = 1.0f;

        [Header("圖片設定")]
        [Tooltip("控制傳送至 API 的最大圖片長寬 (像素)。等比例縮小可大幅節省 Token 消耗 (預設: 1024)")]
        public int MaxImageDimension = 1024;

        [Range(1, 100)]
        [Tooltip("控制傳送至 API 的 JPG 圖片壓縮品質 (1-100)，數值越小檔案越小，有助於節省上傳頻寬")]
        public int ImageQuality = 75;

        [Header("自動摘要設定")]
        public bool AutoSummarizeEnabled = true;
        public int AutoSummarizeThreshold = 5000;

        [TextArea(2, 3)]
        public string SummarizeSystemInstruction = "You are a professional summarization assistant.";

        [TextArea(3, 5)]
        public string SummarizePromptTemplate = "Based on the 'Current Summary' and the 'Recent Conversation History', create a concise 'New Summary' in Traditional Chinese.\nContent to summarize:\n{0}";

        [Header("內部狀態 (唯讀)")]
        [SerializeField] protected string _summary = "目前尚無摘要內容。";
        [SerializeField] protected List<Content> _rawHistory = new List<Content>();
        [SerializeField] protected List<GeminiHistoryTurn> _structuredHistory = new List<GeminiHistoryTurn>();
        [SerializeField] protected GeminiResponseData _lastResponse;

        public IReadOnlyList<GeminiHistoryTurn> History => _structuredHistory;
        public string CurrentSummary => _summary;
        public GeminiResponseData LastResponse => _lastResponse;

        public BaseChatSession(GeminiGameAgent.GeminiModelType model)
        {
            ModelType = model;
        }

        public void ClearHistory() { _rawHistory.Clear(); _structuredHistory.Clear(); }
        public void ResetAll() { ClearHistory(); _summary = "目前尚無摘要內容。"; }

        // 新增 imageBytes 參數以支援圖片辨識
        public abstract UniTask<GeminiResponseData> ChatAsync(string userPrompt, string systemInstruction = null, bool useHistory = true, byte[] imageBytes = null, CancellationToken ct = default);
        public abstract UniTask SummarizeAsync(CancellationToken ct = default);

        protected async UniTask<GeminiResponseData> SendAndProcessAsync(GeminiRequest payload, string userPrompt, bool useHistory, CancellationToken ct)
        {
            string modelId = GeminiGameAgent.GetModelId(ModelType);
            var (res, time, err) = await GeminiGameAgent.PostRequestAsync(modelId, payload, ct);

            if (res != null)
            {
                var data = new GeminiResponseData
                {
                    Text = res.candidates[0].content?.parts?[0].text,
                    ResponseTime = time,
                    TotalTokens = res.usageMetadata?.totalTokenCount ?? 0,
                    PromptTokens = res.usageMetadata?.promptTokenCount ?? 0,
                    ResponseTokens = res.usageMetadata?.candidatesTokenCount ?? 0,
                    CachedTokens = res.usageMetadata?.cachedContentTokenCount ?? 0,
                    FinishReason = res.candidates[0].finishReason
                };

                _lastResponse = data;

                if (useHistory && data.IsSuccess)
                {
                    _rawHistory.Add(new Content { role = "model", parts = new List<Part> { new Part { text = data.Text } } });
                    _structuredHistory.Add(new GeminiHistoryTurn { UserPrompt = userPrompt, Response = data });

                    if (AutoSummarizeEnabled && data.TotalTokens >= AutoSummarizeThreshold)
                    {
                        await SummarizeAsync(ct);
                    }
                }
                return data;
            }
            throw new Exception(err);
        }

        protected string BuildSummarizePrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Current Summary: {_summary}");
            sb.AppendLine("Recent Conversation History to Compress:");
            foreach (var h in _rawHistory) sb.AppendLine($"{h.role}: {h.parts[0].text}");

            return string.Format(SummarizePromptTemplate, sb.ToString());
        }

        protected async UniTask SendSummarizeRequestAsync(GeminiRequest payload, CancellationToken ct)
        {
            var (res, _, _) = await GeminiGameAgent.PostRequestAsync(GeminiGameAgent.GetModelId(ModelType), payload, ct);
            if (res?.candidates != null && res.candidates.Count > 0)
            {
                _summary = res.candidates[0].content?.parts?[0].text;
                _rawHistory.Clear(); // 摘要後清空原始對話以釋放 Token
            }
        }
    }

    /// <summary>
    /// 標準的 Gemini 對話 Session，原生支援 system_instruction
    /// </summary>
    [Serializable]
    public class GeminiChatSession : BaseChatSession
    {
        public GeminiChatSession(GeminiGameAgent.GeminiModelType model = GeminiGameAgent.GeminiModelType.Flash_2_5) : base(model) { }

        public override async UniTask<GeminiResponseData> ChatAsync(string userPrompt, string systemInstruction = null, bool useHistory = true, byte[] imageBytes = null, CancellationToken ct = default)
        {
            string combinedSummary = $"[Context Summary (Canvas)]\n{_summary}";
            string finalInstructionText = string.IsNullOrEmpty(systemInstruction) ? combinedSummary : $"{systemInstruction}\n\n{combinedSummary}";

            var payloadSystemInstruction = new SystemInstruction { parts = new List<Part> { new Part { text = finalInstructionText } } };

            // 處理 User Parts（包含可能的圖片與文字）
            var userParts = new List<Part>();
            if (imageBytes != null && imageBytes.Length > 0)
            {
                userParts.Add(new Part
                {
                    inlineData = new InlineData
                    {
                        mimeType = "image/jpeg",
                        data = Convert.ToBase64String(imageBytes)
                    }
                });
            }
            userParts.Add(new Part { text = userPrompt });

            var contents = new List<Content>();
            if (useHistory)
            {
                _rawHistory.Add(new Content { role = "user", parts = userParts });
                contents = new List<Content>(_rawHistory);
            }
            else
            {
                contents.Add(new Content { role = "user", parts = userParts });
            }

            var payload = new GeminiRequest
            {
                contents = contents,
                systemInstruction = payloadSystemInstruction,
                generationConfig = new GenerationConfig { maxOutputTokens = MaxOutputTokens, temperature = Temperature }
            };

            return await SendAndProcessAsync(payload, userPrompt, useHistory, ct);
        }

        public override async UniTask SummarizeAsync(CancellationToken ct = default)
        {
            if (_rawHistory.Count == 0) return;

            string prompt = BuildSummarizePrompt();

            var payload = new GeminiRequest
            {
                contents = new List<Content> { new Content { role = "user", parts = new List<Part> { new Part { text = prompt } } } },
                systemInstruction = new SystemInstruction { parts = new List<Part> { new Part { text = SummarizeSystemInstruction } } },
                generationConfig = new GenerationConfig { maxOutputTokens = 1024, temperature = 0.5f }
            };

            await SendSummarizeRequestAsync(payload, ct);
        }
    }

    /// <summary>
    /// Gemma 專用的對話 Session，將指令以文字形式注入 User Prompt
    /// </summary>
    [Serializable]
    public class GemmaChatSession : BaseChatSession
    {
        public GemmaChatSession(GeminiGameAgent.GeminiModelType model = GeminiGameAgent.GeminiModelType.Gemma_3_27B) : base(model) { }

        public override async UniTask<GeminiResponseData> ChatAsync(string userPrompt, string systemInstruction = null, bool useHistory = true, byte[] imageBytes = null, CancellationToken ct = default)
        {
            string combinedSummary = $"[Context Summary (Canvas)]\n{_summary}";
            string finalInstructionText = string.IsNullOrEmpty(systemInstruction) ? combinedSummary : $"{systemInstruction}\n\n{combinedSummary}";

            string gemmaInjectedPrompt = $"[SYSTEM INSTRUCTION]\n{finalInstructionText}\n\n[USER INPUT]\n{userPrompt}";

            // 處理 User Parts（包含可能的圖片與文字）
            var userParts = new List<Part>();
            if (imageBytes != null && imageBytes.Length > 0)
            {
                userParts.Add(new Part
                {
                    inlineData = new InlineData
                    {
                        mimeType = "image/jpeg",
                        data = Convert.ToBase64String(imageBytes)
                    }
                });
            }

            var contents = new List<Content>();
            if (useHistory)
            {
                if (_rawHistory.Count == 0)
                {
                    userParts.Add(new Part { text = gemmaInjectedPrompt });
                }
                else
                {
                    userParts.Add(new Part { text = userPrompt });
                }

                _rawHistory.Add(new Content { role = "user", parts = userParts });
                contents = new List<Content>(_rawHistory);
            }
            else
            {
                userParts.Add(new Part { text = gemmaInjectedPrompt });
                contents.Add(new Content { role = "user", parts = userParts });
            }

            var payload = new GeminiRequest
            {
                contents = contents,
                systemInstruction = null, // Gemma 不支援此欄位
                generationConfig = new GenerationConfig { maxOutputTokens = MaxOutputTokens, temperature = Temperature }
            };

            return await SendAndProcessAsync(payload, userPrompt, useHistory, ct);
        }

        public override async UniTask SummarizeAsync(CancellationToken ct = default)
        {
            if (_rawHistory.Count == 0) return;

            string prompt = BuildSummarizePrompt();

            string gemmaSummarizePrompt = $"[SYSTEM INSTRUCTION]\n{SummarizeSystemInstruction}\n\n[USER INPUT]\n{prompt}";

            var payload = new GeminiRequest
            {
                contents = new List<Content> { new Content { role = "user", parts = new List<Part> { new Part { text = gemmaSummarizePrompt } } } },
                systemInstruction = null,
                generationConfig = new GenerationConfig { maxOutputTokens = 1024, temperature = 0.5f }
            };

            await SendSummarizeRequestAsync(payload, ct);
        }
    }

    #endregion

    #region Data Structures
    [Serializable]
    public class ErrorResponseWrapper { public ErrorDetailData error; }
    [Serializable]
    public class ErrorDetailData { public int code; public List<ErrorDetailItem> details; }
    [Serializable]
    public class ErrorDetailItem { [JsonProperty("@type")] public string type; public string retryDelay; }

    [Serializable]
    public class GeminiRequest
    {
        public List<Content> contents;
        [JsonProperty("system_instruction")] public SystemInstruction systemInstruction;
        public GenerationConfig generationConfig;
    }

    [Serializable]
    public class GenerationConfig
    {
        public int maxOutputTokens;
        public float temperature;
    }

    [Serializable]
    public class SystemInstruction { public List<Part> parts; }

    [Serializable]
    public class Content
    {
        public List<Part> parts;
        public string role;
    }

    [Serializable]
    public class Part
    {
        public string text;
        public InlineData inlineData; // 新增支援圖片資料

        // 阻擋 Unity Inspector 自動將 null 轉為空字串造成的 400 錯誤
        public bool ShouldSerializetext()
        {
            return !string.IsNullOrEmpty(text);
        }

        // 阻擋 Unity Inspector 自動 new 空物件造成的 400 錯誤
        public bool ShouldSerializeinlineData()
        {
            return inlineData != null && !string.IsNullOrEmpty(inlineData.data);
        }
    }

    // 定義 InlineData 資料結構
    [Serializable]
    public class InlineData
    {
        public string mimeType;
        public string data;
    }

    [Serializable]
    public class GeminiResponse { public List<Candidate> candidates; public UsageMetadata usageMetadata; }

    [Serializable]
    public class UsageMetadata
    {
        public int totalTokenCount;
        public int promptTokenCount;
        public int candidatesTokenCount;
        public int cachedContentTokenCount;
    }

    [Serializable]
    public class Candidate
    {
        public Content content;
        public string finishReason;
    }
    #endregion
}