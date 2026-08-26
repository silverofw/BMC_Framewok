using BMC.Core;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版浮動提示，對應 uGUI 版 BMC.UI 的 Toast。
    /// 進退場動畫改用 USS transition（UIT_Common.uss 的 .bmc-toast 系列 class），
    /// 不依賴 DOTween，讓本套件維持與 uGUI 版相同的零外部動畫依賴。
    /// </summary>
    public class Toast : UIPanel
    {
        /// <summary>進場動畫時間，需與 UIT_Common.uss 的 transition-duration 一致</summary>
        private const int ENTER_MS = 250;

        /// <summary>退場動畫時間，需與 UIT_Common.uss 的 transition-duration 一致</summary>
        private const int EXIT_MS = 400;

        // 參數名稱與預設值刻意比照 uGUI 版 BMC.UI.Toast，讓兩套 UI 能直接互換：
        // 呼叫端即使用具名參數（delayFrame:）也不會因為換套件而編譯失敗。
        // 註：delayFrame 實際單位是「秒」，這個命名沿用自 uGUI 版，未在此更正以維持一致。
        public static void Show(string info, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {info}");
            ShowInternal(info, delayFrame).Forget();
        }

        public static void Local(string key, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {key}");
            ShowInternal(LocalMgr.Instance.Local(key), delayFrame).Forget();
        }

        public static void LocalFormat(string key, params object[] args)
        {
            Log.Info($"[Toast] {key}");
            ShowInternal(LocalMgr.Instance.LocalFormat(key, args), 2.5f).Forget();
        }

        private static async UniTaskVoid ShowInternal(string info, float staySeconds)
        {
            await UITMgr.Instance.EnsureRuntimeRootAsync();

            // 允許同時存在多則提示，因此關閉重複檢查
            var panel = await UITMgr.Instance.ShowPanel<Toast>(UILayer.UI_Debug, false);
            panel?.Play(info, staySeconds);
        }

        private VisualElement box;
        private Label infoLabel;

        protected override void OnInit()
        {
            // 提示訊息不該擋住底下的操作
            Root.pickingMode = PickingMode.Ignore;

            box = Root.Q<VisualElement>("toast-box");
            infoLabel = Root.Q<Label>("info");
        }

        private void Play(string info, float staySeconds)
        {
            if (infoLabel != null)
                infoLabel.text = info;

            if (box == null)
            {
                ClosePanel();
                return;
            }

            // transition 需要「先有初始值、下一幀才變更」才會播放，
            // 因此進場 class 延到下一幀再加。
            box.schedule.Execute(() => box.AddToClassList("bmc-toast--shown")).ExecuteLater(0);

            int stayMs = ENTER_MS + UnityEngine.Mathf.RoundToInt(staySeconds * 1000f);
            box.schedule.Execute(() =>
            {
                box.RemoveFromClassList("bmc-toast--shown");
                box.AddToClassList("bmc-toast--hidden");
            }).ExecuteLater(stayMs);

            box.schedule.Execute(() => ClosePanel()).ExecuteLater(stayMs + EXIT_MS);
        }
    }
}
