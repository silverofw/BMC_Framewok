using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.UI
{
    public interface IUIPanelAnimator
    {
        // 播放開啟動畫
        void PlayOpen();

        // 播放關閉動畫，並等待完成
        UniTask PlayCloseAsync(CancellationToken token);
    }

    public class UIPanel : MonoBehaviour
    {
        public virtual bool maskControl => false;

        [SerializeField] private GameObject rootPanel;
        [SerializeField] private Button mask;
        [SerializeField] private UIButton closeBtn;

        // 解耦核心：改用介面
        private IUIPanelAnimator _animator;

        protected UICanvasType crtCanvasType = UICanvasType.SCENE_UI_1;
        private List<UIPanel> subPanels = new List<UIPanel>();

        public Action hideCallback = null;
        private CancellationTokenSource cts = new CancellationTokenSource();
        private bool isCloseing = false;

        public void Init(UICanvasType uICanvasType = UICanvasType.SCENE_UI_1)
        {
            crtCanvasType = uICanvasType;
            // 初始化時取得動畫組件
            _animator = GetComponent<IUIPanelAnimator>();
            Show();
        }

        protected virtual void Show()
        {
            _animator?.PlayOpen(); // 執行開啟動畫

            mask?.onClick.AddListener(onMaskClick);
            if (closeBtn)
                closeBtn.OnClick = () => { ClosePanel(); };
        }

        public virtual void UnHidePanel() => rootPanel?.SetActive(true);
        public virtual void HidePanel() => rootPanel?.SetActive(false);

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
        /// 關閉介面：現在使用 async 流程處理動畫等待
        /// </summary>
        public virtual async void ClosePanel(Action callBack = null)
        {
            if (isCloseing) return;
            isCloseing = true;

            try
            {
                if (_animator != null)
                {
                    // 等待動畫播放完畢
                    await _animator.PlayCloseAsync(cts.Token);
                }
            }
            catch (OperationCanceledException) { /* 忽略取消 */ }
            finally
            {
                UIMgr.Instance.closePanel(this, false, callBack);
            }
        }

        protected virtual void OnDestroy()
        {
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

        protected virtual void onMaskClick() => ClosePanel();
    }
}