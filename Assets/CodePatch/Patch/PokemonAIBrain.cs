using UnityEngine;
using BMC.AI;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using System.IO;
using System.Linq;

public class PokemonAIBrain : MonoBehaviour
{
    public enum AIEngine { Gemini, LMStudio }

    [Header("--- 核心設定 ---")]
    [Tooltip("選擇你要使用的 AI 引擎")]
    public AIEngine engine = AIEngine.Gemini;

    [Tooltip("擷取卡來源 (用來獲取畫面)")]
    public CaptureCardPlayer eye;

    [Header("--- Gemini 設定 ---")]
    public string geminiApiKey = "YOUR_API_KEY";
    public GeminiGameAgent.GeminiModelType geminiModel = GeminiGameAgent.GeminiModelType.Flash_2_5;

    [Header("--- LM Studio 設定 ---")]
    public string lmStudioEndpoint = "[http://127.0.0.1:1234/v1/chat/completions](http://127.0.0.1:1234/v1/chat/completions)";

    [Header("--- 迴圈參數 ---")]
    [Tooltip("每次決策之間的間隔時間 (毫秒)")]
    public int decisionIntervalMs = 3000;

    [Tooltip("連續輸入多個按鍵時，每個按鍵之間的延遲 (毫秒)")]
    public int buttonPressDelayMs = 500;

    [Tooltip("壓縮圖片的品質 (1-100)，越低越省傳輸時間，但太低 AI 會看不懂")]
    [Range(10, 100)]
    public int imageQuality = 50;

    [Tooltip("圖片縮放寬度 (大幅減少 Token 消耗，解決本地模型 OOM 錯誤並提升速度)")]
    public int maxImageWidth = 640;

    [Header("--- 記憶與防卡死系統 ---")]
    public int maxHistoryCount = 8;
    public float stuckDifferenceThreshold = 0.05f;

    [Header("--- 影像與視覺輔助 (Visual Grounding) ---")]
    [Tooltip("自動裁切上下左右黑邊還原GBA畫面")]
    public bool autoCropGBA = true;
    [Tooltip("在AI圖片上繪製半透明網格與互動標記")]
    public bool enableGridOverlay = true;

    [Header("--- 世界地圖拼貼與存檔系統 ---")]
    public bool enableMapStitching = true;
    public bool loadPreviousExploration = true;
    public int worldMapSize = 4096;
    public int saveMapInterval = 5;

    [Header("--- Debug 除錯紀錄 ---")]
    public bool saveDebugLog = true;
    public int maxDebugRecords = 100;

    [Header("--- AI 提示詞設定 ---")]
    public TextAsset promptConfigJson;
    [HideInInspector] public PromptExtensionConfig promptConfig;

    private BaseChatSession _session;
    private CancellationTokenSource _cts;

    private Queue<string> _actionHistory = new Queue<string>();
    private int _currentDebugIndex = 0;
    private string _debugFolderPath;

    private Texture2D _previousFrameTex = null;
    private bool _isScreenStuck = false;

    private Dictionary<string, int> _bannedActions = new Dictionary<string, int>();
    private int _continuousStuckCount = 0;

    // --- 虛擬座標與 NPC 記憶追蹤 ---
    private Vector2Int _playerVirtualCoord = new Vector2Int(0, 0);
    private List<string> _lastExecutedActions = new List<string>();
    private string _lastFacingDirection = "DPAD_DOWN";

    private Dictionary<Vector2Int, string> _npcMemories = new Dictionary<Vector2Int, string>();
    private Queue<Vector2Int> _npcMemoryKeys = new Queue<Vector2Int>();
    private int _maxNpcMemoryCount = 10;

    // --- 已知障礙物佔用網格 (Occupancy Grid) ---
    private HashSet<Vector2Int> _knownObstacles = new HashSet<Vector2Int>();

    private RectInt? _cachedGameBounds = null;

    // --- 世界地圖拼貼系統資源 ---
    private Texture2D _worldMapTex;
    private int _stepsSinceLastMapSave = 0;
    private Vector2Int _lastMappedCoord = new Vector2Int(-999, -999);

    [System.Serializable]
    public class AICommandResponse
    {
        public string current_state;
        public string current_facing;
        public string surroundings_analysis;
        public string distance_analysis;
        public string dialog_content;
        public string reason;
        public List<string> actions;
    }

