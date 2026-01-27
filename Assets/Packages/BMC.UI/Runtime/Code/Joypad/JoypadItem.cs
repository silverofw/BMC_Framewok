using TMPro;
using UnityEngine;
namespace BMC.UI
{
    public class JoypadItem : MonoBehaviour
    {
        [SerializeField] protected TMP_Text info;
        [SerializeField] private UIButton breatheButton;
        [SerializeField] private GameObject selectObj;

        public void Init(string title, System.Action callback)
        {
            info.text = title;
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