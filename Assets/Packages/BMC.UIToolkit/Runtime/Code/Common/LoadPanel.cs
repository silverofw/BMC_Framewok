using BMC.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版讀取畫面，對應 uGUI 版 BMC.UI 的 LoadPanel。
    ///
    /// 對外 API（Show／SetProgress／SetMaxProgress／Instance）與 uGUI 版一致，
    /// 呼叫端換套件不用改程式。內部有兩處必然的差異：
    /// 　1. 進退場沒有 UIEffectTweener，改用 USS transition 做整片遮罩的淡入淡出，
    /// 　　 維持本套件不相依 DOTween／UIEffect 的原則。
    /// 　2. 面板不是 MonoBehaviour，沒有 Update，平滑進度改掛在
    /// 　　 VisualElement 的 scheduler 上（每幀執行一次）。
    /// </summary>
    public class LoadPanel : UIPanel
    {
        public static LoadPanel Instance { get; private set; }

        /// <summary>讀取畫面蓋住整個螢幕，期間底下的手把面板不該再收到輸入</summary>
        public override bool maskControl => true;

        private const int ProgressMax = 100;

        /// <summary>進退場動畫秒數，需與 UIT_Common.uss 的 transition-duration 一致</summary>
        private const float ANIMATION_TIME = 0.5f;

        /// <summary>每秒最多跑幾 %：與 uGUI 版的 progressSpeed 預設值相同</summary>
        private const float PROGRESS_SPEED = 50f;

        private static bool AutoFinish;

        /// <summary>
        /// 顯示讀取畫面。
        /// </summary>
        /// <param name="startAction">遮罩全畫面後呼叫</param>
        /// <param name="finishAction">全部執行完並開啟畫面後呼叫</param>
        /// <param name="autoFinish">呼叫 startAction 完畢自動跑滿進度並關閉遮罩</param>
        public static void Show(Action startAction, Action finishAction = null, bool autoFinish = false)
        {
            Log.Info("[LoadPanel] Request Show");
            AutoFinish = autoFinish;
            ShowInternal(startAction, finishAction).Forget();
        }

        private static async UniTaskVoid ShowInternal(Action startAction, Action finishAction)
        {
            await UITMgr.Instance.EnsureRuntimeRootAsync();

            // 與 uGUI 版相同：不做重複檢查，重複呼叫視為重新讀取
            var panel = await UITMgr.Instance.ShowPanel<LoadPanel>(UILayer.UI_4, false);
            panel?.Setup(startAction, finishAction);
        }

        private VisualElement cover;
        private VisualElement barRoot;
        private VisualElement barFill;
        private Label progressLabel;

        private Action onStartAction;
        private Action onFinishCallback;

        private float startTime;
        private float lastProgressTime;
        private readonly List<(float duration, string tip)> progressRecords = new();

        private float visualProgress;  // 用於平滑顯示的進度
        private int targetProgress;    // 目標進度 (0-100)
        private bool isLoading;

        /// <summary>平滑進度的每幀更新，對應 uGUI 版的 Update</summary>
        private IVisualElementScheduledItem ticker;

        protected override void OnInit()
        {
            Instance = this;

            // 讀取畫面要蓋住整個畫面，底下的操作一律擋掉
            Root.pickingMode = PickingMode.Position;

            cover = Root.Q<VisualElement>("cover");
            barRoot = Root.Q<VisualElement>("bar-root");
            barFill = Root.Q<VisualElement>("bar-fill");
            progressLabel = Root.Q<Label>("progress");
        }

        protected override void OnClose()
        {
            ticker?.Pause();
            ticker = null;

            if (Instance == this)
                Instance = null;
        }

        public void Setup(Action startAction, Action finishAction)
        {
            onStartAction = startAction;
            onFinishCallback = finishAction;
            ResetState();

            PerformShowSequence().Forget();
        }

        private void ResetState()
        {
            visualProgress = 0f;
            targetProgress = 0;
            isLoading = false;
            progressRecords.Clear();

            UpdateUI(0f);
            SetBarVisible(false);
        }

        private async UniTaskVoid PerformShowSequence()
        {
            // USS transition 需要「先有初始值、下一幀才變更」才會播放。
            // CloneTree 當下就把 --shown 加上的話，opacity 會直接跳到 1，淡入看不見。
            await UniTask.Yield(PlayerLoopTiming.Update);
            if (IsClosed)
                return;

            cover?.AddToClassList("bmc-load-cover--shown");

            await UniTask.WaitForSeconds(ANIMATION_TIME, ignoreTimeScale: true);
            if (IsClosed)
                return;

            SetBarVisible(true);
            startTime = Time.realtimeSinceStartup;
            lastProgressTime = startTime;
            isLoading = true;

            // 面板不是 MonoBehaviour，改用 scheduler 每幀推進進度
            ticker = Root.schedule.Execute(Tick).Every(0);

            Log.Info($"[{startTime}] Loading START");
            SetProgress(0, "Loading START");

            onStartAction?.Invoke();

            if (AutoFinish)
                targetProgress = ProgressMax;
        }

        private void Tick()
        {
            if (!isLoading)
                return;

            // 平滑插值進度條
            if (visualProgress < targetProgress)
            {
                visualProgress = Mathf.MoveTowards(visualProgress, targetProgress, Time.unscaledDeltaTime * PROGRESS_SPEED);
                UpdateUI(visualProgress);
            }

            // 完成判斷：當視覺進度達到 100 且目標也是 100
            if (visualProgress >= ProgressMax && targetProgress >= ProgressMax)
            {
                isLoading = false;
                ticker?.Pause();
                PerformHideSequence().Forget();
            }
        }

        private void UpdateUI(float val)
        {
            int displayVal = Mathf.FloorToInt(val);

            if (barFill != null)
                barFill.style.width = Length.Percent(Mathf.Clamp(val, 0f, ProgressMax));

            if (progressLabel != null)
                progressLabel.text = displayVal.ToString();
        }

        private void SetBarVisible(bool visible)
        {
            if (barRoot != null)
                barRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetProgress(int progress, string tip)
        {
            RecordTip(tip);
            targetProgress = Mathf.Clamp(progress, 0, ProgressMax);
        }

        /// <summary>
        /// 呼叫加載完成
        /// </summary>
        public void SetMaxProgress(string tip)
        {
            RecordTip(tip);
            targetProgress = ProgressMax;
        }

        private void RecordTip(string tip)
        {
            float now = Time.realtimeSinceStartup;
            progressRecords.Add((now - lastProgressTime, tip));
            lastProgressTime = now;
        }

        private async UniTaskVoid PerformHideSequence()
        {
            Log.Info("[LoadPanel] Reached 100%, generating report...");
            PrintReport();

            SetBarVisible(false);
            cover?.RemoveFromClassList("bmc-load-cover--shown");

            await UniTask.WaitForSeconds(ANIMATION_TIME, ignoreTimeScale: true);
            if (IsClosed)
                return;

            onFinishCallback?.Invoke();
            ClosePanel();
        }

        private void PrintReport()
        {
            var sb = new StringBuilder();
            float totalTime = Time.realtimeSinceStartup - startTime;

            sb.AppendLine("===== LOAD REPORT =====");
            foreach (var record in progressRecords)
            {
                float percent = totalTime > 0f ? (record.duration / totalTime) * 100f : 0f;
                sb.AppendLine($"[{percent:0.00}%][{record.duration:F3}s] {record.tip}");
            }
            sb.AppendLine($"Total Time: {totalTime:F3}s");

            Log.Info(sb.ToString());
        }
    }
}
