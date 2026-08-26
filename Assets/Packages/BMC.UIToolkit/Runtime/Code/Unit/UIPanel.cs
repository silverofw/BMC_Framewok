using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;

namespace BMC.UIToolkit
{
    public interface IUIPanelAnimator
    {
        // 播放開啟動畫
        void PlayOpen();

        // 播放關閉動畫，並等待完成
        UniTask PlayCloseAsync(CancellationToken token);
    }

    /// <summary>
    /// UI Toolkit 版面板基底類別，對應 uGUI 版 BMC.UI 的 UIPanel。
    /// 不繼承 MonoBehaviour：面板本體是由 UXML CloneTree 產生的 VisualElement，
    /// 生命週期（建立／關閉／子面板管理）改由 UITMgr 統一驅動。
    /// </summary>
    public abstract class UIPanel
    {
        public virtual bool maskControl => false;

        public VisualElement Root { get; private set; }
        public UILayer Layer { get; private set; }
        public bool IsClosed { get; private set; }

        /// <summary>
        /// 面板是在第幾幀建立的。用來擋掉「開啟這個面板的那一次按鍵」，見
        /// UITMgr.IsJoypadInputBlocked。
        /// </summary>
        public int OpenFrame { get; private set; }

        public Action hideCallback;

        private IUIPanelAnimator animator;
        private List<UIPanel> subPanels = new List<UIPanel>();
        private CancellationTokenSource cts = new CancellationTokenSource();
        private bool isClosing;

        internal void InternalInit(VisualElement root, UILayer layer)
        {
            Root = root;
            Layer = layer;
            // 這個檔案沒有 using UnityEngine（會讓 Object 跟 System.Object 撞名），寫全名。
            OpenFrame = UnityEngine.Time.frameCount;
            animator = this as IUIPanelAnimator;

            OnInit();

            var closeBtn = Root.Q<UIButton>("close-button");
            if (closeBtn != null)
                closeBtn.OnClick += () => ClosePanel();

            var mask = Root.Q<VisualElement>("mask");
            mask?.RegisterCallback<ClickEvent>(_ => onMaskClick());

            // 手把面板一律由 UITMgr 統一管理堆疊，子類別不必自己 Push／Pop
            if (this is JoypadPanel joypadPanel)
                UITMgr.Instance.PushJoypadPanel(joypadPanel);

            animator?.PlayOpen();
        }

        /// <summary>
        /// 面板初始化時呼叫一次，子類別可在此透過 Root.Q&lt;T&gt;() 查詢元件、綁定資料。
        /// </summary>
        protected virtual void OnInit() { }

        public virtual void UnHidePanel() => Root.style.display = DisplayStyle.Flex;
        public virtual void HidePanel() => Root.style.display = DisplayStyle.None;

        /// <summary>
        /// 關閉介面：等待動畫播放完畢後，再交由 UITMgr 從畫面與清單中移除。
        /// </summary>
        public virtual async void ClosePanel(Action callback = null)
        {
            if (isClosing) return;
            isClosing = true;

            try
            {
                if (animator != null)
                    await animator.PlayCloseAsync(cts.Token);
            }
            catch (OperationCanceledException) { /* 忽略取消 */ }
            finally
            {
                UITMgr.Instance.ClosePanel(this);
                callback?.Invoke();
            }
        }

        internal void InternalClose()
        {
            hideCallback?.Invoke();
            foreach (var panel in subPanels)
            {
                if (panel != null && !panel.IsClosed)
                    UITMgr.Instance.ClosePanel(panel);
            }
            subPanels.Clear();

            if (this is JoypadPanel joypadPanel)
                UITMgr.Instance.RemoveJoypadPanel(joypadPanel);

            cts.Cancel();
            cts.Dispose();

            OnClose();

            Root?.RemoveFromHierarchy();
            IsClosed = true;
        }

        /// <summary>
        /// 面板實際被移除前呼叫，子類別可在此釋放資源、取消訂閱。
        /// </summary>
        protected virtual void OnClose() { }

        protected async UniTask<T> OpenSubPanel<T>(UILayer layer) where T : UIPanel, new()
        {
            var panel = await UITMgr.Instance.ShowPanel<T>(layer);
            subPanels.Add(panel);
            return panel;
        }

        protected async UniTask<T> OpenSubPanel<T>() where T : UIPanel, new()
        {
            var panel = await UITMgr.Instance.ShowPanel<T>(Layer);
            subPanels.Add(panel);
            return panel;
        }

        protected virtual void onMaskClick() => ClosePanel();
    }
}
