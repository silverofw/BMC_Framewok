using System;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版開關，對應 uGUI 版 BMC.UI 的 UIToggle。
    ///
    /// 點擊切換 <see cref="IsOn"/>，並以 USS class <c>bmc-toggle--on</c> 表達狀態。
    /// uGUI 版用兩個 GameObject 清單分別表示開／關外觀；
    /// 這裡改查子元素上的 <c>bmc-toggle-on</c>／<c>bmc-toggle-off</c> class。
    /// 沒有自訂子元素時會自動放一顆勾選框。
    /// </summary>
    [UxmlElement]
    public partial class UIToggle : UIButton
    {
        public const string OnClass = "bmc-toggle--on";
        public const string OnVisualClass = "bmc-toggle-on";
        public const string OffVisualClass = "bmc-toggle-off";

        public event Action<bool> OnValueChanged;

        private bool isOnValue = true;
        private VisualElement defaultBox;

        /// <summary>目前是否開啟。寫入時等同呼叫 <see cref="Set"/>。</summary>
        [UxmlAttribute]
        public bool isOn
        {
            get => isOnValue;
            set => Set(value);
        }

        public bool IsOn => isOnValue;

        public UIToggle()
        {
            AddToClassList("bmc-toggle");

            defaultBox = new VisualElement { name = "toggle-box", pickingMode = PickingMode.Ignore };
            defaultBox.AddToClassList("bmc-toggle__box");

            var mark = new Label("✓") { name = "toggle-mark", pickingMode = PickingMode.Ignore };
            mark.AddToClassList("bmc-toggle__mark");
            defaultBox.Add(mark);
            Insert(0, defaultBox);

            OnClick += () => Toggle();

            // UXML 子元素在建構子之後才掛上來，進畫面時再刷一次開／關外觀
            RegisterCallback<AttachToPanelEvent>(_ => UpdateVisuals());
            UpdateVisuals();
        }

        /// <summary>直接設成開或關。狀態沒變就不會再廣播。</summary>
        public void Set(bool state)
        {
            if (isOnValue == state)
                return;

            isOnValue = state;
            UpdateVisuals();
            OnValueChanged?.Invoke(isOnValue);
        }

        /// <summary>反轉目前狀態。</summary>
        public void Toggle() => Set(!isOnValue);

        /// <summary>清空所有訂閱，對應 uGUI 版的 ClearAllListeners。</summary>
        public void ClearAllListeners() => OnValueChanged = null;

        private void UpdateVisuals()
        {
            EnableInClassList(OnClass, isOnValue);

            var mark = defaultBox?.Q<Label>("toggle-mark");
            if (mark != null)
                mark.style.visibility = isOnValue ? Visibility.Visible : Visibility.Hidden;

            // 自訂開／關外觀：專案可在 UXML 裡放帶這些 class 的子元素，
            // 沒放的話只靠預設勾選框與 --on class。
            foreach (var e in this.Query<VisualElement>(className: OnVisualClass).ToList())
            {
                if (e == this)
                    continue;
                e.style.display = isOnValue ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (var e in this.Query<VisualElement>(className: OffVisualClass).ToList())
            {
                if (e == this)
                    continue;
                e.style.display = isOnValue ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }
    }
}
