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
        // 用 List 當堆疊使用（而非 Stack<T>）：Stack.Pop/Peek 只認「最上層」，
        // 一旦面板因為動畫時間差或巢狀 MsgPanel 而不是照 LIFO 順序關閉，
        // RemovePanel 就會判斷「不是最上層」而跳過移除，導致殘影面板卡在堆疊裡。
        // 改用 List + Remove(panel) 讓面板無論在堆疊哪個位置關閉，都能正確被清除。
        public List<JoypadPanel> joypadPanels = new List<JoypadPanel>();
        public bool IsSceneInit { get; private set; }

        /// <summary>
        /// 全域 Canvas 是否已就緒。LoadGlobalCanvas 要透過 ResMgr 載入 Canvas 資源，
        /// 所以在補丁流程跑完之前一直是 false —— 那段期間任何 ShowPanel 都會空引用。
        /// </summary>
        public bool IsGlobalCanvasReady => globalCanvas != null;


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

        /// <summary>
        /// 最上層 JoypadPanel 是在哪一幀開的，沒有的話回 -1。
        ///
        /// 【給誰用】專案同時跑 uGUI 與 UI Toolkit 兩套時，需要判斷「這顆按鍵該給哪一邊」。
        /// 判斷依據必須是「誰在吃手把輸入」也就是 JoypadPanel，而不是「誰有遮罩」——
        /// 有遮罩不代表會吃輸入（例如純粹擋住玩家亂點的空殼面板），拿 maskControl 當
        /// 依據會讓那種面板攔下不屬於它的按鍵。
        /// UI Toolkit 版可以從 OpenPanels 自行算出同樣的值。
        /// </summary>
        public int TopJoypadOpenFrame => GetTopJoypadPanel()?.OpenFrame ?? -1;

        // 取得目前最上層的 JoypadPanel
        private JoypadPanel GetTopJoypadPanel()
        {
            if (joypadPanels.Count > 0)
                return joypadPanels[joypadPanels.Count - 1];
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
            joypadPanels.Add(panel);
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

            if (panel is JoypadPanel joypadPanel)
                joypadPanels.Remove(joypadPanel);
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
            // 【場景面板要先從遮罩計數裡拿掉】下面的 Destroy(sceneUIRoot) 只是把物件毀掉，
            // uiMaskControlCount 裡的參考不會自己消失；而 Destroy 是延後到當幀結尾才生效，
            // 所以當下也不能靠 p == null 篩。必須趁還掛在場景根節點底下時先移除。
            //
            // 不清的話 Count 會永遠停在 > 0，之後任何「讀這個計數來決定要不要放開輸入」的
            // 地方都會拿到 true。實際踩過：UI Toolkit 的面板在關閉時會用
            // uiMaskControlCount.Count > 0 回推遊戲層的輸入遮罩旗標，換場景之後那個旗標
            // 就再也回不去 false，手把按鍵整個失效。
            //
            // 這裡刻意不呼叫 UIMaskControl?.Invoke(false)：換場景流程可能剛剛才刻意關掉
            // 輸入(見呼叫端的 CloseInput)，在這裡放開會讓玩家在清場中途又能操作。
            // 只把計數修正成真實值，要不要放開由呼叫端決定。
            if (sceneUIRoot != null)
                uiMaskControlCount.RemoveAll(p => p == null || p.transform.IsChildOf(sceneUIRoot));

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
            return joypadPanels[joypadPanels.Count - 1] == panel;
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
            var topPanel = joypadPanels[joypadPanels.Count - 1];
            if (!topPanel.canBackClose)
            {
                Log.Info($"[{topPanel}] can not back close");
                return;
            }
            joypadPanels.RemoveAt(joypadPanels.Count - 1);
            closePanel(topPanel);
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