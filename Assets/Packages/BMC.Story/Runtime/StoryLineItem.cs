using BMC.UI;
using System;
using UnityEngine;

namespace BMC.Story
{
    public class StoryLineItem : UI.UIPanel
    {
        [SerializeField] protected UIText info;
        [SerializeField] protected UIButton btn;
        [SerializeField] private GameObject select;

        // 這些是 Runtime 必要的識別資料
        [HideInInspector] public string NodeID;
        [HideInInspector] public string VideoPath;

        public virtual void Init(StoryNode node, Action action)
        {
            NodeID = node.Id;
            btn.OnClick = () => action?.Invoke();

            if (info != null) info.Set($"[{node.Id}]");
            
            select.SetActive(StoryPlayer.Instance.IsCrtNode(node));
        }
    }
}