using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版除錯面板的熱鍵：F4 開啟／關閉。
    ///
    /// 對照 uGUI 版由 BMC.UI.UIInputTrigger 綁的 F2，兩套各用一個鍵，
    /// 同一台機器上可以並存互不干擾。
    ///
    /// 放在 BMC.UIToolkit.Debug 而非核心套件：核心不相依 InputSystem，
    /// 熱鍵屬於除錯便利功能，整個 Runtime/Debug 資料夾刪掉也不影響核心。
    /// </summary>
    public class UIToolkitDebugTrigger : MonoBehaviour
    {
        private static UIToolkitDebugTrigger instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (instance != null)
                return;

            var go = new GameObject("[UIToolkitDebugTrigger]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UIToolkitDebugTrigger>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (!keyboard.f4Key.wasPressedThisFrame)
                return;

            // 再按一次收起來：熱鍵單鍵開關，不必再去點 Close
            var open = UITMgr.Instance.GetPanel<DebugPanel>();
            if (open != null && !open.IsClosed)
            {
                open.ClosePanel();
                return;
            }

            DebugPanel.Show().Forget();
        }
    }
}
