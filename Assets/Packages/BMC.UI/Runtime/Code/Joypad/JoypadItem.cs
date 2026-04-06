using TMPro;
using UnityEngine;
namespace BMC.UI
{
    public class JoypadItem : MonoBehaviour
    {
        [SerializeField] protected UIText info;
        [SerializeField] private UIButton breatheButton;
        [SerializeField] private GameObject selectObj;

        public void Init(string title, System.Action callback)
        {
            info.Set(title);
            breatheButton.OnClick = callback;
        }
        public void Init(System.Action callback)
        {
            breatheButton.OnClick = callback;
        }

        public void SetSelected(bool selected)
        {
            selectObj.SetActive(selected);
        }
        public void Excute()
        {
            breatheButton.OnClick?.Invoke();
        }
    }
}