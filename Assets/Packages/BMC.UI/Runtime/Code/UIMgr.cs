using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BMC.Core;
using System;
using Cysharp.Threading.Tasks;

namespace BMC.UI
{
    public enum UIEvent
    {
        NONE = 0,

        INPUT_MOVE_START,
        INPUT_MOVE_FLAT,
        INPUT_MOVE_RESET,

        INPUT_TOUCH_END,
        INPUT_TOUCH_POS,

        INPUT_STICK_R,
        INPUT_STICK_R_UP,
        INPUT_STICK_R_DOWN,
        INPUT_STICK_R_LEFT,
        INPUT_STICK_R_RIGHT,

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

        AUDIO_BUTTON_CLICK,
    }
    public enum UICanvasType
    {
        /// <summary>
        /// 背景，基底
        /// </summary>
        UI_0 = 0,
        SCENE_UI_0,

        UI_1,
        SCENE_UI_1,

        UI_2,
        SCENE_UI_2,

        UI_3,
        SCENE_UI_3,
        /// <summary>
        /// Loading
        /// </summary>
        UI_4,
        SCENE_UI_4,
        /// <summary>
        /// 教學，Tip
        /// </summary>
        UI_Top,
        SCENE_UI_TOP,
        UI_Debug,
    }

    public class UIMgr : Singleton<UIMgr>
    {
        public Core.EventHandler eventHandler = new();
        public Stack<JoypadPanel> joypadPanels = new Stack<JoypadPanel>();
        public bool IsSceneInit { get; private set; }


        private Transform globalUIRoot;
        private Dictionary<UICanvasType, Transform> globalCanvas;
        private Transform sceneUIRoot;
        private Dictionary<UICanvasType, Transform> sceneCanvas;

        private List<UIPanel> panels;
        private bool isInit = false;

        private int sortingOrderDelta = 10;

        /// <summary>
        /// UI屏蔽控制
        /// </summary>
        public Action<bool> UIMaskControl;
        public List<UIPanel> uiMaskControlCount = new List<UIPanel>();

        // 用來快取匿名函式，以供正確地解註冊 (改為 Delegate 以支援不同參數)
        private Dictionary<int, Delegate> globalJoypadActions = new Dictionary<int, Delegate>();

        protected override void Init()
        {
            if (isInit)
                return;
            isInit = true;

            IsSceneInit = false;
            panels = new List<UIPanel>();

            // 統一在這裡註冊事件轉發到最頂層的 JoypadPanel
            RegisterGlobalJoypadEvents();
        }

