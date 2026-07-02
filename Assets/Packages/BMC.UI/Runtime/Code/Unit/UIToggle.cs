using System; // 引入 System 以使用 Action
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UI
{
    public class UIToggle : MonoBehaviour
    {
        [Header("當前狀態")]
        [SerializeField] private bool isOn = true;
        [SerializeField] public UIButton btn;

        [Header("狀態切換物件列表")]
        [SerializeField] private List<GameObject> onObjects = new List<GameObject>();
        [SerializeField] private List<GameObject> offObjects = new List<GameObject>();

        // 新增：狀態改變事件 (廣播站)
        // 任何外部腳本都可以訂閱這個事件，並在狀態改變時收到最新的 bool 值
        public event Action<bool> OnValueChanged;

        private void Start()
        {
            UpdateVisuals();

            btn.OnClick = () =>
            {
                Set(!isOn);
            };
        }

        /// <summary>
        /// 供外部呼叫，直接設定為 ON 或 OFF
        /// </summary>
        public void Set(bool state)
        {
            if (isOn == state) return; 
            
            isOn = state;
            UpdateVisuals();

            // 新增：觸發事件，通知所有訂閱者當前的新狀態
            // 使用 ?.Invoke() 是為了安全檢查，如果沒有任何人訂閱就不會執行，避免報錯
            OnValueChanged?.Invoke(isOn);
        }

        /// <summary>
        /// 供外部呼叫，反轉當前狀態
        /// </summary>
        public void Toggle()
        {
            // 直接複用 Set 方法，讓邏輯集中，確保一定會觸發事件
            Set(!isOn); 
        }

        public bool IsOn => isOn;

        private void UpdateVisuals()
        {
            if (onObjects != null)
            {
                foreach (var obj in onObjects)
                {
                    if (obj != null) obj.SetActive(isOn);
                }
            }

            if (offObjects != null)
            {
                foreach (var obj in offObjects)
                {
                    if (obj != null) obj.SetActive(!isOn);
                }
            }
        }
    }
}