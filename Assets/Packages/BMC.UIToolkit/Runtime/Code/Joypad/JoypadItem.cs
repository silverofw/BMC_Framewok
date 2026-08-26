using System;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版手把選項，對應 uGUI 版 BMC.UI 的 JoypadItem。
    ///
    /// uGUI 版是掛在 prefab 上的 MonoBehaviour（自帶 UIButton 與選取框物件），
    /// 這裡改成 UIButton 的子類別：選取狀態純粹用 USS class 表示，
    /// 外觀交給樣式表決定，不需要在 UXML 裡多放一個選取框元素。
    ///
    /// 滑鼠點擊與手把 A 鍵都會走同一個 Execute()，行為保證一致。
    ///
    /// 標了 UxmlElement，UXML 裡可以直接寫 &lt;bmc:JoypadItem /&gt;（MsgPanel 就是這樣用），
    /// 程式動態產生（DebugPanel）也一樣可以 new 出來。
    /// </summary>
    [UxmlElement]
    public partial class JoypadItem : UIButton
    {
        /// <summary>選取狀態的樣式類別，定義於 UIT_Common.uss</summary>
        public const string SelectedClass = "bmc-joypad-item--selected";

        private Action onExecute;

        /// <summary>被選取時通知外部，對應 uGUI 版的 OnSelectEvent</summary>
        public event Action<bool> OnSelectEvent;

        public JoypadItem()
        {
            // 選取游標由 JoypadPanel 管理，不讓 UI Toolkit 內建的焦點導覽
            // 另外搬動一份焦點，否則方向鍵會同時移動兩套游標。
            focusable = false;

            OnClick += Execute;
        }

        public void Init(string title, Action callback)
        {
            text = title;
            onExecute = callback;
        }

        public void Init(Action callback) => onExecute = callback;

        public virtual void SetSelected(bool selected)
        {
            EnableInClassList(SelectedClass, selected);
            OnSelectEvent?.Invoke(selected);
        }

        public void Execute() => onExecute?.Invoke();

        // ========================================================
        // 接收來自 JoypadPanel 的輸入事件
        // 回傳 true 表示此 Item 攔截了該事件，Panel 不需再處理預設邏輯 (如移動游標)
        // 回傳 false 表示未攔截，交由 Panel 繼續處理
        // 子類別可以 override 這些方法來實現特殊操作
        // ========================================================

        public virtual bool OnUp() => false;
        public virtual bool OnDown() => false;
        public virtual bool OnLeft() => false;
        public virtual bool OnRight() => false;

        public virtual bool OnA() => false;
        public virtual bool OnB() => false;
        public virtual bool OnX() => false;
        public virtual bool OnY() => false;

        public virtual bool OnShoulderLeft() => false;
        public virtual bool OnShoulderRight() => false;
        public virtual bool OnTriggerLeft() => false;
        public virtual bool OnTriggerRight() => false;

        public virtual bool OnStart() => false;
        public virtual bool OnSelect() => false;
    }
}