        private void RegisterGlobalJoypadEvents()
        {
            // 防護機制：若已經註冊過，就不重複註冊
            if (globalJoypadActions.Count > 0) return;

            globalJoypadActions[(int)UIEvent.INPUT_UP] = new Action(() => TopPanelAction(p => p.OnInputUp()));
            globalJoypadActions[(int)UIEvent.INPUT_DOWN] = new Action(() => TopPanelAction(p => p.OnInputDown()));
            globalJoypadActions[(int)UIEvent.INPUT_LEFT] = new Action(() => TopPanelAction(p => p.OnInputLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_RIGHT] = new Action(() => TopPanelAction(p => p.OnInputRight()));

            globalJoypadActions[(int)UIEvent.INPUT_A] = new Action(() => TopPanelAction(p => p.OnInputA()));

            // B鍵原本有綁定 closeJoypadPanel，現在改成先觸發面板的 OnInputB，
            // 也可以保留預設關閉行為，或是讓面板自己決定要不要關閉。
            // 這裡保留預設行為：如果面板沒有攔截 B 鍵，就關閉它。
            globalJoypadActions[(int)UIEvent.INPUT_B] = new Action(() => {
                var top = GetTopJoypadPanel();
                if (top != null)
                {
                    top.OnInputB();
                    closeJoypadPanel(); // 預設的 B 鍵關閉行為
                }
            });

            globalJoypadActions[(int)UIEvent.INPUT_X] = new Action(() => TopPanelAction(p => p.OnInputX()));
            globalJoypadActions[(int)UIEvent.INPUT_Y] = new Action(() => TopPanelAction(p => p.OnInputY()));

            globalJoypadActions[(int)UIEvent.INPUT_SHOULDER_L] = new Action(() => TopPanelAction(p => p.OnInputShoulderLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_SHOULDER_R] = new Action(() => TopPanelAction(p => p.OnInputShoulderRight()));
            globalJoypadActions[(int)UIEvent.INPUT_TRIGGER_L] = new Action(() => TopPanelAction(p => p.OnInputTriggerLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_TRIGGER_R] = new Action(() => TopPanelAction(p => p.OnInputTriggerRight()));

            globalJoypadActions[(int)UIEvent.INPUT_START] = new Action(() => TopPanelAction(p => p.OnInputStart()));
            globalJoypadActions[(int)UIEvent.INPUT_SELECT] = new Action(() => TopPanelAction(p => p.OnInputSystemSelect()));

            // 修改這行：INPUT_STICK_R 改為接收 Vector2 參數
            globalJoypadActions[(int)UIEvent.INPUT_STICK_R] = new Action<Vector2>((v) => TopPanelAction(p => p.OnInputStickR(v)));

            globalJoypadActions[(int)UIEvent.INPUT_STICK_R_UP] = new Action(() => TopPanelAction(p => p.OnInputStickRUp()));
            globalJoypadActions[(int)UIEvent.INPUT_STICK_R_DOWN] = new Action(() => TopPanelAction(p => p.OnInputStickRDown()));
            globalJoypadActions[(int)UIEvent.INPUT_STICK_R_LEFT] = new Action(() => TopPanelAction(p => p.OnInputStickRLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_STICK_R_RIGHT] = new Action(() => TopPanelAction(p => p.OnInputStickRRight()));

            foreach (var kvp in globalJoypadActions)
            {
                if (kvp.Value is Action act)
                    eventHandler.Register(kvp.Key, act);
                else if (kvp.Value is Action<Vector2> actV2)
                    eventHandler.Register(kvp.Key, actV2);
            }
        }

        private void UnregisterGlobalJoypadEvents()
        {
            foreach (var kvp in globalJoypadActions)
            {
                if (kvp.Value is Action act)
                    eventHandler.UnRegister(kvp.Key, act);
                else if (kvp.Value is Action<Vector2> actV2)
                    eventHandler.UnRegister(kvp.Key, actV2);
            }
            globalJoypadActions.Clear();
        }

        private void OnDestroy()
        {
            UnregisterGlobalJoypadEvents();
        }

        // 取得目前最上層的 JoypadPanel
        private JoypadPanel GetTopJoypadPanel()
        {
            if (joypadPanels.Count > 0)
                return joypadPanels.Peek();
            return null;
        }

        // 輔助函式：只對最上層的 JoypadPanel 執行動作
        private void TopPanelAction(Action<JoypadPanel> action)
        {
            var topPanel = GetTopJoypadPanel();
            if (topPanel != null)
            {
                action?.Invoke(topPanel);
            }
        }

        public void PushJoypadPanel(JoypadPanel panel)
        {
            joypadPanels.Push(panel);
        }

        public async UniTask LoadGlobalCanvas()
        {
            globalUIRoot = new GameObject("Global_UIRoot").transform;
            GameObject.DontDestroyOnLoad(globalUIRoot);
            globalCanvas = new()
            {
                { UICanvasType.UI_0, await LoadCanvas(UICanvasType.UI_0, globalUIRoot) },
                { UICanvasType.UI_1, await LoadCanvas(UICanvasType.UI_1, globalUIRoot) },
                { UICanvasType.UI_2, await LoadCanvas(UICanvasType.UI_2, globalUIRoot) },
                { UICanvasType.UI_3, await LoadCanvas(UICanvasType.UI_3, globalUIRoot) },
                { UICanvasType.UI_4, await LoadCanvas(UICanvasType.UI_4, globalUIRoot) },
                { UICanvasType.UI_Top, await LoadCanvas(UICanvasType.UI_Top, globalUIRoot) },
                { UICanvasType.UI_Debug, await LoadCanvas(UICanvasType.UI_Debug, globalUIRoot) }
            };
        }
        private async UniTask<Transform> LoadCanvas(UICanvasType uICanvasType, Transform root)
        {
            string ASSET_UI_CANVAS = "Canvas";
            var go = await ResMgr.Instance.LoadAssetAsync<GameObject>(ASSET_UI_CANVAS, true, root);
            go.name = $"Canvas_{uICanvasType}";
            go.GetComponent<Canvas>().sortingOrder = ((int)uICanvasType) * sortingOrderDelta;
            var child = go.transform.GetChild(0);
            StretchToSafeArea((RectTransform)child);
            return go.transform;
        }

        public void RemovePanel(UIPanel panel)
        {
            Log.Info($"[RemovePanel][JoypadPanel: {joypadPanels.Count}] {panel}");
            uiMaskControlCount.Remove(panel);
            if (uiMaskControlCount.Count == 0)
                UIMaskControl?.Invoke(false);

            if (joypadPanels.Count > 0 && joypadPanels.Peek() == panel)
                joypadPanels.Pop();
            panels.Remove(panel);
        }

        public void Reset()
        {
            UnregisterGlobalJoypadEvents();

            globalCanvas = null;
            panels = null;
            ResetSceneUIRoot();
            if (globalUIRoot != null)
            {
                GameObject.Destroy(globalUIRoot.gameObject);
                globalUIRoot = null;
            }
            isInit = false;
            Init();
        }

        public void ResetSceneUIRoot()
        {
            joypadPanels = new();
            sceneCanvas = new();
            if (sceneUIRoot != null)
            {
                GameObject.Destroy(sceneUIRoot.gameObject);
                sceneUIRoot = null;
            }
            IsSceneInit = false;
        }

        private Transform getCanvas(UICanvasType uICanvasType)
        {
            switch (uICanvasType)
            {
                case UICanvasType.UI_0:
                case UICanvasType.UI_1:
                case UICanvasType.UI_2:
                case UICanvasType.UI_3:
                case UICanvasType.UI_4:
                case UICanvasType.UI_Top:
                case UICanvasType.UI_Debug:
                    return globalCanvas[uICanvasType];
                case UICanvasType.SCENE_UI_0:
                case UICanvasType.SCENE_UI_1:
                case UICanvasType.SCENE_UI_2:
                case UICanvasType.SCENE_UI_3:
                case UICanvasType.SCENE_UI_4:
                case UICanvasType.SCENE_UI_TOP:
                    if (!IsSceneInit)
                        return null;
                    return sceneCanvas[uICanvasType];
                default:
                    Log.Error($"[UICanvasType] not found {uICanvasType}");
                    return null;
            }
        }

        public bool IsTopPanel(UIPanel panel)
        {
            if (joypadPanels.Count == 0)
                return false;
            return joypadPanels.Peek() == panel;
        }

        public async UniTask<UIPanel> ShowPanel(Type type, UICanvasType uICanvasType = UICanvasType.SCENE_UI_1, bool checkSame = true)
        {
            // 1. 抓取泛型方法 ShowPanel<T>
            var method = typeof(UIMgr).GetMethod(nameof(ShowPanel), new Type[] { typeof(UICanvasType), typeof(bool) });
            var genericMethod = method.MakeGenericMethod(type);

            // 2. 執行 Invoke，此時回傳的是 object (實質為 UniTask<T>)
            var taskObj = genericMethod.Invoke(this, new object[] { uICanvasType, checkSame });

            // 3. 【關鍵】因為 UniTask<T> 沒繼承 UniTask，必須透過反射呼叫 AsUniTask() 轉型
            var asUniTaskMethod = taskObj.GetType().GetMethod("AsUniTask");
            var uniTask = (UniTask)asUniTaskMethod.Invoke(taskObj, null);

            // 4. 等待完成
            await uniTask;

            // 5. 從已載入清單回傳對應類型的實例
            return panels.Find(p => p.GetType() == type);
        }

        public async UniTask<T> ShowPanel<T>(UICanvasType uICanvasType = UICanvasType.SCENE_UI_1, bool checkSame = true) where T : UIPanel
        {
            if (checkSame)
            {
                var p = GetPanel<T>();
                if (p != null)
                    return p;
            }
            var go = await ResMgr.Instance.LoadAssetAsync<GameObject>(typeof(T).Name, true, getCanvas(uICanvasType));
            if (go == null)
            {
                Log.Error($"[{typeof(T)}] load error");
                return null;
            }
            var panel = go.GetComponent<T>();
            if (panel.maskControl)
            {
                uiMaskControlCount.Add(panel);
            }
            if (uiMaskControlCount.Count == 1)
            {
                UIMaskControl?.Invoke(true);
            }
            panels.Add(panel);
            panel.Init(uICanvasType);
            return panel as T;
        }

        public T GetPanel<T>() where T : UIPanel
        {
            foreach (var p in panels)
            {
                if (p is T)
                    return p as T;
            }
            return null;
        }

        public void closeJoypadPanel()
        {
            if (joypadPanels.Count == 0)
            {
                // 離開遊戲
                //showMsg("EXIT GAME", "HINT", () => { Application.Quit(); });
                Log.Info("[closeJoypadPanel] no panel can close");
                return;
            }
            if (!joypadPanels.Peek().canBackClose)
            {
                Log.Info($"[{joypadPanels.Peek()}] can not back close");
                return;
            }
            var panel = joypadPanels.Pop();
            closePanel(panel);
        }

        public void closePanel(UIPanel panel, bool anima = true, Action callback = null)
        {
            if (anima)
            {
                panel.ClosePanel(callback);
            }
            else
            {
                RemovePanel(panel);
                panel.close();
                if (panel.gameObject != null)
                    GameObject.Destroy(panel.gameObject);
                callback?.Invoke();
            }
        }

        public async UniTask CreateSceneUIRoot(string sceneName)
        {
            sceneUIRoot = new GameObject($"{sceneName}_UIRoot").transform;
            sceneCanvas = new()
            {
                { UICanvasType.SCENE_UI_0, await LoadCanvas(UICanvasType.SCENE_UI_0, sceneUIRoot) },
                { UICanvasType.SCENE_UI_1, await LoadCanvas(UICanvasType.SCENE_UI_1, sceneUIRoot) },
                { UICanvasType.SCENE_UI_2, await LoadCanvas(UICanvasType.SCENE_UI_2, sceneUIRoot) },
                { UICanvasType.SCENE_UI_3, await LoadCanvas(UICanvasType.SCENE_UI_3, sceneUIRoot) },
                { UICanvasType.SCENE_UI_4, await LoadCanvas(UICanvasType.SCENE_UI_4, sceneUIRoot) },
                { UICanvasType.SCENE_UI_TOP, await LoadCanvas(UICanvasType.SCENE_UI_TOP, sceneUIRoot) }
            };
            IsSceneInit = true;
        }

        public void StretchToSafeArea(RectTransform rectTransform, bool forceUpdate = false)
        {
            //Log.Info($"[StretchToSafeArea][{Screen.safeArea}][{Screen.width}:{Screen.height}]");
            Rect safeRect = Screen.safeArea;

            // Convert safe area rectangle from absolute pixels to normalized anchor coordinates
            var anchorMin = safeRect.position;
            var anchorMax = safeRect.position + safeRect.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;

            if (forceUpdate)
            {
                rectTransform.ForceUpdateRectTransforms();
            }
        }

        public void StretchToScreenArea(RectTransform rectTransform, bool forceUpdate = false)
        {
            var anchorMin = Vector2.zero;
            var anchorMax = Vector2.one;
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;

            if (forceUpdate)
            {
                rectTransform.ForceUpdateRectTransforms();
            }
        }
    }
}