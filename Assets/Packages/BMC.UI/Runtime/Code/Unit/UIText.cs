using BMC.Core;
using TMPro;
using UnityEngine;

namespace BMC.UI
{
    public class UIText : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private string key;

        // 改用 OnEnable／OnDisable 而非 Start：
        // 除了初次顯示要翻譯，還要在語言切換時即時更新，
        // 而物件停用期間不該持有訂閱。
        private void OnEnable()
        {
            Local();
            LocalMgr.Instance.OnLanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            LocalMgr.Instance.OnLanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(SystemLanguage language) => Local();

        public void Set(string msg)
        {
            if (text != null)
                text.text = msg;
        }

        // 去除了 ContextMenu，並改為 public 供 Editor 呼叫
        public void Local()
        {
            if (string.IsNullOrEmpty(key))
            {
                //Log.Info("key is null");
                return;
            }

            Set(LocalMgr.Instance.Local(key));
        }
    }
}