    [System.Serializable]
    public class ExplorationSaveData
    {
        public int coordX;
        public int coordY;
        public List<Vector2Int> knownObstacles = new List<Vector2Int>();
        public Dictionary<string, string> npcMemories = new Dictionary<string, string>();
    }

    void Start()
    {
        if (eye == null)
        {
            Debug.LogError("[大腦] 請綁定 CaptureCardPlayer！");
            return;
        }

        if (promptConfigJson != null)
        {
            promptConfig = JsonConvert.DeserializeObject<PromptExtensionConfig>(promptConfigJson.text);
        }
        else
        {
            promptConfig = new PromptExtensionConfig
            {
                personaText = "你現在是一個自動通關寶可夢火紅版的 AI 玩家。",
                outputFormatText = "請嚴格以 JSON 格式回傳..."
            };
        }

        _debugFolderPath = Path.Combine(Application.persistentDataPath, "AIDebugLogs");
        if (!Directory.Exists(_debugFolderPath)) Directory.CreateDirectory(_debugFolderPath);

        if (enableMapStitching)
        {
            _worldMapTex = new Texture2D(worldMapSize, worldMapSize, TextureFormat.RGBA32, false);
            Color32[] clearPixels = new Color32[worldMapSize * worldMapSize];
            for (int i = 0; i < clearPixels.Length; i++) clearPixels[i] = new Color32(0, 0, 0, 0);
            _worldMapTex.SetPixels32(clearPixels);
            _worldMapTex.Apply();

            LoadExplorationData();
        }

        if (engine == AIEngine.Gemini)
        {
            GeminiGameAgent.Initialize(geminiApiKey);
            if (GeminiGameAgent.IsGemmaModel(promptConfig.modelType))
                _session = new GemmaChatSession(promptConfig.modelType);
            else
                _session = new GeminiChatSession(promptConfig.modelType);
        }
        else if (engine == AIEngine.LMStudio)
        {
            _session = new LMStudioSession
            {
                EndpointUrl = lmStudioEndpoint,
                Temperature = promptConfig.temperature,
                MaxOutputTokens = 4096
            };
        }

        _cts = new CancellationTokenSource();
        RunBrainLoopAsync(_cts.Token).Forget();
    }

    private void LoadExplorationData()
    {
        if (!loadPreviousExploration) return;

        string mapPath = Path.Combine(_debugFolderPath, "WorldMap_Latest.png");
        string dataPath = Path.Combine(_debugFolderPath, "ExplorationData.json");

        if (File.Exists(mapPath) && File.Exists(dataPath))
        {
            try
            {
                byte[] mapBytes = File.ReadAllBytes(mapPath);
                _worldMapTex.LoadImage(mapBytes);

                string json = File.ReadAllText(dataPath);
                var data = JsonConvert.DeserializeObject<ExplorationSaveData>(json);
                if (data != null)
                {
                    _playerVirtualCoord = new Vector2Int(data.coordX, data.coordY);
                    _knownObstacles = new HashSet<Vector2Int>(data.knownObstacles);

                    _npcMemories.Clear();
                    _npcMemoryKeys.Clear();
                    foreach (var kvp in data.npcMemories)
                    {
                        var parts = kvp.Key.Split(',');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                        {
                            Vector2Int pos = new Vector2Int(x, y);
                            _npcMemories[pos] = kvp.Value;
                            _npcMemoryKeys.Enqueue(pos);
                        }
                    }
                    Debug.Log($"<color=green>💾 成功讀取探索存檔！恢復座標: {_playerVirtualCoord}，已記憶障礙物: {_knownObstacles.Count} 個</color>");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"<color=yellow>讀取存檔失敗: {ex.Message}</color>");
            }
        }
    }

