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


            StoryPlayer.Instance.Register(StoryEventID.PlayNode, onNodePlay);
            if (playOnStart) StoryPlayer.Instance.StartStory();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            StoryPlayer.Instance.UnRegister(StoryEventID.PlayNode, onNodePlay);
        }

        void onNodePlay(StoryNode crt, StoryNode pre)
        {
            info.Set($"{crt.Id}");
            // 清除舊選項
            foreach (Transform child in choiceContainer) Destroy(child.gameObject);

            // 新邏輯：遍歷 OnEnterEvents 尋找 ShowChoices 事件
            if (crt.OnEnterEvents != null)
            {
                foreach (var evt in crt.OnEnterEvents)
                {
                    if (evt.ActionCase == StoryEvent.ActionOneofCase.ShowChoices)
                    {
                        foreach (var choice in evt.ShowChoices.Choices)
                        {
                            CreateChoiceButton(choice);
                        }
                    }
                }
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