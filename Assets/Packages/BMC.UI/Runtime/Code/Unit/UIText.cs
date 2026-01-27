using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace BMC.UI
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
    public class UIText : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        public void Set(string msg)
        {
            text.text = msg;
        }
    }
}
