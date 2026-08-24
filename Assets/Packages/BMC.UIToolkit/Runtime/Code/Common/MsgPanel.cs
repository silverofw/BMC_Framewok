using BMC.Core;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版通用訊息彈窗，對應 uGUI 版 BMC.UI 的 MsgPanel。
    /// 畫面來自套件內建的 UIT_MsgPanel.uxml，專案若在資源系統中放入同名的 UXML 即會優先採用。
    ///
    /// 與 uGUI 版同樣繼承 JoypadPanel：兩顆按鈕就是手把選項，
    /// 左右移動切換、A 執行、B 等同取消。游標順序也比照 uGUI 版：
    /// [0] 取消、[1] 確認，開啟時預設停在取消鈕上。
    /// </summary>
    public class MsgPanel : JoypadPanel
    {
        public override bool maskControl => true;

        /// <summary>兩顆按鈕並排一列，左右移動即可切換</summary>
        protected override int gridWidth => 2;

        private UILabel titleLabel;
        private UILabel infoLabel;
        private JoypadItem confirmButton;
        private JoypadItem cancelButton;

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
            await UITMgr.Instance.EnsureRuntimeRootAsync();

            // 同時間可能需要疊出多個提示，因此不做重複檢查
            var panel = await UITMgr.Instance.ShowPanel<MsgPanel>(layer, false);
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

            confirmButton = Root.Q<JoypadItem>("confirm-button");
            cancelButton = Root.Q<JoypadItem>("cancel-button");

            // UXML 被專案換掉、按鈕型別還停在 UIButton 時，這裡會查不到東西，
            // 畫面上會變成一個按不動的彈窗，因此直接把原因印出來。
            if (confirmButton == null || cancelButton == null)
                Log.Error($"[MsgPanel] UXML 需要 JoypadItem 型別的按鈕 confirm-button:{confirmButton != null} cancel-button:{cancelButton != null}");
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
            bool hasConfirm = action != null;
            if (confirmButton != null)
                confirmButton.style.display = hasConfirm ? DisplayStyle.Flex : DisplayStyle.None;

            cancelButton?.Init(HandleCancel);
            confirmButton?.Init(HandleConfirm);

            // 游標只收看得見的按鈕：隱藏的確認鈕若留在清單裡，
            // 方向鍵會選到一顆畫面上不存在的東西。
            var items = new List<JoypadItem>();
            if (cancelButton != null)
                items.Add(cancelButton);
            if (hasConfirm && confirmButton != null)
                items.Add(confirmButton);

            InitJoyPad(items);
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
        /// B 鍵等同取消。uGUI 版是由 BMC.UI.UIMgr 直接關閉面板、不會執行 cancel 委派，
        /// 這裡改成走同一條取消流程，行為比較符合預期
        /// （UITMgr 看到面板已自行關閉就不會再多關一層）。
        /// </summary>
        public override void OnInputB() => HandleCancel();

        /// <summary>
        /// 點擊遮罩等同取消。
        /// </summary>
        protected override void onMaskClick() => HandleCancel();
    }
}
