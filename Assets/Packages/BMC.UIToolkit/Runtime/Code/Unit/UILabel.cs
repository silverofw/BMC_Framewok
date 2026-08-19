using UnityEngine.UIElements;
using BMC.Core;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版多語言文字，對應 uGUI 版 BMC.UI 的 UIText。
    ///
    /// 相較 uGUI 版把 key 藏在 prefab 的 Inspector 裡，這裡 key 直接寫在 UXML 上
    /// （local-key 屬性），版面與譯文對應關係一眼可見。
    /// </summary>
    [UxmlElement]
    public partial class UILabel : Label
    {
        private string localKeyValue;

        /// <summary>
        /// 多語言鍵值。留空則維持 UXML 上編排的 text 不動。
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

        public UILabel()
        {
            this.BindLocalization(ApplyLocalization);
        }

        /// <summary>
        /// 直接指定文字，對應 uGUI 版 UIText.Set。
        /// 會清掉 localKey：明確設定的內容不該在下次語言變更時被譯文蓋掉。
        /// </summary>
        public void Set(string msg)
        {
            localKeyValue = null;
            text = msg;
        }

        /// <summary>
        /// 依目前語言套用譯文。
        /// 語系資料尚未載入時刻意不動作，保留 UXML 上的預設文字，
        /// 避免在還沒接上多語言的專案裡把 key 直接顯示給玩家。
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
    }
}
