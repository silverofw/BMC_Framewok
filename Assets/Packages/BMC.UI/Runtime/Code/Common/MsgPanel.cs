using System;
using UnityEngine;

namespace BMC.UI
{
    public class MsgPanel : JoypadPanel
    {
        public override bool maskControl => true;

        [SerializeField] private UIText titleText;
        [SerializeField] private UIText infoText;

        public void Initial(string msg, string title, Action action = null, Action cancel = null)
        {
            infoText.Set(msg);
            titleText.Set(title);

            var confirmItem = joypadItems[1];
            confirmItem.gameObject.SetActive(action != null);
            confirmItem.Init(() =>
            {
                action?.Invoke();
                ClosePanel();
            });

            var cancelerItem = joypadItems[0];
            cancelerItem.Init(() =>
            {
                cancel?.Invoke();
                ClosePanel();
            });
        }
    }
}