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
        public static void Show(string info, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {info}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(info, delayFrame);
            }).Forget();
        }

        public static void Local(string key, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {key}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(LocalMgr.Instance.Local(key), delayFrame);
            }).Forget();
        }

        public static void LocalFormat(string key, params object[] args)
        {
            Log.Info($"[Toast] {key}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(LocalMgr.Instance.LocalFormat(key, args), 3f);
            }).Forget();
        }

        [SerializeField] private UIText text;
        [SerializeField] private RectTransform contentRect;
        [SerializeField] private CanvasGroup canvasGroup;

        private Vector2 _originPos;
        private bool _isInitialized = false;

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

                // 2. 停留一段時間
                await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct);

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
                // 無論如何最終都要關閉介面
                ClosePanel();
            }
        }
    }
}