    private async UniTaskVoid RunBrainLoopAsync(CancellationToken token)
    {
        Debug.Log("<color=green>[大腦] AI 自動遊玩迴圈已啟動！</color>");
        string systemInstruction = promptConfig.BuildFinalSystemInstruction("");

        while (!token.IsCancellationRequested)
        {
            try
            {
                // 1. 取得完整遊戲畫面（已畫上網格）
                byte[] imageBytes = GetScreenshotAndCheckStuck();

                if (imageBytes != null && imageBytes.Length > 0)
                {
                    string actionLog = _actionHistory.Count > 0 ? string.Join(" -> ", _actionHistory) : "無";
                    string npcMemoryStr = _npcMemories.Count > 0 ? string.Join("", _npcMemories.Select(kv => $"\n- 座標({kv.Key.x}, {kv.Key.y}): {kv.Value}")) : "無";

                    // 2. 組合純粹的動態數據
                    string dynamicPrompt = $"請根據畫面決定下一步操作。\n\n[當前系統數據]\n- 虛擬座標：({_playerVirtualCoord.x}, {_playerVirtualCoord.y})\n- 近期動作：{actionLog}\n- NPC對話記憶：{npcMemoryStr}";

                    string lastActionStr = _actionHistory.Count > 0 ? _actionHistory.ToArray()[_actionHistory.Count - 1] : "無";

                    List<string> activeBans = new List<string>();
                    List<string> keysToUpdate = new List<string>(_bannedActions.Keys);
                    foreach (var key in keysToUpdate)
                    {
                        if (_bannedActions[key] > 0) { activeBans.Add(key); _bannedActions[key]--; }
                        else { _bannedActions.Remove(key); }
                    }

                    // 3. 動態事件與警告層
                    if (_isScreenStuck)
                    {
                        if (lastActionStr.Contains("A"))
                        {
                            dynamicPrompt += $"\n\n🚨 【系統反饋】：偵測到畫面無顯著變化。若你判定目前為「對話中」則屬正常，請繼續按 [\"A\"]；若非對話中，代表你在對空氣按 A，請移動離開！";
                        }
                        else
                        {
                            _continuousStuckCount++;
                            if (lastActionStr != "無" && lastActionStr != "[無動作]")
                            {
                                _bannedActions[lastActionStr] = 3;
                                if (!activeBans.Contains(lastActionStr)) activeBans.Add(lastActionStr);
                            }

                            string banListStr = string.Join(" 或 ", activeBans);
                            dynamicPrompt += $"\n\n🚨 【系統警告】：剛剛的指令 {lastActionStr} 撞牆了，該位置已被標記為⬛黑色障礙物。";

                            if (_continuousStuckCount >= 2)
                                dynamicPrompt += $"\n⚠️ 【死亡迴圈】：已連續卡住 {_continuousStuckCount} 次！";

                            dynamicPrompt += $"\n⚠️ 【強制封印】：目前已被封印的動作有：{banListStr}。請「絕對不要」使用，並往其他安全方向移動！";
                        }
                    }
                    else
                    {
                        _continuousStuckCount = 0;
                        dynamicPrompt += "\n\n✅ 【系統反饋】：畫面有成功變動，動作有效。";
                        if (activeBans.Count > 0)
                        {
                            string banListStr = string.Join("、", activeBans);
                            dynamicPrompt += $"\n⚠️ 【地形記憶】：為避免死胡同，請避開這些曾撞牆的方向：{banListStr}。";
                        }
                    }

                    Debug.Log($"<color=yellow>[大腦] 思考中... (目前座標: {_playerVirtualCoord})</color>");

                    var response = await _session.ChatAsync(userPrompt: dynamicPrompt, systemInstruction: systemInstruction, useHistory: false, imageBytes: imageBytes, ct: token);

                    if (response.IsSuccess)
                    {
                        string finishReason = response.FinishReason?.ToLower() ?? "";
                        if (finishReason == "length" || finishReason == "max_tokens")
                        {
                            Debug.LogWarning($"<color=orange>[大腦警告]</color> AI 思考未完畢，跳過本次控制。");
                            SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, "無", "無", "無", "無", "無", "[跳過] 思考未完畢", new List<string>(), response, response.Text);
                        }
                        else
                        {
                            await ParseAndExecuteCommandsAsync(response.Text, imageBytes, dynamicPrompt, npcMemoryStr, token, response);
                        }
                    }
                    else
                    {
                        Debug.LogError($"[大腦] API 呼叫失敗: {response.Error}");
                        SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, "無", "無", "無", "無", "無", $"[API 呼叫失敗]\n{response.Error}", new List<string>(), response, response.Text ?? "無");
                    }
                }
            }
            catch (System.Exception ex) { Debug.LogError($"[大腦迴圈發生異常]: {ex.Message}"); }

