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

        // 之前不可改順序
        /// <summary>
        /// 
        /// </summary>
        UI_MASK_CONTROL = 10000,

        UI_ROGUE_ADD_CHESS,
        UI_UPDATE_BATTLE_TEAM_FIELD,
        UI_HIDE_NORMAL_PANEL,
        UI_SHOW_NORMAL_PANEL,

        MAP_CLICK_POS,
        MAP_CHECK_POS,

        STORY_START,
        STORY_FINISH,

        ATOM_VIEW_SHOW,
        ATOM_VIEW_HIDE,
        ATOM_VIEW_REBUILD,
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
        public BMC.Core.EventHandler eventHandler = new();
        public Stack<JoypadPanel> joypadPanels = new Stack<JoypadPanel>();


        private Transform globalUIRoot;
        private Dictionary<UICanvasType, Transform> globalCanvas;
        private Transform sceneUIRoot;
        private Dictionary<UICanvasType, Transform> sceneCanvas;

        private List<UIPanel> panels;
        private bool isInit = false;
        private bool isSceneInit = false;

        private int sortingOrderDelta = 10;

        /// <summary>
        /// UI屏蔽控制
        /// </summary>
        public Action<bool> UIMaskControl;
        public int uiMaskControlCount = 0;

        protected override void Init()
        {
            if (isInit)
                return;
            isInit = true;

            isSceneInit = false;
            panels = new List<UIPanel>();

            eventHandler.Register((int)UIEvent.INPUT_B, closeJoypadPanel);
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
            panels.Remove(panel);
        }

        public void Reset()
        {
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
            isSceneInit = false;
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
                    if (!isSceneInit)
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
            var panel = go.GetComponent<UIPanel>();
            uiMaskControlCount += panel.maskControl ? 1 : 0;
            if (uiMaskControlCount == 1)
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
                uiMaskControlCount -= panel.maskControl ? 1 : 0;                
                if (uiMaskControlCount == 0)
                {
                    UIMaskControl?.Invoke(false);
                }
                if (uiMaskControlCount < 0)
                    Log.Error($"[ERROR] {uiMaskControlCount}");
                if (joypadPanels.Count > 0 && joypadPanels.Peek() == panel)
                    joypadPanels.Pop();
                panel.close();
                panels.Remove(panel);
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
            isSceneInit = true;
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
