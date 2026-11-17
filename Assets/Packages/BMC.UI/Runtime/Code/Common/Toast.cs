using BMC.Core;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace BMC.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class Toast : UIPanel
    {
        /// <summary>
        /// 排隊間隔（秒，真實時間、不受 timeScale 影響）：同時觸發多則時，前一則出現後下一則至少等這麼久才出現。
        /// 設 0 則不排隊（回到舊行為：同時進來就同時出現、疊在一起）。
        /// </summary>
        public static float QueueInterval = 1.2f;

        /// <summary>下一則出現時，讓還在畫面上的上一則提早上飄淡出，避免兩則疊在同一位置。</summary>
        public static bool DismissPreviousOnNext = true;

        private static double nextShowTime;
        private static Toast latest;

        // 關閉 Domain Reload 時靜態欄位會留到下次進 Play，realtimeSinceStartup 卻重新起算，要重設
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQueue()
        {
            nextShowTime = 0;
            latest = null;
        }

        public static void Show(string info, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {info}");
            ShowQueued(() => info, delayFrame).Forget();
        }

        public static void Local(string key, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {key}");
            ShowQueued(() => LocalMgr.Instance.Local(key), delayFrame).Forget();
        }

        public static void LocalFormat(string key, params object[] args)
        {
            Log.Info($"[Toast] {key}");
            ShowQueued(() => LocalMgr.Instance.LocalFormat(key, args), 3f).Forget();
        }

        private static async UniTaskVoid ShowQueued(Func<string> getInfo, float delay)
        {
            // 在呼叫當下就排好出現時間，同一幀進來的多則會依呼叫順序錯開
            double now = Time.realtimeSinceStartupAsDouble;
            double showAt = Math.Max(now, nextShowTime);
            nextShowTime = showAt + Math.Max(0f, QueueInterval);
            if (showAt > now)
                await UniTask.Delay(TimeSpan.FromSeconds(showAt - now), DelayType.Realtime);

            var p = await UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false);
            if (p == null)
                return;

            if (DismissPreviousOnNext && latest != null && latest != p)
                latest.DismissEarly();
            latest = p;

            p.Init(getInfo(), delay);
        }

        [SerializeField] private UIText text;
        [SerializeField] private RectTransform contentRect;
        [SerializeField] private CanvasGroup canvasGroup;

        private Vector2 _originPos;
        private bool _isInitialized = false;

        /// <summary>取消「停留」階段，讓這則直接進入上飄淡出</summary>
        private CancellationTokenSource _stayCts;

        private bool _dismissRequested;

        private void DismissEarly()
        {
            if (this == null)
                return;
            _dismissRequested = true;   // 還在彈出動畫時被擠掉：彈完直接退場
            _stayCts?.Cancel();
        }

        private void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (contentRect == null) contentRect = transform as RectTransform;
            _originPos = contentRect.anchoredPosition;
            _isInitialized = true;
        }

        public void Init(string info, float delay = 2.5f)
        {
            if (!_isInitialized) Awake();

            // 1. 初始狀態重置 (Pop-in 準備)
            contentRect.localScale = Vector3.zero;
            canvasGroup.alpha = 1f;
            contentRect.anchoredPosition = _originPos;

            text.Set(info);

            // 調用 async UniTaskVoid 方法，不會產生 CS4014 警告
            PlayToastSequence(delay).Forget();
        }

        /// <summary>
        /// 執行 Toast 動畫：立即彈出 -> 停留 -> 上飄淡出
        /// 使用 ToUniTask 配合 CancellationToken 實作
        /// </summary>
        private async UniTaskVoid PlayToastSequence(float delay)
        {
            var ct = this.GetCancellationTokenOnDestroy();

            try
            {
                // 1. 彈出動畫 (Pop-in)
                // 使用 ToUniTask 並帶入取消權杖
                await contentRect.DOScale(1f, 0.3f)
                    .SetEase(Ease.OutBack)
                    .SetLink(gameObject)
                    .ToUniTask(cancellationToken: ct);

                // 2. 停留一段時間（下一則出現時會被提早結束，直接進入退場）
                _stayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                try
                {
                    if (!_dismissRequested)
                        await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: _stayCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 被下一則擠掉：不中斷，繼續播退場
                }
                finally
                {
                    _stayCts.Dispose();
                    _stayCts = null;
                }

                // 3. 結束動畫：向上飄移 + 透明淡出
                float fadeDuration = 0.5f;
                float moveDistance = 80f;

                // 使用 UniTask.WhenAll 搭配 ToUniTask 同時等待多個動畫完成，不再使用 Sequence
                await UniTask.WhenAll(
                    contentRect.DOAnchorPosY(_originPos.y + moveDistance, fadeDuration)
                        .SetEase(Ease.OutCubic)
                        .SetLink(gameObject)
                        .ToUniTask(cancellationToken: ct),

                    canvasGroup.DOFade(0f, fadeDuration)
                        .SetEase(Ease.InQuad)
                        .SetLink(gameObject)
                        .ToUniTask(cancellationToken: ct)
                );
            }
            catch (OperationCanceledException)
            {
                // 正常取消處理，不拋出錯誤
            }
            catch (Exception ex)
            {
                Log.Error($"[Toast] Animation error: {ex.Message}");
            }
            finally
            {
                if (latest == this)
                    latest = null;

                // 無論如何最終都要關閉介面
                ClosePanel();
            }
        }
    }
}