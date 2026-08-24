using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版開關群組，對應 uGUI 版 BMC.UI 的 UIToggleGroup。
    /// 掛在群組底下的 <see cref="UIToggle"/> 會變成單選：打開其中一顆就關掉其餘。
    /// </summary>
    [UxmlElement]
    public partial class UIToggleGroup : VisualElement
    {
        /// <summary>是否允許全部關閉。false 時至少會保持一顆開著。</summary>
        [UxmlAttribute]
        public bool allowSwitchOff { get; set; }

        private readonly List<UIToggle> toggles = new();
        private readonly Dictionary<UIToggle, Action<bool>> listenerMap = new();

        public UIToggleGroup()
        {
            AddToClassList("bmc-toggle-group");
            RegisterCallback<AttachToPanelEvent>(_ => RegisterAll());
            RegisterCallback<DetachFromPanelEvent>(_ => UnregisterAll());
        }

        private void RegisterAll()
        {
            UnregisterAll();
            toggles.Clear();

            this.Query<UIToggle>().ForEach(toggle =>
            {
                // 只收「最近的祖先群組是自己」的開關，避免嵌套群組互相搶人
                if (toggle.GetFirstAncestorOfType<UIToggleGroup>() == this)
                    RegisterToggle(toggle);
            });

            EnsureSingleActive();
        }

        private void UnregisterAll()
        {
            var current = new List<UIToggle>(listenerMap.Keys);
            foreach (var toggle in current)
                UnregisterToggle(toggle);
        }

        /// <summary>動態把指定開關加入群組。</summary>
        public void RegisterToggle(UIToggle toggle)
        {
            if (toggle == null)
                return;

            if (!toggles.Contains(toggle))
                toggles.Add(toggle);

            UnregisterToggle(toggle);

            Action<bool> handler = isOn => OnToggleStateChanged(toggle, isOn);
            listenerMap[toggle] = handler;
            toggle.OnValueChanged += handler;
        }

        /// <summary>把指定開關從群組移除。</summary>
        public void UnregisterToggle(UIToggle toggle)
        {
            if (toggle == null)
                return;

            if (listenerMap.TryGetValue(toggle, out var handler))
            {
                toggle.OnValueChanged -= handler;
                listenerMap.Remove(toggle);
            }
        }

        private void OnToggleStateChanged(UIToggle changedToggle, bool isOn)
        {
            if (isOn)
            {
                foreach (var toggle in toggles)
                {
                    if (toggle != null && toggle != changedToggle)
                        toggle.Set(false);
                }
                return;
            }

            if (!allowSwitchOff && !HasAnyActiveToggle())
                changedToggle.Set(true);
        }

        private bool HasAnyActiveToggle()
        {
            foreach (var toggle in toggles)
            {
                if (toggle != null && toggle.IsOn)
                    return true;
            }
            return false;
        }

        private void EnsureSingleActive()
        {
            UIToggle active = null;

            foreach (var toggle in toggles)
            {
                if (toggle == null)
                    continue;

                if (!toggle.IsOn)
                    continue;

                if (active == null)
                {
                    active = toggle;
                    continue;
                }

                toggle.Set(false);
            }

            if (active == null && !allowSwitchOff && toggles.Count > 0)
                toggles[0]?.Set(true);
        }

        /// <summary>目前開啟的那一顆；全部關閉時回傳 null。</summary>
        public UIToggle GetActiveToggle()
        {
            foreach (var toggle in toggles)
            {
                if (toggle != null && toggle.IsOn)
                    return toggle;
            }
            return null;
        }
    }
}
