using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using BMC.Core;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI 事件識別碼。INPUT_ 系列刻意與 uGUI 版 BMC.UI.UIEvent 同名同語意，
    /// 專案既有的輸入層只要多送一份到 BMC.UIToolkit.UITMgr.eventHandler，
    /// 兩套 UI 的手把操作行為就會一致；本套件也附了 BMC.UIToolkit.Joypad 這個
    /// 可選組件當作預設的輸入來源（見 Runtime/Joypad）。
    /// </summary>
    public enum UIEvent
    {
        NONE = 0,

        AUDIO_BUTTON_CLICK,

        INPUT_UP,
        INPUT_DOWN,
        INPUT_LEFT,
        INPUT_RIGHT,

        INPUT_A,
        INPUT_B,
        INPUT_X,
        INPUT_Y,

        INPUT_SHOULDER_L,
        INPUT_SHOULDER_R,
        INPUT_TRIGGER_L,
        INPUT_TRIGGER_R,

        INPUT_START,
        INPUT_SELECT,

        INPUT_STICK_R,
    }

    /// <summary>
    /// UI Toolkit 版的畫面分層，對應 uGUI 版 BMC.UI 的 UICanvasType。
    /// UI Toolkit 沒有 Canvas.sortingOrder，改以 VisualElement 的子物件疊放順序（後加入者在上層）決定層級。
    /// </summary>
    public enum UILayer
    {
        UI_0 = 0,
        SCENE_UI_0,

        UI_1,
        SCENE_UI_1,

        UI_2,
        SCENE_UI_2,

        UI_3,
        SCENE_UI_3,

        UI_4,
        SCENE_UI_4,

        UI_Top,
        SCENE_UI_TOP,

        UI_Debug,
    }

    /// <summary>
    /// UI Toolkit 版的介面管理員。類別名刻意做成 UITMgr，
    /// 以免與 uGUI 版 BMC.UI.UIMgr 在同一個檔案裡撞名。
    /// </summary>
    public partial class UITMgr : Singleton<UITMgr>
    {
        /// <summary>
        /// 所有分層容器，**照 UILayer 的宣告順序**建立，越後面越上層。
        ///
        /// 【為什麼不把場景層另外掛成一棵子樹】原本的做法是把整個 sceneRoot 塞進
        /// 全域 UI_1 底下，結果是「任何 SCENE_* 都在全域 UI_2 以下」——
        /// 跟 enum 的順序完全對不上。實際症狀：從 UI_2 的面板開一張 SCENE_UI_2 的面板，
        /// 輸入正常（手把堆疊看的是開啟順序）但畫面被蓋在後面，看起來像沒開起來。
        ///
        /// uGUI 版是每一層各自一個 Canvas、用 sortingOrder 排，層級本來就照 enum 交錯。
        /// 這裡改成同一套語意：全部拉平成兄弟節點，場景層只是「換場景時要清掉的那幾層」，
        /// 不再是結構上的巢狀關係。
        /// </summary>
        private static readonly UILayer[] AllLayerOrder =
        {
            UILayer.UI_0, UILayer.SCENE_UI_0,
            UILayer.UI_1, UILayer.SCENE_UI_1,
            UILayer.UI_2, UILayer.SCENE_UI_2,
            UILayer.UI_3, UILayer.SCENE_UI_3,
            UILayer.UI_4, UILayer.SCENE_UI_4,
            UILayer.UI_Top, UILayer.SCENE_UI_TOP,
            UILayer.UI_Debug,
        };

        private const string ASSET_UI_ROOT = "UIDocumentRoot";

        /// <summary>
        /// 預設主題的資源位址。PanelSettings.themeStyleSheet 不可為 null，執行期自建根節點時需要它。
        /// </summary>
        private const string ASSET_DEFAULT_THEME = "UIT_Theme";

        /// <summary>
        /// 面板資源的位址前綴。
        ///
        /// uGUI 版 BMC.UI 直接以類別名稱當資源位址，而收集器採 AddressByFileName，
        /// 因此兩套同時存在時位址會正面衝突——兩邊都有 Toast 與 MsgPanel，
        /// 先被收集到的那個（uGUI 的 .prefab）會蓋掉另一個，載入時型別對不上而失敗。
        /// 本套件的資源一律加上前綴來區隔命名空間。
        ///
        /// 專案若有自己的命名規則，可在啟動時改寫此值。
        /// </summary>
        public static string AddressPrefix { get; set; } = "UIT_";

        /// <summary>
        /// 取得面板類別對應的資源位址。
        /// </summary>
        public static string GetPanelAddress(System.Type panelType) => AddressPrefix + panelType.Name;

        public Core.EventHandler eventHandler = new();

        private GameObject rootGo;
        private UIDocument rootDocument;
        private Dictionary<UILayer, VisualElement> layers;
        public bool IsSceneInit { get; private set; }

        private List<UIPanel> panels;

        /// <summary>
        /// 追蹤還在載入中的 ShowPanel&lt;T&gt; 請求(鍵為面板型別，值為 UniTask&lt;T&gt;.Preserve() 過的
        /// 可重複等待版本)。ShowPanel 內部要 await 資源載入才會把面板加進 panels 清單，
        /// 在那之前 checkSame 完全看不到「已經在開了」——同一顆按鈕在極短時間內被觸發兩次
        /// (例如輸入事件重複派送)就會建出兩份面板、各自搶同一批資源。這裡讓載入中的重複請求
        /// 直接等同一份 task，而不是各自起一份新的。
        /// </summary>
        private readonly Dictionary<Type, object> pendingShowTasks = new();

        protected override void Init()
        {
            panels = new List<UIPanel>();
            IsSceneInit = false;

            // 統一在這裡把輸入事件轉發到最上層的 IJoypadPanel（實作見 UITMgr.Joypad.cs）
            RegisterGlobalJoypadEvents();
        }

        /// <summary>
        /// 載入全域 UIDocument 根節點（"UIDocumentRoot" 預製物件需掛有 UIDocument 元件，透過 ResMgr 載入），
        /// 並依 GlobalLayerOrder 建立分層容器。對應 uGUI 版的 LoadGlobalCanvas。
        /// </summary>
        public async UniTask LoadGlobalRoot()
        {
            rootGo = await ResMgr.Instance.LoadAssetAsync<GameObject>(ASSET_UI_ROOT, true, null);
            rootGo.name = "Global_UIRoot";
            GameObject.DontDestroyOnLoad(rootGo);

            var document = rootGo.GetComponent<UIDocument>();
            if (document == null)
            {
                Log.Error($"[UITMgr] {ASSET_UI_ROOT} 缺少 UIDocument 元件");
                return;
            }

            UseRootDocument(document);
        }

        /// <summary>
        /// 直接使用場景中已存在的 UIDocument 作為全域根節點，不透過 ResMgr 載入。
        /// 適用於範例／測試場景等不依賴資源系統的情境。
        /// </summary>
        public void UseRootDocument(UIDocument document)
        {
            rootDocument = document;

            // UIDocument.rootVisualElement 預設只有 width:100%，沒有 height:100%
            // （設計上允許內容自訂高度）。我們掛在底下的分層容器都是 position:absolute，
            // 不會撐出高度，若不強制補上 height:100%，rootVisualElement 會直接坍縮成 0 高，
            // 導致所有子層跟著坍縮。
            rootDocument.rootVisualElement.style.width = Length.Percent(100);
            rootDocument.rootVisualElement.style.height = Length.Percent(100);

            layers = BuildLayers(rootDocument.rootVisualElement, AllLayerOrder);
        }

        /// <summary>
        /// 全域根節點是否已建立。
        /// </summary>
        public bool IsRootReady => rootDocument != null;

        /// <summary>
        /// 確保全域根節點存在：若專案尚未透過 LoadGlobalRoot 或 UseRootDocument 指定，
        /// 就地建立一個帶預設 PanelSettings 的 UIDocument。
        /// 讓套件內建的基礎介面（MsgPanel／Toast）在專案還沒接好資源系統時也能運作。
        /// </summary>
        /// <param name="sortingOrder">UIDocument 的排序值，數值越大越上層</param>
        public async UniTask EnsureRuntimeRootAsync(float sortingOrder = 100f)
        {
            if (IsRootReady)
                return;

            var theme = await LoadAsset<ThemeStyleSheet>(ASSET_DEFAULT_THEME);
            if (theme == null)
                return;

            // 先建立為停用狀態，等 panelSettings 指派完再啟用：
            // UIDocument 在 OnEnable 就會依 panelSettings 建立 rootVisualElement，
            // 順序顛倒會拿到 null 的 rootVisualElement。
            var go = new GameObject("Global_UIRoot(Runtime)");
            go.SetActive(false);
            GameObject.DontDestroyOnLoad(go);

            var document = go.AddComponent<UIDocument>();
            document.panelSettings = CreateDefaultPanelSettings(theme, sortingOrder);
            go.SetActive(true);

            UseRootDocument(document);
        }

        private static PanelSettings CreateDefaultPanelSettings(ThemeStyleSheet theme, float sortingOrder)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.sortingOrder = sortingOrder;
            settings.themeStyleSheet = theme;
            return settings;
        }

        /// <summary>
        /// 由資源系統取得指定位址的資產。先驗證位址再載入，
        /// 位址不存在時回傳 null 並給出可讀訊息，而不是讓 YooAsset 直接拋錯。
        /// </summary>
        private static async UniTask<T> LoadAsset<T>(string address) where T : UnityEngine.Object
        {
            if (!ResMgr.Instance.Check(address))
            {
                Log.Error($"[UITMgr] 資源位址不存在: '{address}'。請確認 BMC.UIToolkit 的 Basic Controls 資源已加入資源收集器。");
                return null;
            }

            return await ResMgr.Instance.LoadAssetAsync<T>(address, false);
        }

        /// <summary>
        /// 建立場景專用分層容器，掛載於全域 UI_1 之上。對應 uGUI 版的 CreateSceneUIRoot。
        /// </summary>
        public void CreateSceneRoot()
        {
            // 分層容器在 UseRootDocument 就全部建好了（含場景層），這裡只要把上一個場景
            // 留下的東西清乾淨再標記可用 —— 容器本身不重建，順序才不會跑掉。
            ResetSceneRoot();
            IsSceneInit = true;
        }

        public void ResetSceneRoot()
        {
            // 【要走完整的關閉流程，不能只把清單清掉】場景層的面板可能同時是手把面板
            // （會留在 joypadPanels 裡）、可能接管了外部狀態（例如把 uGUI 的
            // UIMgr.UIMaskControl 換掉）、也可能有排程或 CancellationToken 要收。
            // 只從 panels 移除的話這些全部殘留。
            //
            // 實際踩過：從遊戲選單登出回主畫面、再進遊戲，殘留的舊 GamePanel 還在手把堆疊
            // 最上層，按鍵打到它，登出確認視窗又跳一次。滑鼠不會有這個症狀 ——
            // 滑鼠事件走視覺樹，而那棵樹已經隨場景根節點移除了；手把是查堆疊，查得到。
            //
            // 先收集再關閉：InternalClose 會回頭動 panels（它要關掉自己的子面板），
            // 不能邊走訪邊改。
            var closing = new List<UIPanel>();
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                if (!IsSceneLayer(panels[i].Layer))
                    continue;
                closing.Add(panels[i]);
                panels.RemoveAt(i);
            }

            foreach (var panel in closing)
                panel.InternalClose();

            // 容器留著（順序由 AllLayerOrder 決定），只清掉裡面的東西
            if (layers != null)
            {
                foreach (var layer in AllLayerOrder)
                    if (IsSceneLayer(layer) && layers.TryGetValue(layer, out var ve))
                        ve.Clear();
            }

            IsSceneInit = false;
        }

        private static bool IsSceneLayer(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.SCENE_UI_0:
                case UILayer.SCENE_UI_1:
                case UILayer.SCENE_UI_2:
                case UILayer.SCENE_UI_3:
                case UILayer.SCENE_UI_4:
                case UILayer.SCENE_UI_TOP:
                    return true;
                default:
                    return false;
            }
        }

        private Dictionary<UILayer, VisualElement> BuildLayers(VisualElement root, UILayer[] order)
        {
            var dict = new Dictionary<UILayer, VisualElement>();
            foreach (var layer in order)
            {
                var ve = new VisualElement { name = $"Layer_{layer}" };
                ve.StretchToParentSize();
                ve.pickingMode = PickingMode.Ignore;
                root.Add(ve);
                dict[layer] = ve;
            }
            return dict;
        }

        private VisualElement GetLayer(UILayer layer)
        {
            if (layers == null)
            {
                Log.Error($"[UILayer] 全域根節點尚未建立，請先呼叫 LoadGlobalRoot／UseRootDocument／EnsureRuntimeRootAsync: {layer}");
                return null;
            }

            if (IsSceneLayer(layer) && !IsSceneInit)
            {
                Log.Error($"[UILayer] 場景尚未初始化: {layer}");
                return null;
            }

            return layers[layer];
        }

        /// <summary>
        /// 目前開啟中的面板。UI Toolkit 的面板不是 GameObject，
        /// 無法從 Hierarchy 觀察，因此開放這份清單供除錯工具查詢。
        /// </summary>
        public IReadOnlyList<UIPanel> OpenPanels => panels;

        public T GetPanel<T>() where T : UIPanel
        {
            foreach (var p in panels)
            {
                if (p is T t)
                    return t;
            }
            return null;
        }

        /// <summary>
        /// 依 UXML（VisualTreeAsset）建立並顯示面板，資源位址為 AddressPrefix + 類別名稱。
        /// 對應 uGUI 版的 ShowPanel&lt;T&gt;，但改為 new() 建立面板實例、CloneTree 建立畫面。
        /// </summary>
        public async UniTask<T> ShowPanel<T>(UILayer layer = UILayer.SCENE_UI_1, bool checkSame = true) where T : UIPanel, new()
        {
            if (checkSame && TryGetExisting<T>(out var exist))
                return exist;

            if (checkSame && pendingShowTasks.TryGetValue(typeof(T), out var pendingObj))
                return await (UniTask<T>)pendingObj;

            var task = ShowPanelCore<T>(layer);
            if (checkSame)
            {
                task = task.Preserve();
                pendingShowTasks[typeof(T)] = task;
            }

            try
            {
                return await task;
            }
            finally
            {
                if (checkSame)
                    pendingShowTasks.Remove(typeof(T));
            }
        }

        private async UniTask<T> ShowPanelCore<T>(UILayer layer) where T : UIPanel, new()
        {
            var address = GetPanelAddress(typeof(T));
            var vta = await LoadAsset<VisualTreeAsset>(address);
            if (vta == null)
                return null;

            return CreatePanel<T>(vta, layer);
        }

        /// <summary>
        /// 使用直接指定的 VisualTreeAsset 建立並顯示面板，略過 ResMgr 資源查找。
        /// 適用於 Inspector 直接指派 UXML 的情境（範例／測試場景常用）。
        /// </summary>
        public T ShowPanel<T>(VisualTreeAsset asset, UILayer layer = UILayer.SCENE_UI_1, bool checkSame = true) where T : UIPanel, new()
        {
            if (checkSame && TryGetExisting<T>(out var exist))
                return exist;

            if (asset == null)
            {
                Log.Error($"[{typeof(T)}] VisualTreeAsset 為 null");
                return null;
            }

            return CreatePanel<T>(asset, layer);
        }

        private bool TryGetExisting<T>(out T panel) where T : UIPanel
        {
            panel = GetPanel<T>();
            if (panel != null)
                BringToFront(panel);
            return panel != null;
        }

        /// <summary>
        /// 把已經開著的面板拉到同一層的最上面。
        ///
        /// 【為什麼需要】同一層之內是「後加入的畫在上面」，但重複開啟同一張面板時是
        /// 沿用既有實例、不會重新加入，於是它會停在當初的位置 ——
        /// 中間若有別的面板開在同一層，再開一次也蓋不過去。
        /// 有了這一條，「越後面開的越上層」在同層內才真的成立
        /// （跨層的順序由 UILayer 的宣告順序決定，見 AllLayerOrder）。
        ///
        /// 場景層的面板沒有初始化時 Root 會是 null，所以要防呆。
        /// </summary>
        public void BringToFront(UIPanel panel) => panel?.Root?.BringToFront();

        private T CreatePanel<T>(VisualTreeAsset asset, UILayer layer) where T : UIPanel, new()
        {
            var container = GetLayer(layer);
            if (container == null)
                return null;

            var root = asset.CloneTree();

            // VisualElement 不是 GameObject，不會出現在 Hierarchy，只能靠
            // Window > UI Toolkit > Debugger 檢視。預設 TemplateContainer 沒有名稱，
            // 在偵錯器裡是一片無名節點，因此明確命名成面板類別名稱。
            root.name = typeof(T).Name;

            // CloneTree() 回傳的 TemplateContainer 預設不佔滿版面：若唯一子元素是 position:absolute
            // （例如遮罩型面板），TemplateContainer 會因為子元素不參與版面而縮成 0 大小。
            // 一律撐滿所在層級容器，讓面板內部自行決定要滿版還是置中顯示。
            root.StretchToParentSize();
            container.Add(root);

            var panel = new T();
            panels.Add(panel);
            panel.InternalInit(root, layer);
            return panel;
        }

        public void ClosePanel(UIPanel panel)
        {
            if (panel == null)
                return;
            panels.Remove(panel);
            panel.InternalClose();
        }

        public void RemovePanel(UIPanel panel)
        {
            panels.Remove(panel);
        }
    }
}
