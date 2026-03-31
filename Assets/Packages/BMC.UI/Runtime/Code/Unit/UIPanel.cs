using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace BMC.UI
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
    public class UIPanel : MonoBehaviour
    {
        /// <summary>
        /// 開啟介面時停用腳色控制
        /// </summary>
        public virtual bool maskControl => false;

        [SerializeField] private GameObject rootPanel;
        [SerializeField] private Button mask;
        [SerializeField] private UIButton closeBtn;
        [SerializeField] private UIPanelAnimaTool uiUtility;

        protected UICanvasType crtCanvasType = UICanvasType.SCENE_UI_1;
        private List<UIPanel> subPanels = new List<UIPanel>();

        public Action hideCallback = null;
        public bool IsHide => !rootPanel?.activeSelf ?? false;

        // 用於追蹤所有 UniTask 的 cancellation token
        private CancellationTokenSource cts = new CancellationTokenSource();
        private bool isCloseing = false;

        public void Init(UICanvasType uICanvasType = UICanvasType.SCENE_UI_1)
        {
            crtCanvasType = uICanvasType;
            Show();
        }

        protected virtual void Show()
        {
            mask?.onClick.AddListener(() => { onMaskClick(); });
            if (closeBtn)
                closeBtn.OnClick = () => { ClosePanel(); };
        }
        public virtual void UnHidePanel()
        {
            rootPanel?.SetActive(true);
        }
        public virtual void HidePanel()
        {
            rootPanel?.SetActive(false);
        }

        /// <summary>
        /// 限定 UIMgr 呼叫
        /// 同時關閉所有子panel
        /// </summary>
        public virtual void close()
        {
            hideCallback?.Invoke();
            foreach (var panel in subPanels)
            {
                if (panel != null)
                    UIMgr.Instance.closePanel(panel);
            }
            subPanels.Clear();
        }
        /// <summary>
        /// 關閉介面+動畫
        /// </summary>
        public virtual void ClosePanel(Action callBack = null)
        {
            if (isCloseing)
            {
                return;
            }
            isCloseing = true;

            if (uiUtility != null)
            {
                uiUtility.DOPlayBackwards(() => { UIMgr.Instance.closePanel(this, false, callBack); });
            }
            else
            {
                UIMgr.Instance.closePanel(this, false, callBack);
            }
        }

        protected virtual void OnDestroy()
        {
            // 取消所有 UniTask
            cts.Cancel();
            cts.Dispose();
            StopAllCoroutines();

            UIMgr.Instance.RemovePanel(this);
        }

        protected async UniTask<T> OpenSubPanel<T>(UICanvasType canvasType) where T : UIPanel
        {
            var panel = await UIMgr.Instance.ShowPanel<T>(canvasType);
            subPanels.Add(panel);
            return panel;
        }

        protected async UniTask<T> OpenSubPanel<T>() where T : UIPanel
        {
            var panel = await UIMgr.Instance.ShowPanel<T>(crtCanvasType);
            subPanels.Add(panel);
            return panel;
        }

        protected virtual void onMaskClick()
        {
            ClosePanel();
        }

        protected void delayCall(float delay, Action callback)
        {
            delayCallAsync(delay, callback).Forget();
        }

        async UniTask WaitForSecondsAsync(float seconds)
        {
            float startTime = Time.time;
            while (Time.time < startTime + seconds)
            {
                await Task.Yield(); // 每幀回到主執行緒
                cts.Token.ThrowIfCancellationRequested(); // 檢查是否被取消
            }
        }

        async UniTask delayCallAsync(float delay, Action action)
        {
            try
            {
                await WaitForSecondsAsync(delay).AttachExternalCancellation(cts.Token);
                action?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // 當被取消時不執行 action
            }
        }
    }
}
