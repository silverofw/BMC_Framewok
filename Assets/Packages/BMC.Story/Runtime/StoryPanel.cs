using BMC.UI;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace BMC.Story
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
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
            ShowChoices(crt);
        }

        private void ShowChoices(StoryNode crt)
        {
            foreach (Transform child in choiceContainer) Destroy(child.gameObject);

            foreach (var choice in crt.Choices)
            {
                var go = Instantiate(choiceButtonPrefab.gameObject, choiceContainer);
                var textComp = go.GetComponentInChildren<UIText>();
                if (textComp != null) textComp.Set(choice.Text);

                string targetId = choice.TargetNodeId;
                go.GetComponent<UIButton>().OnClick = () => OnChoiceSelected(targetId);
                go.SetActive(true);
            }
        }

        private void OnChoiceSelected(string targetNodeId)
        {
            StoryPlayer.Instance.PlayNode(targetNodeId);
        }
    }
}
