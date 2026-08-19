using System;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版通用訊息彈窗，對應 uGUI 版 BMC.UI 的 MsgPanel。
    /// 畫面來自套件內建的 Resources/MsgPanel.uxml，專案若在資源系統中放入同名的 UXML 即會優先採用。
    /// </summary>
    public class MsgPanel : UIPanel
    {
        public override bool maskControl => true;

        private UILabel titleLabel;
        private UILabel infoLabel;
        private UIButton confirmButton;
        private UIButton cancelButton;

        private Action onConfirm;
        private Action onCancel;

        /// <summary>
        /// 顯示訊息彈窗。
        /// </summary>
        /// <param name="msg">訊息內容</param>
        /// <param name="title">標題</param>
        /// <param name="action">按下確認後執行；傳 null 則隱藏確認鈕（單鈕提示用）</param>
        /// <param name="cancel">按下取消或點擊遮罩後執行</param>
        /// <param name="layer">顯示層級，預設為全域最上層</param>
        public static async UniTask<MsgPanel> Show(string msg, string title, Action action = null, Action cancel = null,
            UILayer layer = UILayer.UI_Top)
        {
            // 專案若尚未建立根節點就地補上，讓彈窗在任何情境都能出現
            await UIMgr.Instance.EnsureRuntimeRootAsync();

            // 同時間可能需要疊出多個提示，因此不做重複檢查
            var panel = await UIMgr.Instance.ShowPanel<MsgPanel>(layer, false);
            panel?.Initial(msg, title, action, cancel);
            return panel;
        }

        protected override void OnInit()
        {
            // 面板本體要攔下點擊，否則會冒泡到遮罩觸發關閉
            var box = Root.Q<VisualElement>("box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            titleLabel = Root.Q<UILabel>("title");
            infoLabel = Root.Q<UILabel>("info");

            confirmButton = Root.Q<UIButton>("confirm-button");
            if (confirmButton != null)
                confirmButton.OnClick += HandleConfirm;

            cancelButton = Root.Q<UIButton>("cancel-button");
            if (cancelButton != null)
                cancelButton.OnClick += HandleCancel;
        }

        /// <summary>
        /// 設定內容與按鈕行為。簽名與 uGUI 版 BMC.UI.MsgPanel.Initial 一致。
        /// </summary>
        public void Initial(string msg, string title, Action action = null, Action cancel = null)
        {
            onConfirm = action;
            onCancel = cancel;

            // 用 Set 而非直接寫 text：呼叫端傳進來的是已決定好的內容，
            // Set 會清掉 local-key，避免之後語言變更把它蓋掉。
            // 需要跟著語言走時，呼叫端請自行傳 LocalMgr.Local(key) 的結果。
            infoLabel?.Set(msg);
            titleLabel?.Set(title);

            // 沒有確認行為時視為單鈕提示：只留取消鈕當作「關閉」
            if (confirmButton != null)
                confirmButton.style.display = action != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleConfirm()
        {
            onConfirm?.Invoke();
            ClosePanel();
        }

        private void HandleCancel()
        {
            onCancel?.Invoke();
            ClosePanel();
        }

        /// <summary>
        /// 點擊遮罩等同取消。
        /// </summary>
        protected override void onMaskClick() => HandleCancel();
    }
}
