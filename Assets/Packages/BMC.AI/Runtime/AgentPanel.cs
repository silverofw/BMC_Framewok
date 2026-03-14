using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // 使用 TMPro 命名空間
using BMC.AI;
using BMC.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

public class AgentPanel : UIPanel
{
    [Header("API 設定")]
    [SerializeField] private string apiKey = "";

    [Header("UI 引用 - 聊天內容")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Transform chatContainer;
    [SerializeField] private AgentItem infoItem; // 聊天訊息 Prefab
    [SerializeField] private TMP_InputField inputField; // 修正：改為使用 TMP_InputField
    [SerializeField] private UIButton sendBtn;
    [SerializeField] private TMP_InputField promptInput; // 使用者輸入額外的基礎提示詞

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
        GeminiGameAgent.Initialize(apiKey);
        EnsureSessionTypeMatch();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 在編輯器內變動 Index 時即時同步變數
        SyncContextToGlobals();
    }
#endif

    private void Start()
    {
        cts = new CancellationTokenSource();
        sendBtn.OnClick = () => SendMessageAsync().Forget();

        // 隱藏原始 Prefab
        if (infoItem != null) infoItem.gameObject.SetActive(false);
        if (contextItemPrefab != null) contextItemPrefab.gameObject.SetActive(false);

        // 初始化情境切換按鈕
        RefreshContextButtons();

        // 同步初始設定
        SyncContextToGlobals();
        EnsureSessionTypeMatch();
    }

    /// <summary>
    /// 根據當前 contextPresets 陣列重新生成切換按鈕
    /// </summary>
    public void RefreshContextButtons()
    {
        if (contextContainer == null || contextItemPrefab == null) return;

        // 清空現有按鈕 (排除 Prefab 本身)
        foreach (Transform child in contextContainer)
        {
            if (child.gameObject != contextItemPrefab.gameObject)
                Destroy(child.gameObject);
        }

        if (contextPresets == null) return;

        for (int i = 0; i < contextPresets.Length; i++)
        {
            int index = i; // 閉包需要
            var item = Instantiate(contextItemPrefab, contextContainer);
            item.gameObject.SetActive(true);

            // 初始化按鈕文字與點擊事件
            item.Init(contextPresets[i].contextName, () =>
            {
                SwitchContext(index);
            });
        }
    }

    /// <summary>
    /// 切換當前啟用的情境
    /// </summary>
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

            Debug.Log($"<color=white>[AgentPanel]</color> 已建立新的 {chatSession.GetType().Name} ({modelType})");
        }
    }

    private async UniTask SendMessageAsync()
    {
        // 修正：使用 .text 取得內容
        string actualPrompt = inputField != null ? inputField.text : "";
        if (string.IsNullOrEmpty(actualPrompt)) return;

        // 修正：使用 .text 清空內容
        inputField.text = "";

        // 產生玩家訊息 UI
        CreateChatItem("You", actualPrompt);

        // 同步參數至 Session
        chatSession.ModelType = modelType;
        chatSession.Temperature = temperature;
        chatSession.MaxOutputTokens = maxOutputTokens;
        chatSession.AutoSummarizeThreshold = summarizeThreshold;

        // 取得當前情境配置
        PromptExtensionConfig currentConfig = (contextPresets != null && contextPresets.Length > activeContextIndex)
            ? contextPresets[activeContextIndex]
            : new PromptExtensionConfig();

        // 取得 promptInput 的內容作為基礎指令
        string baseInstruction = promptInput != null ? promptInput.text : "";

        // 組合最終 System Instruction
        string finalInstruction = currentConfig.BuildFinalSystemInstruction(baseInstruction);

        // 發送前打印完整的系統提示詞
        Debug.Log($"<color=magenta>[System Prompt]</color>\n{finalInstruction}");

        lastResult = "Thinking...";

        try
        {
            var response = await chatSession.ChatAsync(actualPrompt, finalInstruction, useHistory, null, cts.Token);

            if (response.IsSuccess)
            {
                lastResult = response.Text;
                CreateChatItem(currentConfig.contextName, response.Text);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Chat Error: {ex.Message}");
            lastResult = "Error: " + ex.Message;
        }
    }

    private void CreateChatItem(string sender, string content)
    {
        if (infoItem == null) return;

        var obj = Instantiate(infoItem, chatContainer);
        obj.gameObject.SetActive(true);
        obj.Init(sender, content, null);

        // 自動捲動到底部
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

[Serializable]
public class PromptExtensionConfig
{
    public string contextName = "New Context";
    public GeminiGameAgent.GeminiModelType modelType = GeminiGameAgent.GeminiModelType.Gemma_3_12B;
    public bool useHistory = true;
    public float temperature = 0.7f;

    [TextArea(3, 10)] public string personaText;
    [TextArea(3, 10)] public string rulesText;
    [TextArea(3, 10)] public string cleanTextRuleText;
    [TextArea(3, 10)] public string outputFormatText;
    [TextArea(3, 10)] public string examplesText;
    [TextArea(3, 10)] public string thoughtProcessText;

    public string BuildFinalSystemInstruction(string baseInstruction)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(baseInstruction)) sb.AppendLine(baseInstruction);

        if (!string.IsNullOrEmpty(personaText)) sb.AppendLine($"[PERSONA]\n{personaText}");
        if (!string.IsNullOrEmpty(rulesText)) sb.AppendLine($"[RULES]\n{rulesText}");
        if (!string.IsNullOrEmpty(cleanTextRuleText)) sb.AppendLine($"[CLEAN_TEXT_RULE]\n{cleanTextRuleText}");
        if (!string.IsNullOrEmpty(outputFormatText)) sb.AppendLine($"[OUTPUT_FORMAT]\n{outputFormatText}");
        if (!string.IsNullOrEmpty(examplesText)) sb.AppendLine($"[EXAMPLES]\n{examplesText}");
        if (!string.IsNullOrEmpty(thoughtProcessText)) sb.AppendLine($"[THOUGHT_PROCESS]\n{thoughtProcessText}");
        return sb.ToString();
    }
}