using UnityEngine;
using BMC.UI;
using System;
public class AgentItem : UIPanel
{
    [SerializeField] private UIText info;
    [SerializeField] private UIText agentName;
    [SerializeField] private UIButton onClick;

    public void Init(string name, string text, Action onclick)
    {
        agentName.Set(name);
        info.Set(text);
        onClick.OnClick = () => { onclick?.Invoke(); };
    }
}
