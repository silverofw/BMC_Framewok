using BMC.UIToolkit;
using UnityEngine.UIElements;

namespace BMC.Story
{
    /// <summary>
    /// UI Toolkit 版精簡對話框面板，對應 uGUI 版的 StoryPanel。
    /// 不是遊戲實際用的 AVG 對話介面(那個在遊戲專案自己的 AvgDialogPanel)，這裡是套件自帶的
    /// 最小可動範例，用來示範怎麼接 StoryPlayer 事件、開啟 StoryLinePanel。
    /// </summary>
    public class StoryPanel : UIPanel
    {
        private UILabel info;
        private VisualElement choiceContainer;
        private UIButton linePanelBtn;

        protected override void OnInit()
        {
            info = Root.Q<UILabel>("info-label");
            choiceContainer = Root.Q<VisualElement>("choice-container");
            linePanelBtn = Root.Q<UIButton>("line-panel-button");

            if (linePanelBtn != null)
                linePanelBtn.OnClick += OpenLinePanel;

            StoryPlayer.Instance.handler.Register<StoryNode, StoryNode>((int)StoryEventID.PlayNode, OnNodePlay);
            StoryPlayer.Instance.handler.Register<StoryEvent, StoryNode, StoryNode>((int)StoryEventID.NodeEventTrigger, OnNodeEvent);
        }

        protected override void OnClose()
        {
            StoryPlayer.Instance.handler.UnRegister<StoryNode, StoryNode>((int)StoryEventID.PlayNode, OnNodePlay);
            StoryPlayer.Instance.handler.UnRegister<StoryEvent, StoryNode, StoryNode>((int)StoryEventID.NodeEventTrigger, OnNodeEvent);
        }

        private async void OpenLinePanel()
        {
            var p = await UITMgr.Instance.ShowPanel<StoryLinePanel>();
            if (p != null)
                await p.RefreshStoryLayout(StoryPlayer.Instance.StartNode, StoryPlayer.Instance._currentPackage);
        }

        private void OnNodePlay(StoryNode crt, StoryNode pre)
        {
            if (crt == null)
                return;

            info?.Set($"{crt.Id}");
            choiceContainer?.Clear();
        }

        private void OnNodeEvent(StoryEvent evt, StoryNode crt, StoryNode pre)
        {
            if (evt == null || evt.ActionCase != StoryEvent.ActionOneofCase.ShowChoices)
                return;

            foreach (var choice in evt.ShowChoices.Choices)
                CreateChoiceButton(choice);
        }

        private void CreateChoiceButton(Choice choice)
        {
            if (choiceContainer == null)
                return;

            var btn = new UIButton { text = choice.Text };
            string targetId = choice.TargetNodeId;
            btn.OnClick += () => OnChoiceSelected(targetId);
            choiceContainer.Add(btn);
        }

        private void OnChoiceSelected(string targetNodeId)
        {
            StoryPlayer.Instance.PlayNode(targetNodeId);
        }
    }
}
