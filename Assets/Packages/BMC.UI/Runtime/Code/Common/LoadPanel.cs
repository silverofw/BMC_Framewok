using Coffee.UIEffects;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.UI
{
    public class LoadPanel : UIPanel
    {
        public static LoadPanel Instance { get; private set; }

        [Header("UI Components")]
        [SerializeField] private UIEffectTweener transitionEffect;
        [SerializeField] private Image barImage;
        [SerializeField] private UIText progressText;
        [SerializeField] private GameObject barRoot;

        [Header("Settings")]
        [SerializeField] private float animationTime = 0.5f;

        private const int ProgressMax = 100;

        private Action _onStartAction;
        private Action _onFinishCallback;

        private float _startTime;
        private float _lastProgressTime;
        private readonly List<(float duration, string tip)> _progressRecords = new();

        private float _visualProgress; // 用於平滑顯示的進度
        private int _targetProgress;   // 目標進度 (0-100)
        private bool _isLoading;

        private static bool AutoFinish;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="startAction">遮罩全畫面後呼叫</param>
        /// <param name="finishAction">全部執行完立後開啟呼叫</param>
        /// <param name="autoFinish">呼叫startAction完畢自動呼叫關閉遮罩動畫</param>
        public static void Show(Action startAction, Action finishAction = null, bool autoFinish = false)
        {
            Log.Info("[LoadPanel] Request Show");
            AutoFinish = autoFinish;
            UIMgr.Instance.ShowPanel<LoadPanel>(UICanvasType.UI_4, false).ContinueWith(p =>
            {
                p.Setup(startAction, finishAction);
            }).Forget();
        }

        void Awake()
        {
            Instance = this;
        }

        protected override void OnDestroy()
        {
            Instance = null;
            base.OnDestroy();
        }

        public void Setup(Action startAction, Action finishAction)
        {
            _onStartAction = startAction;
            _onFinishCallback = finishAction;
            ResetState();
        }

        private void ResetState()
        {
            _visualProgress = 0;
            _targetProgress = 0;
            _isLoading = false;
            _progressRecords.Clear();
            barImage.fillAmount = 0;
            progressText.Set("0");
        }

        protected override void Show()
        {
            base.Show();
            PerformShowSequence().Forget();
        }

        private async UniTaskVoid PerformShowSequence()
        {
            transitionEffect.PlayReverse(true);
            barRoot.SetActive(false);

            await UniTask.WaitForSeconds(animationTime, delayTiming: PlayerLoopTiming.Update, cancellationToken: this.GetCancellationTokenOnDestroy());

            barRoot.SetActive(true);
            _startTime = Time.realtimeSinceStartup;
            _lastProgressTime = _startTime;
            _isLoading = true;

            Log.Info($"[{_startTime}] Loading START");
            SetProgress(0, "Loading START");

            _onStartAction?.Invoke();

            _visualProgress = AutoFinish? ProgressMax:_visualProgress;
        }

        private void Update()
        {
            if (!_isLoading) return;

            // 平滑插值進度條
            if (_visualProgress < _targetProgress)
            {
                _visualProgress = Mathf.MoveTowards(_visualProgress, _targetProgress, Time.deltaTime * 50f); // 每秒最多跑50%
                UpdateUI(_visualProgress);
            }

            // 完成判斷：當視覺進度達到 100 且目標也是 100
            if (_visualProgress >= ProgressMax && _targetProgress >= ProgressMax)
            {
                _isLoading = false;
                PerformHideSequence().Forget();
            }
        }

        private void UpdateUI(float val)
        {
            int displayVal = Mathf.FloorToInt(val);
            barImage.fillAmount = val / ProgressMax;
            progressText.Set(displayVal.ToString());
        }

        public void SetProgress(int progress, string tip)
        {
            float now = Time.realtimeSinceStartup;
            _progressRecords.Add((now - _lastProgressTime, tip));
            _lastProgressTime = now;

            _targetProgress = Mathf.Clamp(progress, 0, ProgressMax);
        }

        /// <summary>
        /// 呼叫加載完成
        /// </summary>
        /// <param name="tip"></param>
        public void SetMaxProgress(string tip)
        {
            float now = Time.realtimeSinceStartup;
            _progressRecords.Add((now - _lastProgressTime, tip));
            _lastProgressTime = now;

            _targetProgress = Mathf.Clamp(ProgressMax, 0, ProgressMax);
        }

        private async UniTaskVoid PerformHideSequence()
        {
            Log.Info("[LoadPanel] Reached 100%, generating report...");
            PrintReport();

            barRoot.SetActive(false);
            transitionEffect.PlayForward(true);

            await UniTask.WaitForSeconds(animationTime, delayTiming: PlayerLoopTiming.Update, cancellationToken: this.GetCancellationTokenOnDestroy());

            _onFinishCallback?.Invoke();
            ClosePanel();
        }

        private void PrintReport()
        {
            StringBuilder sb = new StringBuilder();
            float totalTime = Time.realtimeSinceStartup - _startTime;

            sb.AppendLine("===== LOAD REPORT =====");
            foreach (var record in _progressRecords)
            {
                float percent = (record.duration / totalTime) * 100;
                sb.AppendLine($"[{percent:0.00}%][{record.duration:F3}s] {record.tip}");
            }
            sb.AppendLine($"Total Time: {totalTime:F3}s");

            Log.Info(sb.ToString());
        }
    }
}