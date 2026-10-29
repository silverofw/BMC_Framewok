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

        /// <summary>
        /// 是否正在執行「不准全部關閉」的補救(把唯一被關掉的那顆硬撥回 true)。
        ///
        /// 這個補救會跟「在 OnValueChanged 裡把同一顆撥成 false」的消費端互相硬撥：
        /// 消費端 Set(false) → 這裡 Set(true) → 消費端又 Set(false) → …。
        /// <see cref="UIToggle.Set"/> 的「狀態沒變就不廣播」早退完全擋不住，因為每一跳
        /// 狀態都真的在變，結果是 StackOverflowException —— 那是 catch 不到的，整個行程
        /// 直接死。所以這個補救只做一層：已經在補救中就放手，讓狀態停在「全部關閉」。
        /// 那違反 allowSwitchOff=false，但有界、不崩，消費端也還有機會自己選回來。
        ///
        /// 刻意只守這一條路徑，不守整個 <see cref="OnToggleStateChanged"/>：上面 isOn 那條
        /// 「開一顆就關掉其餘」是正常用法本來就會遞迴進來的路(消費端在 handler 裡改選別顆)，
        /// 守了會變成多顆同時亮著 —— 那是把崩潰換成安靜的錯狀態，更糟。
        /// </summary>
        private bool isEnforcingMinimumSelection;

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

            if (allowSwitchOff || isEnforcingMinimumSelection || HasAnyActiveToggle())
                return;

            isEnforcingMinimumSelection = true;
            try
            {
                changedToggle.Set(true);
            }
            finally
            {
                isEnforcingMinimumSelection = false;
            }
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
