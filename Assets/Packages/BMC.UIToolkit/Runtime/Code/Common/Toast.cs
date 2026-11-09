using BMC.Core;
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版浮動提示，對應 uGUI 版 BMC.UI 的 Toast。
    /// 進退場動畫改用 USS transition（UIT_Common.uss 的 .bmc-toast 系列 class），
    /// 不依賴 DOTween，讓本套件維持與 uGUI 版相同的零外部動畫依賴。
    /// 同時觸發多則時會排隊：每則至少間隔 <see cref="QueueInterval"/> 秒才出現，避免疊在一起。
    /// </summary>
    public class Toast : UIPanel
    {
        /// <summary>進場動畫時間，需與 UIT_Common.uss 的 transition-duration 一致</summary>
        private const int ENTER_MS = 250;

        /// <summary>退場動畫時間，需與 UIT_Common.uss 的 transition-duration 一致</summary>
        private const int EXIT_MS = 400;

        /// <summary>
        /// 排隊間隔（秒，真實時間、不受 timeScale 影響）：前一則出現後，下一則至少等這麼久才出現。
        /// 設 0 則不排隊（回到舊行為：同時進來就同時出現）。
        /// </summary>
        public static float QueueInterval = 1.2f;

        /// <summary>
        /// 下一則出現時，讓還在畫面上的上一則提早淡出，避免兩則疊在同一位置。
        /// 關掉則上一則照原本停留時間播完（仍會排隊錯開出現時間）。
        /// </summary>
        public static bool DismissPreviousOnNext = true;

        /// <summary>佇列中下一則可以出現的時間點（Time.realtimeSinceStartupAsDouble）</summary>
        private static double nextShowTime;

        /// <summary>目前最新顯示的一則，用來在下一則出現時讓它提早退場</summary>
        private static Toast latest;

        // 關閉 Domain Reload 時靜態欄位會留到下次進 Play，realtimeSinceStartup 卻重新起算，
        // 不重設的話佇列時間會停在上一次 Play 的值、新提示被延後很久。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQueue()
        {
            nextShowTime = 0;
            latest = null;
        }

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
            // 排隊：在呼叫當下就決定順序與出現時間，所以同一幀進來的多則會依呼叫順序錯開
            double now = Time.realtimeSinceStartupAsDouble;
            double showAt = Math.Max(now, nextShowTime);
            nextShowTime = showAt + Math.Max(0f, QueueInterval);
            if (showAt > now)
                await UniTask.Delay(TimeSpan.FromSeconds(showAt - now), DelayType.Realtime);

            await UITMgr.Instance.EnsureRuntimeRootAsync();

            // 允許同時存在多則提示，因此關閉重複檢查
            var panel = await UITMgr.Instance.ShowPanel<Toast>(UILayer.UI_Debug, false);
            if (panel == null)
                return;

            if (DismissPreviousOnNext && latest != null && latest != panel)
                latest.DismissEarly();
            latest = panel;

            panel.Play(info, staySeconds);
        }

        private VisualElement box;
        private Label infoLabel;

        private IVisualElementScheduledItem hideItem;
        private IVisualElementScheduledItem closeItem;
        private bool timelineStarted;
        private bool dismissRequested;
        private bool exiting;

        protected override void OnInit()
        {
            // 提示訊息不該擋住底下的操作
            Root.pickingMode = PickingMode.Ignore;

            box = Root.Q<VisualElement>("toast-box");
            infoLabel = Root.Q<Label>("info");
        }

        protected override void OnClose()
        {
            if (latest == this)
                latest = null;
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

            // transition 需要「先算過一次初始樣式、之後才變更」才會播放。
            // 只靠 ExecuteLater(0) 不夠：同一幀內排程比樣式計算先跑，新複製出來的面板
            // 會在還沒有初始樣式時就被加上進場 class，結果直接跳到終點、不播動畫
            // （UXML 已快取時的第二則之後最容易遇到）。因此等第一次排版完成（此時樣式必已算過）再開始。
            EventCallback<GeometryChangedEvent> onFirstLayout = null;
            onFirstLayout = _ =>
            {
                box.UnregisterCallback(onFirstLayout);
                box.schedule.Execute(() => StartTimeline(staySeconds)).ExecuteLater(0);
            };
            box.RegisterCallback(onFirstLayout);
        }

        private void StartTimeline(float staySeconds)
        {
            timelineStarted = true;

            // 還沒開始播就被下一則擠掉：直接關，不要閃一下
            if (dismissRequested)
            {
                ClosePanel();
                return;
            }

            box.AddToClassList("bmc-toast--shown");

            int stayMs = ENTER_MS + Mathf.RoundToInt(staySeconds * 1000f);
            hideItem = box.schedule.Execute(BeginExit);
            hideItem.ExecuteLater(stayMs);
        }

        /// <summary>下一則出現了：還在畫面上的這則提早淡出（位移沿用持續上飄，不會頓）</summary>
        private void DismissEarly()
        {
            if (IsClosed || exiting)
                return;

            if (!timelineStarted)
            {
                dismissRequested = true;
                return;
            }

            hideItem?.Pause();
            BeginExit();
        }

        private void BeginExit()
        {
            if (exiting)
                return;
            exiting = true;

            box.RemoveFromClassList("bmc-toast--shown");
            box.AddToClassList("bmc-toast--hidden");

            closeItem = box.schedule.Execute(() =>
            {
                if (!IsClosed)
                    ClosePanel();
            });
            closeItem.ExecuteLater(EXIT_MS);
        }
    }
}
