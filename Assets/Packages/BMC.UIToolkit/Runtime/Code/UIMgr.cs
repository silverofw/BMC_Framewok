using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using BMC.Core;

namespace BMC.UIToolkit
{
    public enum UIEvent
    {
        NONE = 0,

        AUDIO_BUTTON_CLICK,
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

    public class UIMgr : Singleton<UIMgr>
    {
        private static readonly UILayer[] GlobalLayerOrder =
        {
            UILayer.UI_0, UILayer.UI_1, UILayer.UI_2, UILayer.UI_3, UILayer.UI_4, UILayer.UI_Top, UILayer.UI_Debug,
        };

        private static readonly UILayer[] SceneLayerOrder =
        {
            UILayer.SCENE_UI_0, UILayer.SCENE_UI_1, UILayer.SCENE_UI_2, UILayer.SCENE_UI_3, UILayer.SCENE_UI_4, UILayer.SCENE_UI_TOP,
        };

        private const string ASSET_UI_ROOT = "UIDocumentRoot";

        public Core.EventHandler eventHandler = new();

        private GameObject rootGo;
        private UIDocument rootDocument;
        private Dictionary<UILayer, VisualElement> globalLayers;

        private VisualElement sceneRoot;
        private Dictionary<UILayer, VisualElement> sceneLayers;
        public bool IsSceneInit { get; private set; }

        private List<UIPanel> panels;

        protected override void Init()
        {
            panels = new List<UIPanel>();
            IsSceneInit = false;
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
                Log.Error($"[UIMgr] {ASSET_UI_ROOT} 缺少 UIDocument 元件");
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

            globalLayers = BuildLayers(rootDocument.rootVisualElement, GlobalLayerOrder);
        }

        /// <summary>
        /// 建立場景專用分層容器，掛載於全域 UI_1 之上。對應 uGUI 版的 CreateSceneUIRoot。
        /// </summary>
        public void CreateSceneRoot()
        {
            ResetSceneRoot();

            sceneRoot = new VisualElement { name = "Scene_UIRoot" };
            sceneRoot.StretchToParentSize();
            sceneRoot.pickingMode = PickingMode.Ignore;
            globalLayers[UILayer.UI_1].Add(sceneRoot);

            sceneLayers = BuildLayers(sceneRoot, SceneLayerOrder);
            IsSceneInit = true;
        }

        public void ResetSceneRoot()
        {
            panels.RemoveAll(p => IsSceneLayer(p.Layer));

            sceneRoot?.RemoveFromHierarchy();
            sceneRoot = null;
            sceneLayers = null;
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
            if (IsSceneLayer(layer))
            {
                if (!IsSceneInit)
                {
                    Log.Error($"[UILayer] 場景尚未初始化: {layer}");
                    return null;
                }
                return sceneLayers[layer];
            }
            return globalLayers[layer];
        }

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
        /// 依 UXML（VisualTreeAsset，資源名稱與面板類別同名）建立並顯示面板。
        /// 對應 uGUI 版的 ShowPanel&lt;T&gt;，但改為 new() 建立面板實例、CloneTree 建立畫面。
        /// </summary>
        public async UniTask<T> ShowPanel<T>(UILayer layer = UILayer.SCENE_UI_1, bool checkSame = true) where T : UIPanel, new()
        {
            if (checkSame && TryGetExisting<T>(out var exist))
                return exist;

            var vta = await ResMgr.Instance.LoadAssetAsync<VisualTreeAsset>(typeof(T).Name, false);
            if (vta == null)
            {
                Log.Error($"[{typeof(T)}] load error");
                return null;
            }

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
            return panel != null;
        }

        private T CreatePanel<T>(VisualTreeAsset asset, UILayer layer) where T : UIPanel, new()
        {
            var container = GetLayer(layer);
            if (container == null)
                return null;

            var root = asset.CloneTree();
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
