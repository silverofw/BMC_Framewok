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
            // todo 呼叫UI前要先初始化故事播放器
            //StoryPlayer.Instance.LoadStory(textAsset.bytes);
            linePanelBtn.OnClick = async () => {
                var p = await UIMgr.Instance.ShowPanel<StoryLinePanel>();
                p.RefreshStoryLayout(StoryPlayer.Instance.StartNode, StoryPlayer.Instance._currentPackage);
            };

            StoryPlayer.Instance.Register(onNodePlay);
            StoryPlayer.Instance.Register(onNodeEvent);
            if (playOnStart) StoryPlayer.Instance.StartStory();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            StoryPlayer.Instance.UnRegister(onNodePlay);
            StoryPlayer.Instance.UnRegister(onNodeEvent);
        }
        void onNodePlay(StoryNode crt, StoryNode pre)
        {
            if (crt == null)
                return;

            info.Set($"{crt.Id}");
            // 清除舊選項
            foreach (Transform child in choiceContainer) Destroy(child.gameObject);
        }
        void onNodeEvent(StoryEvent evt, StoryNode crt, StoryNode pre)
        {
            if (evt == null || evt.ActionCase != StoryEvent.ActionOneofCase.ShowChoices) 
                return;

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