using BMC.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace BMC.UI
{
    public class UIText : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [SerializeField] private string key;

        private void Start()
        {
            Local();
        }

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