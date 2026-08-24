using System;
using System.Collections.Generic;
using BMC.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UITMgr 的手把輸入轉發，對應 uGUI 版 BMC.UI.UIMgr 的 RegisterGlobalJoypadEvents 一段。
    ///
    /// 這裡刻意不相依 InputSystem：UITMgr 只負責把 eventHandler 收到的
    /// UIEvent.INPUT_* 轉給最上層的 JoypadPanel，實際去讀手把的程式碼放在
    /// 可選組件 BMC.UIToolkit.Joypad（Runtime/Joypad）。專案若已有自己的輸入層，
    /// 只要對這個 eventHandler 送出同樣的事件即可，不必用附的那一份。
    /// </summary>
    public partial class UITMgr
    {
        /// <summary>
        /// 用 List 當堆疊使用：最後推入的是最上層，也是唯一會收到輸入的面板。
        /// </summary>
        private readonly List<JoypadPanel> joypadPanels = new();

        private readonly Dictionary<int, Delegate> globalJoypadActions = new();

        private void RegisterGlobalJoypadEvents()
        {
            if (globalJoypadActions.Count > 0) return;

            globalJoypadActions[(int)UIEvent.INPUT_UP] = new Action(() => TopPanelAction(p => p.OnInputUp()));
            globalJoypadActions[(int)UIEvent.INPUT_DOWN] = new Action(() => TopPanelAction(p => p.OnInputDown()));
            globalJoypadActions[(int)UIEvent.INPUT_LEFT] = new Action(() => TopPanelAction(p => p.OnInputLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_RIGHT] = new Action(() => TopPanelAction(p => p.OnInputRight()));

            globalJoypadActions[(int)UIEvent.INPUT_A] = new Action(() => TopPanelAction(p => p.OnInputA()));

            // B 鍵：先讓面板自己處理，再套用預設的返回關閉行為
            // （面板可用 canBackClose 擋掉，語意與 uGUI 版一致）。
            globalJoypadActions[(int)UIEvent.INPUT_B] = new Action(() =>
            {
                var top = GetTopJoypadPanel();
                if (top == null || IsJoypadInputBlocked(top))
                    return;

                top.OnInputB();

                // 面板可能已經在 OnInputB 裡自己關掉了（例如 MsgPanel 的取消），
                // 這時再呼叫一次就會把下一層面板也關掉。
                if (top.IsClosed || GetTopJoypadPanel() != top)
                    return;

                CloseJoypadPanel();
            });

            globalJoypadActions[(int)UIEvent.INPUT_X] = new Action(() => TopPanelAction(p => p.OnInputX()));
            globalJoypadActions[(int)UIEvent.INPUT_Y] = new Action(() => TopPanelAction(p => p.OnInputY()));

            globalJoypadActions[(int)UIEvent.INPUT_SHOULDER_L] = new Action(() => TopPanelAction(p => p.OnInputShoulderLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_SHOULDER_R] = new Action(() => TopPanelAction(p => p.OnInputShoulderRight()));
            globalJoypadActions[(int)UIEvent.INPUT_TRIGGER_L] = new Action(() => TopPanelAction(p => p.OnInputTriggerLeft()));
            globalJoypadActions[(int)UIEvent.INPUT_TRIGGER_R] = new Action(() => TopPanelAction(p => p.OnInputTriggerRight()));

            globalJoypadActions[(int)UIEvent.INPUT_START] = new Action(() => TopPanelAction(p => p.OnInputStart()));
            globalJoypadActions[(int)UIEvent.INPUT_SELECT] = new Action(() => TopPanelAction(p => p.OnInputSystemSelect()));

            globalJoypadActions[(int)UIEvent.INPUT_STICK_R] = new Action<Vector2>(v => TopPanelAction(p => p.OnInputStickR(v)));

            foreach (var kvp in globalJoypadActions)
            {
                // EventHandler 只有具型別的多載，Delegate 要先拆回實際型別
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

        private JoypadPanel GetTopJoypadPanel()
        {
            if (joypadPanels.Count > 0)
                return joypadPanels[joypadPanels.Count - 1];
            return null;
        }

        private void TopPanelAction(Action<JoypadPanel> action)
        {
            var top = GetTopJoypadPanel();
            if (top == null || IsJoypadInputBlocked(top))
                return;

            action?.Invoke(top);
        }

        /// <summary>
        /// 手把面板上面是否壓著別的遮罩型面板。
        ///
        /// 手把面板不見得是最上層：從除錯面板叫出 MsgPanel 之後，除錯面板仍是
        /// 堆疊裡唯一的 JoypadPanel，若不擋下來，按 A 會穿過彈窗執行背後的按鈕。
        /// 判斷依據是 maskControl——會壓住整個畫面的面板才算，
        /// Toast 這種不擋操作的提示（maskControl = false）不影響輸入。
        /// </summary>
        private bool IsJoypadInputBlocked(JoypadPanel top)
        {
            int topIndex = panels.IndexOf(top);
            if (topIndex < 0)
                return false;

            for (int i = topIndex + 1; i < panels.Count; i++)
            {
                if (panels[i].maskControl)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 推入手把堆疊。由 UIPanel.InternalInit 自動呼叫，面板不需自行處理。
        /// </summary>
        internal void PushJoypadPanel(JoypadPanel panel)
        {
            if (panel == null || joypadPanels.Contains(panel))
                return;
            joypadPanels.Add(panel);
        }

        /// <summary>
        /// 移出手把堆疊。由 UIPanel.InternalClose 自動呼叫。
        /// </summary>
        internal void RemoveJoypadPanel(JoypadPanel panel)
        {
            if (panel == null)
                return;
            joypadPanels.Remove(panel);
        }

        public bool IsTopJoypadPanel(JoypadPanel panel) => GetTopJoypadPanel() == panel;

        /// <summary>
        /// 目前是否有手把面板在等輸入。輸入層可以先問這個再去讀裝置，
        /// 專案只用 uGUI 那一套時就不必每幀白跑一次。
        /// </summary>
        public bool HasJoypadPanel => joypadPanels.Count > 0;

        /// <summary>
        /// 關閉最上層的手把面板（返回鍵行為），對應 uGUI 版的 closeJoypadPanel。
        /// 實際移出堆疊的動作在面板關閉流程裡完成，這裡不先行 Remove，
        /// 否則關閉動畫還沒播完就會讓輸入落到下一層面板上。
        /// </summary>
        public void CloseJoypadPanel()
        {
            var top = GetTopJoypadPanel();
            if (top == null)
            {
                Log.Info("[CloseJoypadPanel] no panel can close");
                return;
            }

            if (!top.canBackClose)
            {
                Log.Info($"[{top.GetType().Name}] can not back close");
                return;
            }

            top.ClosePanel();
        }

        /// <summary>
        /// 目前焦點是否落在文字輸入元件上。
        ///
        /// 輸入來源是全域輪詢的，玩家在聊天室之類的輸入框打字時，
        /// 方向鍵與 Enter 不該同時被當成手把操作送給面板。
        /// </summary>
        public bool IsTextInputFocused()
        {
            var focused = rootDocument?.rootVisualElement?.focusController?.focusedElement as VisualElement;
            if (focused == null)
                return false;

            // TextField 實際取得焦點的是內部的 TextElement，
            // 因此往上找有沒有 TextField 祖先，而不是只看焦點元素本身。
            if (focused is TextField)
                return true;

            return focused.GetFirstAncestorOfType<TextField>() != null;
        }
    }
}
