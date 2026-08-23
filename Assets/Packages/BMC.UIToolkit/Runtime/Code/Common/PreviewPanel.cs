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

        private const string TabActiveClass = "bmc-tab--active";
        private const string HiddenClass = "bmc-hidden";

        // 字體特效類別，彼此互斥：切換時先全部移除，再加上被按下的那一個
        private static readonly string[] FxClasses =
        {
            "fx-bold", "fx-outline", "fx-shadow", "fx-neon", "fx-spaced", "fx-italic",
        };

        private Label clickCountLabel;
        private int clickCount;

        private UIButton tabBasicButton;
        private UIButton tabFontFxButton;
        private VisualElement tabBasicContent;
        private VisualElement tabFontFxContent;

        private Label fxPreviewLabel;

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

            InitTabs();
            InitFontEffects();
        }

        private void HandleCountClicked()
        {
            clickCount++;
            if (clickCountLabel != null)
                clickCountLabel.text = $"OnClick fired: {clickCount}";
        }

        private void InitTabs()
        {
            tabBasicButton = Root.Q<UIButton>("tab-basic");
            tabFontFxButton = Root.Q<UIButton>("tab-fontfx");
            tabBasicContent = Root.Q<VisualElement>("scroll");
            tabFontFxContent = Root.Q<VisualElement>("scroll-fontfx");

            if (tabBasicButton != null)
                tabBasicButton.OnClick += () => SelectTab(basic: true);
            if (tabFontFxButton != null)
                tabFontFxButton.OnClick += () => SelectTab(basic: false);

            SelectTab(basic: true);
        }

        private void SelectTab(bool basic)
        {
            tabBasicContent?.EnableInClassList(HiddenClass, !basic);
            tabFontFxContent?.EnableInClassList(HiddenClass, basic);

            tabBasicButton?.EnableInClassList(TabActiveClass, basic);
            tabFontFxButton?.EnableInClassList(TabActiveClass, !basic);
        }

        private void InitFontEffects()
        {
            fxPreviewLabel = Root.Q<Label>("fx-preview");

            BindFxButton("fx-none", null);
            BindFxButton("fx-bold", "fx-bold");
            BindFxButton("fx-outline", "fx-outline");
            BindFxButton("fx-shadow", "fx-shadow");
            BindFxButton("fx-neon", "fx-neon");
            BindFxButton("fx-spaced", "fx-spaced");
            BindFxButton("fx-italic", "fx-italic");
        }

        private void BindFxButton(string buttonName, string fxClass)
        {
            var button = Root.Q<UIButton>(buttonName);
            if (button != null)
                button.OnClick += () => ApplyFontEffect(fxClass);
        }

        /// <summary>
        /// 套用字體特效：先清掉所有 fx- 類別再加上指定的一個，null／空字串代表還原成一般樣式。
        /// </summary>
        private void ApplyFontEffect(string fxClass)
        {
            if (fxPreviewLabel == null)
                return;

            foreach (var cls in FxClasses)
                fxPreviewLabel.RemoveFromClassList(cls);

            if (!string.IsNullOrEmpty(fxClass))
                fxPreviewLabel.AddToClassList(fxClass);
        }
    }
}
