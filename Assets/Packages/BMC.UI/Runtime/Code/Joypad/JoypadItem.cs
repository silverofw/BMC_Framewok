using System;
using TMPro;
using UnityEngine;
namespace BMC.UI
{
    public class JoypadItem : MonoBehaviour
    {
        [SerializeField] protected UIText info;
        [SerializeField] private UIButton btn;
        [SerializeField] private GameObject selectObj;

        private Action OnClick;
        public Action<bool> OnSelectEvent; // 避免與原本的名稱衝突，稍微改名為 OnSelectEvent

        private void Start()
        {
            btn.OnClick = Excute;
        }

        public void Init(string title, System.Action callback)
        {
            info.Set(title);
            OnClick = callback;
        }
        public void Init(System.Action callback)
        {
            OnClick = callback;
        }

        public void SetSelected(bool selected)
        {
            selectObj.SetActive(selected);
            OnSelectEvent?.Invoke(selected);
        }
        public void Excute()
        {
            OnClick?.Invoke();
        }

        // ========================================================
        // 接收來自 JoypadPanel 的輸入事件
        // 回傳 true 表示此 Item 攔截了該事件，Panel 不需再處理預設邏輯 (如移動游標)
        // 回傳 false 表示未攔截，交由 Panel 繼續處理
        // 子類別 (如 BagJoypadItem) 可以 override 這些方法來實現特殊操作
        // ========================================================

        public virtual bool OnUp() { return false; }
        public virtual bool OnDown() { return false; }
        public virtual bool OnLeft() { return false; }
        public virtual bool OnRight() { return false; }

        public virtual bool OnA() { return false; } // 通常 A 鍵對應 Excute，但如果子類想自己攔截可以 override
        public virtual bool OnB() { return false; }
        public virtual bool OnX() { return false; }
        public virtual bool OnY() { return false; }

        public virtual bool OnStickR(Vector2 v) { return false; }
        public virtual bool OnStickRUp() { return false; }
        public virtual bool OnStickRDown() { return false; }
        public virtual bool OnStickRLeft() { return false; }
        public virtual bool OnStickRRight() { return false; }

        public virtual bool OnShoulderLeft() { return false; }
        public virtual bool OnShoulderRight() { return false; }
        public virtual bool OnTriggerLeft() { return false; }
        public virtual bool OnTriggerRight() { return false; }

        public virtual bool OnStart() { return false; }
        public virtual bool OnSelect() { return false; } // 這是指手把上的 Select 鍵，不是 A 鍵
    }
}