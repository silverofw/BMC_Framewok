using UnityEngine.UIElements;

namespace BMC.UIToolkit.Samples.BasicUIFlow
{
    /// <summary>
    /// 最基礎的 UI Toolkit 面板範例：顯示標題、可累加的計數器，
    /// 以及「Increment」／「Close」兩顆按鈕，示範 UIPanel 開關與 UIButton 點擊流程。
    /// </summary>
    public class SamplePanel : UIPanel
    {
        private Label countLabel;
        private int count;

        protected override void OnInit()
        {
            // 面板內容區塊要攔截點擊，避免冒泡到 mask 觸發背景關閉
            var box = Root.Q<VisualElement>("sample-panel-box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            countLabel = Root.Q<Label>("count-label");

            var incrementButton = Root.Q<UIButton>("increment-button");
            if (incrementButton != null)
                incrementButton.OnClick += OnIncrementClicked;

            // close-button 由 UIPanel.InternalInit 自動綁定 ClosePanel，這裡不需要額外處理
        }

        private void OnIncrementClicked()
        {
            count++;
            if (countLabel != null)
                countLabel.text = $"Count: {count}";
        }
    }
}