            await UniTask.Delay(decisionIntervalMs, cancellationToken: token);
        }
    }

    private byte[] GetScreenshotAndCheckStuck()
    {
        if (eye.displayImage.texture is WebCamTexture camTexture && camTexture.isPlaying)
        {
            if (camTexture.width > 100)
            {
                int srcWidth = camTexture.width;
                int srcHeight = camTexture.height;

                int targetWidth = maxImageWidth;
                int targetHeight = Mathf.RoundToInt((float)srcHeight / srcWidth * targetWidth);

                RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
                Graphics.Blit(camTexture, rt);

                Texture2D fullTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                fullTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                fullTex.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                RectInt bounds = new RectInt(0, 0, targetWidth, targetHeight);

                if (autoCropGBA)
                {
                    RectInt currentBounds = FindActiveGameBounds(fullTex);
                    if (currentBounds.width > targetWidth * 0.4f && currentBounds.height > targetHeight * 0.4f)
                        _cachedGameBounds = currentBounds;
                    if (_cachedGameBounds.HasValue) bounds = _cachedGameBounds.Value;
                }

                Texture2D rawTex;
                if (bounds.width == targetWidth && bounds.height == targetHeight)
                    rawTex = fullTex;
                else
                {
                    rawTex = new Texture2D(bounds.width, bounds.height, TextureFormat.RGB24, false);
                    rawTex.SetPixels(fullTex.GetPixels(bounds.x, bounds.y, bounds.width, bounds.height));
                    rawTex.Apply();
                    Destroy(fullTex);
                }

                if (_previousFrameTex != null)
                {
                    float diff = CalculateImageDifference(_previousFrameTex, rawTex);
                    _isScreenStuck = (diff < stuckDifferenceThreshold);

                    if (!_isScreenStuck && _lastExecutedActions.Count > 0)
                    {
                        foreach (var action in _lastExecutedActions)
                        {
                            if (action.StartsWith("DPAD_"))
                            {
                                if (_lastFacingDirection != action)
                                {
                                    _lastFacingDirection = action;
                                }
                                else
                                {
                                    if (action == "DPAD_UP") _playerVirtualCoord.y++;
                                    else if (action == "DPAD_DOWN") _playerVirtualCoord.y--;
                                    else if (action == "DPAD_LEFT") _playerVirtualCoord.x--;
                                    else if (action == "DPAD_RIGHT") _playerVirtualCoord.x++;

                                    if (_knownObstacles.Contains(_playerVirtualCoord))
                                    {
                                        _knownObstacles.Remove(_playerVirtualCoord);
                                    }
                                }
                            }
                        }
                    }
                    else if (_isScreenStuck && _lastExecutedActions.Count > 0)
                    {
                        string stuckDir = _lastExecutedActions.FirstOrDefault(a => a.StartsWith("DPAD_"));
                        if (stuckDir != null)
                        {
                            Vector2Int obstacleCoord = _playerVirtualCoord;
                            if (stuckDir == "DPAD_UP") obstacleCoord.y++;
                            else if (stuckDir == "DPAD_DOWN") obstacleCoord.y--;
                            else if (stuckDir == "DPAD_LEFT") obstacleCoord.x--;
                            else if (stuckDir == "DPAD_RIGHT") obstacleCoord.x++;
                            _knownObstacles.Add(obstacleCoord);
                        }
                    }

                    _lastExecutedActions.Clear();
                    Destroy(_previousFrameTex);
                }

                _previousFrameTex = rawTex;

                if (enableMapStitching && _playerVirtualCoord != _lastMappedCoord)
                {
                    UpdateWorldMap(rawTex);
                    _lastMappedCoord = _playerVirtualCoord;

                    _stepsSinceLastMapSave++;
                    if (_stepsSinceLastMapSave >= saveMapInterval)
                    {
                        SaveWorldMap();
                        _stepsSinceLastMapSave = 0;
                    }
                }

                // ==========================================
                // 產生供 AI 觀看的帶網格完整圖片
                // ==========================================
                Texture2D aiTex = new Texture2D(bounds.width, bounds.height, TextureFormat.RGB24, false);
                aiTex.SetPixels32(rawTex.GetPixels32());
                aiTex.Apply();

                if (enableGridOverlay)
                {
                    DrawGridAndOverlay(aiTex);
                }

                byte[] jpgBytes = aiTex.EncodeToJPG(imageQuality);
                Destroy(aiTex);

                return jpgBytes;
            }
        }
        return null;
    }

    private void UpdateWorldMap(Texture2D frame)
    {
        if (_worldMapTex == null) return;

        int w = frame.width;
        int h = frame.height;
        float tw = w / 15f;
        float th = h / 10f;

        int centerX = _worldMapTex.width / 2;
        int centerY = _worldMapTex.height / 2;

        int startX = centerX + Mathf.RoundToInt((_playerVirtualCoord.x - 7) * tw);
        int startY = centerY + Mathf.RoundToInt((_playerVirtualCoord.y - 4) * th);

        if (startX < 0 || startY < 0 || startX + w >= _worldMapTex.width || startY + h >= _worldMapTex.height)
            return;

        Color[] existingMap = _worldMapTex.GetPixels(startX, startY, w, h);
        Color[] newFrame = frame.GetPixels();

        int pMinX = Mathf.RoundToInt(7 * tw);
        int pMaxX = Mathf.RoundToInt(8 * tw);
        int pMinY = Mathf.RoundToInt(4 * th);
        int pMaxY = Mathf.RoundToInt(6.5f * th);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int index = y * w + x;
                bool isPlayerArea = (x >= pMinX && x <= pMaxX && y >= pMinY && y <= pMaxY);

                if (isPlayerArea && existingMap[index].a > 0.5f)
                    newFrame[index] = existingMap[index];
                else
                    newFrame[index].a = 1f;
            }
        }

        _worldMapTex.SetPixels(startX, startY, w, h, newFrame);
        _worldMapTex.Apply();
    }

    private void SaveWorldMap()
    {
        if (_worldMapTex == null || !saveDebugLog) return;
        try
        {
            string mapPath = Path.Combine(_debugFolderPath, "WorldMap_Latest.png");
            File.WriteAllBytes(mapPath, _worldMapTex.EncodeToPNG());

            string dataPath = Path.Combine(_debugFolderPath, "ExplorationData.json");
            var saveData = new ExplorationSaveData
            {
                coordX = _playerVirtualCoord.x,
                coordY = _playerVirtualCoord.y,
                knownObstacles = _knownObstacles.ToList()
            };

            foreach (var kvp in _npcMemories)
                saveData.npcMemories[$"{kvp.Key.x},{kvp.Key.y}"] = kvp.Value;

            string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
            File.WriteAllText(dataPath, json);
        }
        catch (System.Exception e) { Debug.LogWarning($"儲存探索進度失敗: {e.Message}"); }
    }

    private RectInt FindActiveGameBounds(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        int width = tex.width;
        int height = tex.height;

        int minX = width, maxX = 0, minY = height, maxY = 0;
        const int threshold = 20;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 c = pixels[y * width + x];
                if (c.r > threshold || c.g > threshold || c.b > threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (minX > maxX || minY > maxY) return new RectInt(0, 0, width, height);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private void DrawGridAndOverlay(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();
        int width = tex.width;
        int height = tex.height;

        int columns = 15;
        int rows = 10;

        float cellW = width / (float)columns;
        float cellH = height / (float)rows;

        Color32 gridColor = new Color32(255, 255, 255, 60);
        Color32 playerColor = new Color32(255, 0, 0, 80);
        Color32 interactColor = new Color32(0, 255, 0, 60);
        Color32 obstacleColor = new Color32(0, 0, 0, 180);

        int playerCol = 7;
        int playerRow = 5;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int col = Mathf.FloorToInt(x / cellW);
                int row = Mathf.FloorToInt(y / cellH);

                int visualRow = (rows - 1) - row;

                int absoluteX = _playerVirtualCoord.x + (col - playerCol);
                int absoluteY = _playerVirtualCoord.y + (playerRow - visualRow);
                Vector2Int cellCoord = new Vector2Int(absoluteX, absoluteY);

                bool isPlayerCell = (col == playerCol && visualRow == playerRow);
                bool isInteractCell = (col == playerCol && Mathf.Abs(visualRow - playerRow) == 1) ||
                                      (visualRow == playerRow && Mathf.Abs(col - playerCol) == 1);

                bool isObstacle = _knownObstacles.Contains(cellCoord);

                bool isLine = (x % cellW < 2 || y % cellH < 2);
                int index = y * width + x;

                if (isLine) pixels[index] = BlendColor(pixels[index], gridColor);
                else if (isObstacle) pixels[index] = BlendColor(pixels[index], obstacleColor);
                else if (isPlayerCell) pixels[index] = BlendColor(pixels[index], playerColor);
                else if (isInteractCell) pixels[index] = BlendColor(pixels[index], interactColor);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
    }

    private Color32 BlendColor(Color32 baseColor, Color32 overlayColor)
    {
        float alpha = overlayColor.a / 255f;
        byte r = (byte)((overlayColor.r * alpha) + (baseColor.r * (1 - alpha)));
        byte g = (byte)((overlayColor.g * alpha) + (baseColor.g * (1 - alpha)));
        byte b = (byte)((overlayColor.b * alpha) + (baseColor.b * (1 - alpha)));
        return new Color32(r, g, b, 255);
    }

    private float CalculateImageDifference(Texture2D tex1, Texture2D tex2)
    {
        if (tex1.width != tex2.width || tex1.height != tex2.height) return 1f;

        Color32[] p1 = tex1.GetPixels32();
        Color32[] p2 = tex2.GetPixels32();
        int diffCount = 0;
        int sampleCount = 0;

        for (int i = 0; i < p1.Length; i += 10)
        {
            sampleCount++;
            if (Mathf.Abs(p1[i].r - p2[i].r) > 20 || Mathf.Abs(p1[i].g - p2[i].g) > 20 || Mathf.Abs(p1[i].b - p2[i].b) > 20)
                diffCount++;
        }

        return (float)diffCount / sampleCount;
    }

    private async UniTask ParseAndExecuteCommandsAsync(string jsonText, byte[] imageBytes, string dynamicPrompt, string npcMemoryStr, CancellationToken token, GeminiResponseData responseData)
    {
        string rawAIResponse = jsonText;

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, "無", "無", "無", "無", "無", "[失敗] 模型回傳為空", null, responseData, rawAIResponse);
            return;
        }

        int startIndex = jsonText.IndexOf('{');
        int endIndex = jsonText.LastIndexOf('}');

        if (startIndex != -1 && endIndex != -1 && endIndex >= startIndex)
            jsonText = jsonText.Substring(startIndex, endIndex - startIndex + 1);
        else
        {
            SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, "無", "無", "無", "無", "無", $"[JSON 解析失敗]\n找不到括號", null, responseData, rawAIResponse);
            return;
        }

        try
        {
            AICommandResponse commandData = JsonConvert.DeserializeObject<AICommandResponse>(jsonText);

            if (commandData != null && commandData.actions != null && commandData.actions.Count > 0)
            {
                Debug.Log($"<color=magenta>[狀態] {commandData.current_state ?? "未知"} | 朝向: {commandData.current_facing ?? "未知"} | 對話: {commandData.dialog_content ?? "無"}</color>");

                if (!string.IsNullOrEmpty(commandData.current_state) && commandData.current_state.Contains("對話中") &&
                    !string.IsNullOrEmpty(commandData.dialog_content) && commandData.dialog_content != "無")
                {
                    if (_npcMemories.ContainsKey(_playerVirtualCoord))
                    {
                        if (!_npcMemories[_playerVirtualCoord].Contains(commandData.dialog_content))
                        {
                            _npcMemories[_playerVirtualCoord] += " | " + commandData.dialog_content;
                            if (_npcMemories[_playerVirtualCoord].Length > 300)
                                _npcMemories[_playerVirtualCoord] = "..." + _npcMemories[_playerVirtualCoord].Substring(_npcMemories[_playerVirtualCoord].Length - 250);
                        }
                    }
                    else
                    {
                        _npcMemories[_playerVirtualCoord] = commandData.dialog_content;
                        _npcMemoryKeys.Enqueue(_playerVirtualCoord);
                        if (_npcMemoryKeys.Count > _maxNpcMemoryCount)
                        {
                            var oldKey = _npcMemoryKeys.Dequeue();
                            _npcMemories.Remove(oldKey);
                        }
                    }
                }

                string currentActionSummary = "[" + string.Join(", ", commandData.actions) + "]";
                _actionHistory.Enqueue(currentActionSummary);
                if (_actionHistory.Count > maxHistoryCount) _actionHistory.Dequeue();

                _lastExecutedActions.Clear();
                _lastExecutedActions.AddRange(commandData.actions);

                SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, commandData.current_state ?? "無", commandData.current_facing ?? "無", commandData.surroundings_analysis ?? "無", commandData.distance_analysis ?? "無", commandData.dialog_content ?? "無", commandData.reason, commandData.actions, responseData, rawAIResponse);

                for (int i = 0; i < commandData.actions.Count; i++)
                {
                    SwitchControllerHelper.SendSwitchCommand(commandData.actions[i]);
                    if (i < commandData.actions.Count - 1) await UniTask.Delay(buttonPressDelayMs, cancellationToken: token);
                }
            }
            else
            {
                _actionHistory.Enqueue("[無動作]");
                if (_actionHistory.Count > maxHistoryCount) _actionHistory.Dequeue();

                _lastExecutedActions.Clear();
                SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, commandData?.current_state ?? "無", commandData?.current_facing ?? "無", commandData?.surroundings_analysis ?? "無", commandData?.distance_analysis ?? "無", commandData?.dialog_content ?? "無", commandData?.reason ?? "無", new List<string> { "無" }, responseData, rawAIResponse);
            }
        }
        catch (JsonException ex)
        {
            SaveDebugRecord(imageBytes, dynamicPrompt, npcMemoryStr, "無", "無", "無", "無", "無", $"[JSON 解析失敗]\n{ex.Message}", null, responseData, rawAIResponse);
        }
    }

    private void SaveDebugRecord(byte[] imageBytes, string dynamicPrompt, string npcMemoryStr, string currentState, string currentFacing, string surroundingsAnalysis, string distanceAnalysis, string dialogContent, string aiReason, List<string> actions, GeminiResponseData responseData, string rawAIResponse)
    {
        if (!saveDebugLog || imageBytes == null) return;

        try
        {
            string filePrefix = $"Record_{_currentDebugIndex:D3}";
            string imagePath = Path.Combine(_debugFolderPath, $"{filePrefix}.jpg");
            string textPath = Path.Combine(_debugFolderPath, $"{filePrefix}.txt");

            File.WriteAllBytes(imagePath, imageBytes);
            string actionStr = (actions != null && actions.Count > 0) ? string.Join(", ", actions) : "無";

            string actualModel = engine == AIEngine.Gemini ? promptConfig.modelType.ToString() : "LM Studio Local";
            int tokens = responseData?.TotalTokens ?? 0;
            float floatTime = responseData?.ResponseTime ?? 0f;
            string finishReason = responseData?.FinishReason ?? "N/A";

            string logContent = $"=== AI 決策紀錄 ===\n時間: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                $"【當前虛擬座標】: ({_playerVirtualCoord.x}, {_playerVirtualCoord.y})\n" +
                                $"【使用模型】: {actualModel}\n" +
                                $"【消耗 Token】: {tokens}\n" +
                                $"【思考時間】: {floatTime:F2} 秒\n" +
                                $"【結束原因】: {finishReason}\n" +
                                $"---------------------------\n【餵給 AI 的 Prompt】\n{dynamicPrompt}\n" +
                                $"---------------------------\n【NPC 對話情報記憶】\n{npcMemoryStr}\n" +
                                $"---------------------------\n【AI 的狀態判定】\n{currentState}\n" +
                                $"---------------------------\n【AI 自我判斷朝向】\n{currentFacing}\n" +
                                $"---------------------------\n【AI 的環境預判】\n{surroundingsAnalysis}\n" +
                                $"---------------------------\n【AI 的距離評估】\n{distanceAnalysis}\n" +
                                $"---------------------------\n【AI 的對話理解】\n{dialogContent}\n" +
                                $"---------------------------\n【AI 的思考理由】\n{aiReason}\n" +
                                $"---------------------------\n【最終執行按鍵】: {actionStr}\n" +
                                $"【物理防撞牆偵測狀態】: {(_isScreenStuck ? "🔴 已觸發" : "🟢 正常移動")}\n" +
                                $"===========================\n【AI 完整原始回傳內容 (Raw Response)】\n{rawAIResponse}\n===========================\n";

            File.WriteAllText(textPath, logContent);
            _currentDebugIndex = (_currentDebugIndex + 1) % maxDebugRecords;
        }
        catch (System.Exception ex) { Debug.LogWarning($"[Debug儲存失敗] {ex.Message}"); }
    }

    void OnDestroy()
    {
        if (_cts != null) { _cts.Cancel(); _cts.Dispose(); }
        if (_previousFrameTex != null) Destroy(_previousFrameTex);
        SaveWorldMap();
    }
}