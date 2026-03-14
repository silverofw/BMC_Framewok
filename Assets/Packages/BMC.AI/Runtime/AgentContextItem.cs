using BMC.UI;
using System;
using UnityEngine;

public class AgentContextItem : BMC.UI.UIPanel
{
    [SerializeField] private UIText nameText;
    [SerializeField] private UIButton btn;

    public void Init(string info, Action onclick)
    {
        nameText.Set(info);
        btn.OnClick = onclick;
    }
}
