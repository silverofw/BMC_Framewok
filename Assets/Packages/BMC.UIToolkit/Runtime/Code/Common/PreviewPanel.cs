using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版元件展示櫃，對應 uGUI 版 BMC.UI 的 PreviewPanel。
    ///
    /// 與 uGUI 版相同定位：內容主要寫在 UXML 裡，程式只負責互動驗證。
    /// 用途是快速確認三件事——主題字型有沒有掛上、UIButton 的按壓回饋與
    /// 啟用狀態是否正常、共用樣式類別（bmc-title／bmc-text／bmc-button）長什麼樣。
    /// </summary>
    public class PreviewPanel : UIPanel
    {
        public override bool maskControl => true;

        private Label clickCountLabel;
        private int clickCount;

        public static async UniTask<PreviewPanel> Show(UILayer layer = UILayer.UI_Top)
        {
            await UIMgr.Instance.EnsureRuntimeRootAsync();
            return await UIMgr.Instance.ShowPanel<PreviewPanel>(layer);
        }

        protected override void OnInit()
        {
            // 面板本體要攔下點擊，否則會冒泡到遮罩觸發關閉
            var box = Root.Q<VisualElement>("box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            clickCountLabel = Root.Q<Label>("click-count");

            var normal = Root.Q<UIButton>("btn-normal");
            if (normal != null)
                normal.OnClick += HandleCountClicked;

            var primary = Root.Q<UIButton>("btn-primary");
            if (primary != null)
                primary.OnClick += HandleCountClicked;

            // 停用狀態的展示：確認 :disabled 樣式與點擊攔截都正常
            Root.Q<UIButton>("btn-disabled")?.SetEnabled(false);

            // close-button 由 UIPanel.InternalInit 自動綁定關閉，這裡不需處理
        }

        private void HandleCountClicked()
        {
            clickCount++;
            if (clickCountLabel != null)
                clickCountLabel.text = $"OnClick fired: {clickCount}";
        }
    }
}
