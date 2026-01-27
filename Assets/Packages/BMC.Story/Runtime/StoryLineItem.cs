using BMC.UI;
using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace BMC.Story
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
    public class StoryLineItem : UI.UIPanel
    {
        [SerializeField] private UIText info;
        [SerializeField] private UIButton btn;
        [SerializeField] private GameObject select;

        // 這些是 Runtime 必要的識別資料
        [HideInInspector] public string NodeID;
        [HideInInspector] public string VideoPath;

        public void Init(StoryNode node, Action action)
        {
            NodeID = node.Id;
            VideoPath = node.VideoPath;
            btn.OnClick = () => action?.Invoke();

            if (info != null) info.Set($"[{node.Id}] {node.VideoPath}");
            
            select.SetActive(StoryPlayer.Instance.IsCrtNode(node));
        }
    }
}