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
                if (errorRes?.Error?.Details != null)
                {
                    foreach (var detail in errorRes.Error.Details)
                    {
                        if (detail.Type == "type.googleapis.com/google.rpc.RetryInfo" && !string.IsNullOrEmpty(detail.RetryDelay))
                        {
                            string delayStr = detail.RetryDelay.TrimEnd('s');
                            if (float.TryParse(delayStr, out float seconds)) return seconds;
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
    }

    /// <summary>
    /// GeminiChatSession: 可實例化的對話 Session
    /// </summary>
    [Serializable]
    public class GeminiChatSession
    {
        public GeminiGameAgent.GeminiModelType ModelType = GeminiGameAgent.GeminiModelType.Flash_2_5;
        public int MaxOutputTokens = 2048;
        [Range(0f, 2f)] public float Temperature = 1.0f;

        [Header("自動摘要設定")]
        public bool AutoSummarizeEnabled = true;
        public int AutoSummarizeThreshold = 5000;

        [Header("內部狀態 (唯讀)")]
        [SerializeField] private string _summary = "目前尚無摘要內容。";
        [SerializeField] private List<Content> _rawHistory = new List<Content>();
        [SerializeField] private List<GeminiHistoryTurn> _structuredHistory = new List<GeminiHistoryTurn>();
        [SerializeField] private GeminiResponseData _lastResponse;

        public IReadOnlyList<GeminiHistoryTurn> History => _structuredHistory;
        public string CurrentSummary => _summary;
        public GeminiResponseData LastResponse => _lastResponse;

        public GeminiChatSession(GeminiGameAgent.GeminiModelType model = GeminiGameAgent.GeminiModelType.Flash_2_5)
        {
            ModelType = model;
        }

        public void ClearHistory() { _rawHistory.Clear(); _structuredHistory.Clear(); }
        public void ResetAll() { ClearHistory(); _summary = "目前尚無摘要內容。"; }

        public async UniTask<GeminiResponseData> ChatAsync(string userPrompt, string systemInstruction = null, bool useHistory = true, CancellationToken ct = default)
        {
            string modelId = GeminiGameAgent.GetModelId(ModelType);
            bool isGemma = GeminiGameAgent.IsGemmaModel(ModelType);

            // 處理 Gemma 的指令注入 (Gemma 不支援獨立 system_instruction)
            string combinedSummary = $"[Context Summary (Canvas)]\n{_summary}";
            string finalInstructionText = string.IsNullOrEmpty(systemInstruction) ? combinedSummary : $"{systemInstruction}\n\n{combinedSummary}";

            List<Content> contents = new List<Content>();
            SystemInstruction payloadSystemInstruction = null;

            if (isGemma)
            {
                // Gemma 模式：將指令與 User Prompt 合併為第一條訊息
                string gemmaInjectedPrompt = $"[SYSTEM INSTRUCTION]\n{finalInstructionText}\n\n[USER INPUT]\n{userPrompt}";

                if (useHistory)
                {
                    if (_rawHistory.Count == 0)
                        _rawHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = gemmaInjectedPrompt } } });
                    else
                        _rawHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = userPrompt } } });

                    contents = new List<Content>(_rawHistory);
                }
                else
                {
                    contents.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = gemmaInjectedPrompt } } });
                }
            }
            else
            {
                // Gemini 模式：使用標準 system_instruction 欄位
                payloadSystemInstruction = new SystemInstruction { Parts = new List<Part> { new Part { Text = finalInstructionText } } };

                if (useHistory)
                {
                    _rawHistory.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = userPrompt } } });
                    contents = new List<Content>(_rawHistory);
                }
                else
                {
                    contents.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = userPrompt } } });
                }
            }

            var payload = new GeminiRequest
            {
                Contents = contents,
                SystemInstruction = payloadSystemInstruction,
                GenerationConfig = new GenerationConfig { MaxOutputTokens = MaxOutputTokens, Temperature = Temperature }
            };

            var (res, time, err) = await GeminiGameAgent.PostRequestAsync(modelId, payload, ct);

            if (res != null)
            {
                var data = new GeminiResponseData
                {
                    Text = res.Candidates[0].Content?.Parts?[0].Text,
                    ResponseTime = time,
                    TotalTokens = res.UsageMetadata?.TotalTokenCount ?? 0,
                    PromptTokens = res.UsageMetadata?.PromptTokenCount ?? 0,
                    ResponseTokens = res.UsageMetadata?.CandidatesTokenCount ?? 0,
                    CachedTokens = res.UsageMetadata?.CachedContentTokenCount ?? 0,
                    FinishReason = res.Candidates[0].FinishReason
                };

                _lastResponse = data;

                if (useHistory && data.IsSuccess)
                {
                    _rawHistory.Add(new Content { Role = "model", Parts = new List<Part> { new Part { Text = data.Text } } });
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

        public async UniTask SummarizeAsync(CancellationToken ct = default)
        {
            if (_rawHistory.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Current Summary: {_summary}");
            sb.AppendLine("Recent Conversation History to Compress:");
            foreach (var h in _rawHistory) sb.AppendLine($"{h.Role}: {h.Parts[0].Text}");

            string prompt = "Based on the 'Current Summary' and the 'Recent Conversation History', create a concise 'New Summary' in Traditional Chinese.\n" +
                           $"Content to summarize:\n{sb.ToString()}";

            // 摘要請求通常是單次請求，Gemma 一樣需要將指令合併
            bool isGemma = GeminiGameAgent.IsGemmaModel(ModelType);
            GeminiRequest payload;

            if (isGemma)
            {
                payload = new GeminiRequest
                {
                    Contents = new List<Content> { new Content { Role = "user", Parts = new List<Part> { new Part { Text = $"[INSTRUCTION: Summarize the following]\n{prompt}" } } } },
                    SystemInstruction = null, // Gemma 不支援
                    GenerationConfig = new GenerationConfig { MaxOutputTokens = 1024, Temperature = 0.5f }
                };
            }
            else
            {
                payload = new GeminiRequest
                {
                    Contents = new List<Content> { new Content { Role = "user", Parts = new List<Part> { new Part { Text = prompt } } } },
                    SystemInstruction = new SystemInstruction { Parts = new List<Part> { new Part { Text = "You are a professional summarization assistant." } } },
                    GenerationConfig = new GenerationConfig { MaxOutputTokens = 1024, Temperature = 0.5f }
                };
            }

            var (res, _, _) = await GeminiGameAgent.PostRequestAsync(GeminiGameAgent.GetModelId(ModelType), payload, ct);
            if (res?.Candidates != null && res.Candidates.Count > 0)
            {
                _summary = res.Candidates[0].Content?.Parts?[0].Text;
                _rawHistory.Clear();
            }
        }
    }

    #region Data Structures
    [Serializable]
    public class ErrorResponseWrapper { [JsonProperty("error")] public ErrorDetailData Error; }
    [Serializable]
    public class ErrorDetailData { [JsonProperty("code")] public int Code; [JsonProperty("details")] public List<ErrorDetailItem> Details; }
    [Serializable]
    public class ErrorDetailItem { [JsonProperty("@type")] public string Type; [JsonProperty("retryDelay")] public string RetryDelay; }

    [Serializable]
    public class GeminiRequest
    {
        [JsonProperty("contents")] public List<Content> Contents;
        [JsonProperty("system_instruction")] public SystemInstruction SystemInstruction;
        [JsonProperty("generationConfig")] public GenerationConfig GenerationConfig;
    }

    [Serializable]
    public class GenerationConfig
    {
        [JsonProperty("maxOutputTokens")] public int MaxOutputTokens;
        [JsonProperty("temperature")] public float Temperature;
    }

    [Serializable]
    public class SystemInstruction { [JsonProperty("parts")] public List<Part> Parts; }

    [Serializable]
    public class Content
    {
        [JsonProperty("parts")] public List<Part> Parts;
        [JsonProperty("role")] public string Role;
    }

    [Serializable]
    public class Part { [JsonProperty("text")] public string Text; }

    [Serializable]
    public class GeminiResponse { [JsonProperty("candidates")] public List<Candidate> Candidates; [JsonProperty("usageMetadata")] public UsageMetadata UsageMetadata; }

    [Serializable]
    public class UsageMetadata
    {
        [JsonProperty("totalTokenCount")] public int TotalTokenCount;
        [JsonProperty("promptTokenCount")] public int PromptTokenCount;
        [JsonProperty("candidatesTokenCount")] public int CandidatesTokenCount;
        [JsonProperty("cachedContentTokenCount")] public int CachedContentTokenCount;
    }

    [Serializable]
    public class Candidate
    {
        [JsonProperty("content")] public Content Content;
        [JsonProperty("finishReason")] public string FinishReason;
    }
    #endregion
}