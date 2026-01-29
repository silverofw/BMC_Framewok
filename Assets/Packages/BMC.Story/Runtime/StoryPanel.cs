using BMC.UI;
using UnityEngine;

namespace BMC.Story
{
    public class StoryPanel : UIPanel
    {
        public TextAsset textAsset;
        public bool playOnStart = true;

        [Header("UI References")]
        [SerializeField] private UIText info;
        public Transform choiceContainer;
        public UIButton choiceButtonPrefab;
        public UIButton linePanelBtn;

        private void Awake()
        {
            choiceButtonPrefab.gameObject.SetActive(false);
        }

        private void Start()
        {
            StoryPlayer.Instance.LoadStory(textAsset.bytes);
            linePanelBtn.OnClick = async () => {
                var p = await UIMgr.Instance.ShowPanel<StoryLinePanel>();
                p.RefreshStoryLayout(StoryPlayer.Instance.StartNode, StoryPlayer.Instance._currentPackage);
            };


            StoryPlayer.Instance.Register(onNodeEvent);
            if (playOnStart) StoryPlayer.Instance.StartStory();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            StoryPlayer.Instance.UnRegister(onNodeEvent);
        }

        void onNodeEvent(StoryEvent evt, StoryNode crt, StoryNode pre)
        {
            if (evt == null || crt == null 
                || evt.ActionCase != StoryEvent.ActionOneofCase.ShowChoices) 
                return;

            info.Set($"{crt.Id}");
            // 清除舊選項
            foreach (Transform child in choiceContainer) Destroy(child.gameObject);

            foreach (var choice in evt.ShowChoices.Choices)
            {
                CreateChoiceButton(choice);
            }
        }

        private void CreateChoiceButton(Choice choice)
        {
            var go = Instantiate(choiceButtonPrefab.gameObject, choiceContainer);
            var textComp = go.GetComponentInChildren<UIText>();
            if (textComp != null) textComp.Set(choice.Text);

            string targetId = choice.TargetNodeId;
            go.GetComponent<UIButton>().OnClick = () => OnChoiceSelected(targetId);
            go.SetActive(true);
        }

        private void OnChoiceSelected(string targetNodeId)
        {
            StoryPlayer.Instance.PlayNode(targetNodeId);
        }
    }
}