using System;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UI
{
    /// <summary>
    /// UI Toggle 群組管理器（實現單選 / Radio Button 效果）
    /// </summary>
    public class UIToggleGroup : MonoBehaviour
    {
        [Header("群組設定")]
        [Tooltip("是否允許全部關閉。若為 false，則至少保持一個 Toggle 處於開啟狀態。")]
        [SerializeField] private bool allowSwitchOff = false;

        [Header("管理的 Toggle 列表")]
        [SerializeField] private List<UIToggle> toggles = new List<UIToggle>();

        // 儲存委派 mapping 以便正確取消訂閱事件
        private readonly Dictionary<UIToggle, Action<bool>> listenerMap = new Dictionary<UIToggle, Action<bool>>();

        private void OnEnable()
        {
            RegisterAll();
        }

        private void OnDisable()
        {
            UnregisterAll();
        }

        /// <summary>
        /// 註冊列表內所有的 Toggle 事件
        /// </summary>
        private void RegisterAll()
        {
            foreach (var toggle in toggles)
            {
                if (toggle != null)
                {
                    RegisterToggle(toggle);
                }
            }

            // 初始化校正：確保初始狀態符合群組規則
            EnsureSingleActive();
        }

        /// <summary>
        /// 解除所有 Toggle 的事件註冊
        /// </summary>
        private void UnregisterAll()
        {
            var currentToggles = new List<UIToggle>(listenerMap.Keys);
            foreach (var toggle in currentToggles)
            {
                UnregisterToggle(toggle);
            }
        }

        /// <summary>
        /// 動態將指定 Toggle 加入群組
        /// </summary>
        public void RegisterToggle(UIToggle toggle)
        {
            if (toggle == null) return;

            if (!toggles.Contains(toggle))
            {
                toggles.Add(toggle);
            }

            // 若該 Toggle 已在監聽清單中，先取消以避免重複綁定
            UnregisterToggle(toggle);

            // 建立閉包包裝委派，將 Toggle 本身作為參數傳入處理方法
            Action<bool> handler = (isOn) => OnToggleStateChanged(toggle, isOn);
            listenerMap[toggle] = handler;
            toggle.OnValueChanged += handler;
        }

        /// <summary>
        /// 動態將指定 Toggle 從群組移除
        /// </summary>
        public void UnregisterToggle(UIToggle toggle)
        {
            if (toggle == null) return;

            if (listenerMap.TryGetValue(toggle, out var handler))
            {
                toggle.OnValueChanged -= handler;
                listenerMap.Remove(toggle);
            }
        }

        /// <summary>
        /// 當群組內的 Toggle 狀態發生變更時觸發
        /// </summary>
        private void OnToggleStateChanged(UIToggle changedToggle, bool isOn)
        {
            if (isOn)
            {
                // 當前 Toggle 被開啟，強制關閉群組內其他所有 Toggle
                foreach (var toggle in toggles)
                {
                    if (toggle != null && toggle != changedToggle)
                    {
                        toggle.Set(false);
                    }
                }
            }
            else
            {
                // 當前 Toggle 被關閉，如果不允許全部關閉且目前沒有任何開啟的 Toggle
                if (!allowSwitchOff && !HasAnyActiveToggle())
                {
                    // 強制保持開啟
                    changedToggle.Set(true);
                }
            }
        }

        /// <summary>
        /// 檢查群組內是否有任何 Toggle 處於開啟狀態
        /// </summary>
        private bool HasAnyActiveToggle()
        {
            foreach (var toggle in toggles)
            {
                if (toggle != null && toggle.IsOn)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 檢查並修正初始狀態，確保最多/最少只有一個 Toggle 開啟
        /// </summary>
        private void EnsureSingleActive()
        {
            UIToggle activeToggle = null;

            foreach (var toggle in toggles)
            {
                if (toggle == null) continue;

                if (toggle.IsOn)
                {
                    if (activeToggle == null)
                    {
                        activeToggle = toggle;
                    }
                    else
                    {
                        // 如果 Inspector 中設定了多個 ON，將後續的強制關閉
                        toggle.Set(false);
                    }
                }
            }

            // 如果沒有任何 ON 且不允許全部關閉，預設開啟第一個
            if (activeToggle == null && !allowSwitchOff && toggles.Count > 0)
            {
                toggles[0]?.Set(true);
            }
        }

        /// <summary>
        /// 取得當前開啟的 Toggle（若無開啟則回傳 null）
        /// </summary>
        public UIToggle GetActiveToggle()
        {
            foreach (var toggle in toggles)
            {
                if (toggle != null && toggle.IsOn)
                {
                    return toggle;
                }
            }
            return null;
        }
    }
}