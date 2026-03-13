using UnityEngine;
using Cysharp.Threading.Tasks;
using BMC.AI;
using BMC.UI;
using System;

public class AgentPanel : UIPanel
{
    [Header("UI 元件")]
    [SerializeField] private UIButton sendBtn;
    [SerializeField] private UIButton summarizeBtn;
    [SerializeField] private UIButton resetBtn;

    [Header("Gemini 全域設定")]
    [SerializeField] private string apiKey = "YOUR_API_KEY";

    [Header("Session 實體對象 (Inspector 檢視區)")]
    // 透過 SerializeField 讓 GeminiChatSession 顯示在 Inspector
    [SerializeField] private GeminiChatSession chatSession;

    [Header("生成參數同步")]
    [SerializeField] private GeminiGameAgent.GeminiModelType modelType = GeminiGameAgent.GeminiModelType.Latest_Flash_Lite;
    [SerializeField] private bool useHistory = true;
    [SerializeField] private int maxOutputTokens = 2048;
    [Range(0f, 2f)][SerializeField] private float temperature = 1.0f;
    [SerializeField] private int summarizeThreshold = 5000;

    [Header("測試內容")]
    [TextArea(3, 5)] public string systemInstruction = "你是一個專業的開發助理。";
    [TextArea(3, 5)] public string testPrompt = "請分析目前的設計。";

    [Header("Debug 輸出")]
    [TextArea(5, 15)] public string lastResult;

    private void Awake()
    {
        GeminiGameAgent.Initialize(apiKey);

        // 如果 Inspector 沒拉物件，就建立一個
        if (chatSession == null)
            chatSession = new GeminiChatSession(modelType);
    }

    private void Start()
    {
        if (sendBtn != null)
            sendBtn.OnClick = async () => { await ExecuteTest(); };

        if (summarizeBtn != null)
            summarizeBtn.OnClick = async () => { await chatSession.SummarizeAsync(); };

        if (resetBtn != null)
            resetBtn.OnClick = () => { chatSession.ResetAll(); };
    }

    public async UniTask ExecuteTest()
    {
        Debug.Log("<color=cyan>[Agent]</color> 準備發送請求...");

        // 將面板參數同步至 Session 物件
        chatSession.ModelType = modelType;
        chatSession.MaxOutputTokens = maxOutputTokens;
        chatSession.Temperature = temperature;
        chatSession.AutoSummarizeThreshold = summarizeThreshold;

        lastResult = "請求中...";

        try
        {
            GeminiResponseData response = await chatSession.ChatAsync(testPrompt, systemInstruction, useHistory, this.GetCancellationTokenOnDestroy());
            lastResult = response.Text;

            Debug.Log($"<color=green>[回應]</color>\n{response.Text}");
            Debug.Log($"<color=yellow>[統計]</color> 耗時: {response.ResponseTime:F2}s | 總 Token: {response.TotalTokens}");
        }
        catch (Exception e)
        {
            lastResult = $"錯誤: {e.Message}";
            Debug.LogError($"請求失敗: {e.Message}");
        }
    }
}