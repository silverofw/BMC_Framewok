using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BMC.AI;
using BMC.UI;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.IO;

public class AgentPanel : UIPanel
{
    [Header("API 安全設定")]
    [Tooltip("若此處為空，將從 StreamingAssets/apiKey.txt 讀取")]
    [SerializeField] private string apiKey = "";

    [Header("UI 引用 - 聊天內容")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Transform chatContainer;
    [SerializeField] private AgentItem infoItem;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private UIButton sendBtn;
    [SerializeField] private TMP_InputField promptInput;

    [Header("UI 引用 - 情境切換")]
    [SerializeField] private AgentContextItem contextItemPrefab;
    [SerializeField] private Transform contextContainer;

    [Header("生成參數")]
    public GeminiGameAgent.GeminiModelType modelType = GeminiGameAgent.GeminiModelType.Gemma_3_12B;
    public bool useHistory = true;
    [Range(0, 2)] public float temperature = 0.7f;
    public int maxOutputTokens = 2048;
    public int summarizeThreshold = 5000;

    [Header("情境設定 (JSON)")]
    public TextAsset contextConfigJson;
    public int activeContextIndex = 0;
    public PromptExtensionConfig[] contextPresets;

    [Header("Debug 輸出")]
    [TextArea(5, 15)] public string lastResult;

    private BaseChatSession chatSession;
    private CancellationTokenSource cts;

    private void Awake()
    {
        string finalKey = apiKey;
        string localPath = Path.Combine(Application.streamingAssetsPath, "apiKey.txt");
        if (string.IsNullOrEmpty(finalKey) && File.Exists(localPath))
        {
            finalKey = File.ReadAllText(localPath).Trim();
        }
        GeminiGameAgent.Initialize(finalKey);
        EnsureSessionTypeMatch();
    }

    private void Start()
    {
        cts = new CancellationTokenSource();
        sendBtn.OnClick = () => SendMessageAsync().Forget();
        if (infoItem != null) infoItem.gameObject.SetActive(false);
        if (contextItemPrefab != null) contextItemPrefab.gameObject.SetActive(false);
        RefreshContextButtons();
        SyncContextToGlobals();
        EnsureSessionTypeMatch();
    }

    public void RefreshContextButtons()
    {
        if (contextContainer == null || contextItemPrefab == null) return;
        foreach (Transform child in contextContainer)
        {
            if (child.gameObject != contextItemPrefab.gameObject)
                Destroy(child.gameObject);
        }
        if (contextPresets == null) return;
        for (int i = 0; i < contextPresets.Length; i++)
        {
            int index = i;
            var item = Instantiate(contextItemPrefab, contextContainer);
            item.gameObject.SetActive(true);
            item.Init(contextPresets[i].contextName, () => { SwitchContext(index); });
        }
    }

    public void SwitchContext(int index)
    {
        if (index < 0 || index >= contextPresets.Length) return;
        activeContextIndex = index;
        SyncContextToGlobals();
        EnsureSessionTypeMatch();
        Debug.Log($"<color=cyan>[AgentPanel]</color> 已切換至情境: {contextPresets[index].contextName}");
    }

    public void SyncContextToGlobals()
    {
        if (contextPresets != null && activeContextIndex >= 0 && activeContextIndex < contextPresets.Length)
        {
            PromptExtensionConfig config = contextPresets[activeContextIndex];
            modelType = config.modelType;
            useHistory = config.useHistory;
            temperature = config.temperature;
        }
    }

    private void EnsureSessionTypeMatch()
    {
        bool isGemma = GeminiGameAgent.IsGemmaModel(modelType);
        if (chatSession == null ||
           (isGemma && !(chatSession is GemmaChatSession)) ||
           (!isGemma && !(chatSession is GeminiChatSession)))
        {
            if (isGemma) chatSession = new GemmaChatSession(modelType);
            else chatSession = new GeminiChatSession(modelType);
        }
    }

    private async UniTask SendMessageAsync()
    {
        string actualPrompt = inputField != null ? inputField.text : "";
        if (string.IsNullOrEmpty(actualPrompt)) return;
        inputField.text = "";
        CreateChatItem("You", actualPrompt);

        chatSession.ModelType = modelType;
        chatSession.Temperature = temperature;
        chatSession.MaxOutputTokens = maxOutputTokens;
        chatSession.AutoSummarizeThreshold = summarizeThreshold;

        PromptExtensionConfig currentConfig = (contextPresets != null && contextPresets.Length > activeContextIndex)
            ? contextPresets[activeContextIndex]
            : new PromptExtensionConfig();

        string baseInstruction = promptInput != null ? promptInput.text : "";
        string finalInstruction = currentConfig.BuildFinalSystemInstruction(baseInstruction);

        Debug.Log($"<color=#FFA500>[Agent] 準備發送請求...</color>\n" +
                  $"<b>目標模型:</b> {modelType}\n" +
                  $"<b>系統提示詞:</b>\n{finalInstruction}");

        lastResult = "Thinking...";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await chatSession.ChatAsync(actualPrompt, finalInstruction, useHistory, null, cts.Token);
            stopwatch.Stop();

            if (response.IsSuccess)
            {
                Debug.Log($"<color=#00FF00>[Agent] 收到回覆！</color>\n" +
                          $"<b>等待時間:</b> {response.ResponseTime:F2}s\n" +
                          $"<b>Token:</b> {response.TotalTokens}");

                lastResult = response.Text;
                CreateChatItem(currentConfig.contextName, response.Text);
            }
            else
            {
                // 現在 GeminiResponseData 有 Error 欄位了，這裡不會再報錯
                Debug.LogError($"[Agent] 請求失敗: {response.Error}");
                lastResult = "Error: " + response.Error;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Agent] 發生例外: {ex.Message}");
            lastResult = "Exception: " + ex.Message;
        }
    }

    private void CreateChatItem(string sender, string content)
    {
        if (infoItem == null) return;
        var obj = Instantiate(infoItem, chatContainer);
        obj.gameObject.SetActive(true);
        obj.Init(sender, content, null);
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        cts?.Cancel();
        cts?.Dispose();
    }
}