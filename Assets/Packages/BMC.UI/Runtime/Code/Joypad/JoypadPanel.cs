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
            UIMgr.Instance.PushJoypadPanel(this); // 改由 UIMgr 來負責 Push 和註冊事件

            updateJoyItems();
        }

        public override void close()
        {
            base.close();
            // 反註冊與 Pop 動作也交由 UIMgr 的 RemovePanel 或 closeJoypadPanel 處理
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

        // ==========================================
        // 取得當前選取的 JoypadItem
        // ==========================================
        protected JoypadItem GetSelectedJoypadItem()
        {
            if (joypadItems != null && selectedItemIndex >= 0 && selectedItemIndex < joypadItems.Count)
            {
                return joypadItems[selectedItemIndex];
            }
            return null;
        }

        // ==========================================
        // 接收 UIMgr 傳遞過來的輸入指令 (這部分原本是 Action，現在變成 public 給 UIMgr 呼叫)
        // ==========================================
        public virtual void OnInputUp()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnUp()) return; // 若 Item 攔截了事件，則不往下執行

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

        public virtual void OnInputDown()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnDown()) return;

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

        public virtual void OnInputLeft()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnLeft()) return;

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

        public virtual void OnInputRight()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnRight()) return;

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

        public virtual void OnInputA()
        {
            var item = GetSelectedJoypadItem();
            if (item != null && item.OnA()) return; // A鍵等同於原本的 Select 邏輯

            if (joypadItems.Count <= selectedItemIndex)
            {
                Log.Warning("NO ITEM");
                return;
            }
            joypadItems[selectedItemIndex].Excute();
        }

        // ==========================================
        // 其餘擴充指令，由子類別根據需求 override，並優先傳遞給選取的 Item
        // ==========================================
        public virtual void OnInputStickR(Vector2 v) { var item = GetSelectedJoypadItem(); if (item != null) item.OnStickR(v); }
        public virtual void OnInputStickRUp() { var item = GetSelectedJoypadItem(); if (item != null) item.OnStickRUp(); }
        public virtual void OnInputStickRDown() { var item = GetSelectedJoypadItem(); if (item != null) item.OnStickRDown(); }
        public virtual void OnInputStickRLeft() { var item = GetSelectedJoypadItem(); if (item != null) item.OnStickRLeft(); }
        public virtual void OnInputStickRRight() { var item = GetSelectedJoypadItem(); if (item != null) item.OnStickRRight(); }

        public virtual void OnInputB() { var item = GetSelectedJoypadItem(); if (item != null) item.OnB(); }
        public virtual void OnInputX() { var item = GetSelectedJoypadItem(); if (item != null) item.OnX(); }
        public virtual void OnInputY() { var item = GetSelectedJoypadItem(); if (item != null) item.OnY(); }

        public virtual void OnInputShoulderLeft() { var item = GetSelectedJoypadItem(); if (item != null) item.OnShoulderLeft(); }
        public virtual void OnInputShoulderRight() { var item = GetSelectedJoypadItem(); if (item != null) item.OnShoulderRight(); }
        public virtual void OnInputTriggerLeft() { var item = GetSelectedJoypadItem(); if (item != null) item.OnTriggerLeft(); }
        public virtual void OnInputTriggerRight() { var item = GetSelectedJoypadItem(); if (item != null) item.OnTriggerRight(); }

        public virtual void OnInputStart() { var item = GetSelectedJoypadItem(); if (item != null) item.OnStart(); }
        public virtual void OnInputSystemSelect() { var item = GetSelectedJoypadItem(); if (item != null) item.OnSelect(); }
    }
}