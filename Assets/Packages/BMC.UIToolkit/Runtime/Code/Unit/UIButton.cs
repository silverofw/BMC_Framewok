using System;
using BMC.Core;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版按鈕，對應 uGUI 版 BMC.UI 的 UIButton。
    /// 包裝原生 Button，統一經由 UITMgr 事件匯流排送出點擊音效事件（AUDIO_BUTTON_CLICK），
    /// 實際播放的音效由外部（例如 BMC.Audio）訂閱決定，UI 層不直接相依特定音效系統。
    ///
    /// 按下時的縮放回饋定義在 UIT_Theme.tss 的 UIButton:active 規則，
    /// 對齊 uGUI 版的 scale 0.9 / 0.1 秒 / Ease.OutQuad。
    /// </summary>
    [UxmlElement]
    public partial class UIButton : Button
    {
        public event Action OnClick;

        private string localKeyValue;

        /// <summary>
        /// 按鈕文字的多語言鍵值。留空則維持 UXML 上編排的 text 不動。
        /// </summary>
        [UxmlAttribute]
        public string localKey
        {
            get => localKeyValue;
            set
            {
                localKeyValue = value;
                ApplyLocalization();
            }
        }

        public UIButton()
        {
            clicked += HandleClicked;
            this.BindLocalization(ApplyLocalization);
        }

        /// <summary>
        /// 依目前語言套用譯文。語系資料尚未載入時保留 UXML 上的預設文字。
        /// </summary>
        public void ApplyLocalization()
        {
            if (string.IsNullOrEmpty(localKeyValue))
                return;
            if (!LocalMgr.Instance.IsReady)
                return;

            // LangData 查不到 key 時的約定是原樣回傳 key。
            // 這種情況保留 UXML 上編排好的文字，不要把 COMMON_OK 這種內部鍵值顯示給玩家。
            var translated = LocalMgr.Instance.Local(localKeyValue);
            if (translated == localKeyValue)
                return;

            text = translated;
        }

        private void HandleClicked()
        {
            UITMgr.Instance.eventHandler.Send((int)UIEvent.AUDIO_BUTTON_CLICK);
            OnClick?.Invoke();
        }
    }
}
