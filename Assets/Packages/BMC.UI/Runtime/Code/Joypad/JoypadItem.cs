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
        public Action<bool> OnSelect;

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
            OnSelect?.Invoke(selected);
        }
        public void Excute()
        {
            OnClick?.Invoke();
        }
    }
}