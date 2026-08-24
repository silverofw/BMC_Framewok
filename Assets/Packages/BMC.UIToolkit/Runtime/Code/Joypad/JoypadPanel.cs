using BMC.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版手把面板基底，對應 uGUI 版 BMC.UI 的 JoypadPanel。
    ///
    /// 輸入來源與 uGUI 版一致：由外部輸入層把 UIEvent.INPUT_* 送進
    /// UITMgr.Instance.eventHandler，UITMgr 再轉發給最上層的 JoypadPanel
    /// （實作見 UITMgr.Joypad.cs）。本套件附了可選組件 BMC.UIToolkit.Joypad
    /// 當預設輸入來源，專案已有自己的輸入層時可以整個資料夾刪掉。
    ///
    /// 面板被 UITMgr 建立時會自動推入手把堆疊、關閉時自動移除，
    /// 子類別不需要（也不應該）自己處理進出堆疊。
    /// </summary>
    public abstract class JoypadPanel : UIPanel
    {
        /// <summary>
        /// 固定每頁欄位長度：上下移動一次跨越的格數。
        /// 必須與實際排版的欄數相同，方向鍵的移動方向才會與畫面一致。
        /// </summary>
        protected virtual int gridWidth => 1;

        /// <summary>B 鍵／Esc 是否可以關閉本面板</summary>
        public virtual bool canBackClose => true;

        /// <summary>選取的項目若在捲動區內，移動游標時要一併捲到可視範圍</summary>
        protected virtual ScrollView joypadScroll => null;

        protected readonly List<JoypadItem> joypadItems = new();

        protected int selectedItemIndex = 0;

        /// <summary>
        /// 重設本面板的選項清單，對應 uGUI 版的 InitJoyPad。
        /// </summary>
        protected void InitJoyPad(IEnumerable<JoypadItem> items, int selectIndex = 0)
        {
            joypadItems.Clear();
            joypadItems.AddRange(items);
            selectedItemIndex = selectIndex;
            updateJoyItems();
        }

        /// <summary>
        /// 套用選取狀態，對應 uGUI 版的 updateJoyItems。
        /// </summary>
        protected virtual void updateJoyItems()
        {
            if (joypadItems.Count == 0)
                return;

            selectedItemIndex = Mathf.Clamp(selectedItemIndex, 0, joypadItems.Count - 1);

            for (int i = 0; i < joypadItems.Count; i++)
                joypadItems[i].SetSelected(i == selectedItemIndex);

            ScrollToItem(joypadItems[selectedItemIndex]);
        }

        /// <summary>
        /// 把選取的項目捲進可視範圍。
        ///
        /// ScrollView.ScrollTo 只能在「元素已掛在 panel 上、而且排版算過」之後呼叫，
        /// 否則它內部（ShouldDeferScrollTo）會取用還沒建立的狀態直接丟 NullReferenceException。
        /// 原本用 schedule 延後一幀並不保險：那一幀之間面板可能已經關閉或換頁，
        /// 元素早就被移出樹了。改成沒排版就等 GeometryChangedEvent，
        /// 而且呼叫前後都確認元素還在畫面上。
        /// </summary>
        protected void ScrollToItem(VisualElement target)
        {
            var scroll = joypadScroll;
            if (scroll == null || target == null)
                return;

            if (IsLaidOut(target))
            {
                TryScrollTo(scroll, target);
                return;
            }

            // 剛建立的元素這一幀還沒有 layout，等它第一次排版完再捲
            target.RegisterCallbackOnce<GeometryChangedEvent>(_ => TryScrollTo(scroll, target));
        }

        private static void TryScrollTo(ScrollView scroll, VisualElement target)
        {
            // panel 為 null 代表元素已經被移出畫面（面板關閉、換頁重建）
            if (scroll.panel == null || target.panel == null)
                return;

            scroll.ScrollTo(target);
        }

        private static bool IsLaidOut(VisualElement element)
        {
            if (element.panel == null)
                return false;

            var rect = element.layout;
            return !float.IsNaN(rect.width) && !float.IsNaN(rect.height) && rect.height > 0f;
        }

        protected JoypadItem GetSelectedJoypadItem()
        {
            if (selectedItemIndex >= 0 && selectedItemIndex < joypadItems.Count)
                return joypadItems[selectedItemIndex];
            return null;
        }

        public void CloseTopPanel()
        {
            if (!UITMgr.Instance.IsTopJoypadPanel(this))
                return;
            ClosePanel();
        }

        // ==========================================
        // 接收 UITMgr 傳遞過來的輸入指令
        // 移動規則與 uGUI 版 JoypadPanel 逐條對應
        // ==========================================

        public virtual void OnInputUp()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnUp()) return; // 若 Item 攔截了事件，則不往下執行

            if (selectedItemIndex >= gridWidth)
            {
                selectedItemIndex -= gridWidth;
                updateJoyItems();
            }
        }

        public virtual void OnInputDown()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnDown()) return;

            if (selectedItemIndex + gridWidth < joypadItems.Count)
            {
                selectedItemIndex += gridWidth;
                updateJoyItems();
            }
        }

        public virtual void OnInputLeft()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnLeft()) return;

            if (selectedItemIndex > 0)
            {
                selectedItemIndex--;
                updateJoyItems();
            }
        }

        public virtual void OnInputRight()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnRight()) return;

            if (selectedItemIndex < joypadItems.Count - 1)
            {
                selectedItemIndex++;
                updateJoyItems();
            }
        }

        public virtual void OnInputA()
        {
            var item = GetSelectedJoypadItem();
            if (item == null)
            {
                Log.Warning("[JoypadPanel] NO ITEM");
                return;
            }

            if (item.OnA()) return; // A 鍵等同於執行，但子類別可自行攔截
            item.Execute();
        }

        // ==========================================
        // 其餘指令預設只轉給選取中的 Item，子類別可 override 加上面板層級的行為
        // ==========================================

        public virtual void OnInputB() { GetSelectedJoypadItem()?.OnB(); }
        public virtual void OnInputX() { GetSelectedJoypadItem()?.OnX(); }
        public virtual void OnInputY() { GetSelectedJoypadItem()?.OnY(); }

        public virtual void OnInputShoulderLeft() { GetSelectedJoypadItem()?.OnShoulderLeft(); }
        public virtual void OnInputShoulderRight() { GetSelectedJoypadItem()?.OnShoulderRight(); }
        public virtual void OnInputTriggerLeft() { GetSelectedJoypadItem()?.OnTriggerLeft(); }
        public virtual void OnInputTriggerRight() { GetSelectedJoypadItem()?.OnTriggerRight(); }

        public virtual void OnInputStart() { GetSelectedJoypadItem()?.OnStart(); }
        public virtual void OnInputSystemSelect() { GetSelectedJoypadItem()?.OnSelect(); }

        public virtual void OnInputStickR(Vector2 v) { }
    }
}
