using System;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版按鈕，對應 uGUI 版 BMC.UI 的 UIButton。
    /// 包裝原生 Button，統一經由 UIMgr 事件匯流排送出點擊音效事件（AUDIO_BUTTON_CLICK），
    /// 實際播放的音效由外部（例如 BMC.Audio）訂閱決定，UI 層不直接相依特定音效系統。
    /// </summary>
    [UxmlElement]
    public partial class UIButton : Button
    {
        public event Action OnClick;

        public UIButton()
        {
            clicked += HandleClicked;
        }

        private void HandleClicked()
        {
            UIMgr.Instance.eventHandler.Send((int)UIEvent.AUDIO_BUTTON_CLICK);
            OnClick?.Invoke();
        }
    }
}
