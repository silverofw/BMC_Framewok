using System.Collections.Generic;
using UnityEngine;
namespace BMC.UI
{
    public class JoypadLRPagePanel : JoypadPanel
    {
        [SerializeField] private List<JoypadItem> pages = new();
        [SerializeField] private List<JoypadLRPageSubPanel> contents;

        private int selectedPageIndex = 0;

        protected override void Show()
        {
            base.Show();

            for (int i = 0; i < contents.Count; i++)
            {
                contents[i].Init(this);
            }
            updatePages();

            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_SHOULDER_L, onLeft);
            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_SHOULDER_R, onRight);
        }

        public override void close()
        {
            base.close();
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_SHOULDER_L, onLeft);
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_SHOULDER_R, onRight);
        }

        void onLeft()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;
            selectedPageIndex--;
            if (selectedPageIndex < 0)
                selectedPageIndex = contents.Count - 1;
            updatePages();
        }

        void onRight()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;
            selectedPageIndex++;
            if (selectedPageIndex >= contents.Count)
                selectedPageIndex = 0;
            updatePages();
        }

        void updatePages()
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].SetSelected(i == selectedPageIndex);
            }

            for (int i = 0; i < contents.Count; i++)
            {
                if (i == selectedPageIndex)
                    contents[i].gameObject.SetActive(true);
                else
                    contents[i].gameObject.SetActive(false);
            }
        }
    }
}