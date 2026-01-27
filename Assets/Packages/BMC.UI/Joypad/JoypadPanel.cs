using System;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UI
{
    public class JoypadPanel : UIPanel
    {
        /// <summary>
        /// 固定每頁欄位長度
        /// </summary>
        [SerializeField] protected int gridWidth = 0;
        [SerializeField] protected List<JoypadItem> joypadItems = new List<JoypadItem>();
        public virtual bool canBackClose => true;

        protected int selectedItemIndex = 0;

        public void InitJoyPad(List<JoypadItem> joypadItems)
        {
            this.joypadItems = joypadItems;
        }

        protected override void Show()
        {
            base.Show();
            UIMgr.Instance.joypadPanels.Push(this);
            //Log.Info($"[JoypadPanel][{joypadPanels.Count}] Show Panel {this.GetType().Name}");

            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_UP, onUp);
            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_DOWN, onDown);
            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_LEFT, onLeft);
            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_RIGHT, onRight);
            UIMgr.Instance.eventHandler.Register((int)UIEvent.INPUT_A, select);

            updateJoyItems();
        }

        public override void close()
        {
            base.close();

            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_UP, onUp);
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_DOWN, onDown);
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_LEFT, onLeft);
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_RIGHT, onRight);
            UIMgr.Instance.eventHandler.UnRegister((int)UIEvent.INPUT_A, select);
        }

        public void CloseTopPanel()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;
            ClosePanel();
        }

        protected void updateJoyItems()
        {
            for (int i = 0; i < joypadItems.Count; i++)
            {
                joypadItems[i].SetSelected(i == selectedItemIndex);
            }
        }

        void onUp()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;

            if (selectedItemIndex >= gridWidth)
            {
                selectedItemIndex -= gridWidth;
                updateJoyItems();
            }
            else
            {
                Log.Info("already top row");
            }
        }

        void onDown()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;

            if (selectedItemIndex + gridWidth < joypadItems.Count)
            {
                selectedItemIndex += gridWidth;
                updateJoyItems();
            }
            else
            {
                Log.Info("already bot row");
            }
        }

        void onLeft()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;

            if (selectedItemIndex > 0)
            {
                selectedItemIndex--;
                updateJoyItems();
            }
            else
            {
                Log.Info("already first");
            }
        }

        void onRight()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;

            if (selectedItemIndex < joypadItems.Count - 1)
            {
                selectedItemIndex++;
                updateJoyItems();
            }
            else
            {
                Log.Info("already last");
            }
        }

        void select()
        {
            if (!UIMgr.Instance.IsTopPanel(this))
                return;
            if (joypadItems.Count <= selectedItemIndex)
            {
                Log.Warning("NO ITEM");
                return;
            }
            joypadItems[selectedItemIndex].Excute();
        }
    }
}
