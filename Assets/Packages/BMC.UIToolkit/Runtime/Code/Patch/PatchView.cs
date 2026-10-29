using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 熱更新畫面（Patch 場景）的 UI Toolkit 介面：標題、逐格動畫、進度條、步驟提示、版號與訊息框。
    /// 這時熱更 DLL 與 YooAsset 資源都還沒載入，所以不走 UITMgr，直接掛在場景物件的 UIDocument 上，
    /// 版面用套件的 Runtime/Patch/UIT_PatchView.uxml 與 PatchPanelSettings，文字一律從 PatchLocal 取。
    ///
    /// 套件版面只有素色外觀、不指定字型。專案的美術（背景、進度條、訊息框圖、字型、動畫格）
    /// 寫在自己的 USS 拖進 <see cref="skin"/>：套用時會在 cover 加上 patch-skin，
    /// 換膚 USS 的選擇器寫成「.patch-skin.patch-cover」「.patch-skin .patch-bar」，權重就一定蓋得過套件預設。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PatchView : MonoBehaviour
    {
        [Tooltip("專案的換膚 USS（圖片、字型、動畫格）")]
        [SerializeField] private StyleSheet skin;

        [Header("逐格動畫（每格的圖寫在換膚 USS 的「{前綴}1」…「{前綴}{格數}」）")]
        [SerializeField] private string animClassPrefix = "patch-anim--f";
        [Tooltip("0 表示不播動畫，動畫區也會隱藏")]
        [SerializeField] private int animFrameCount = 0;
        [SerializeField] private int animFrameMs = 150;

        private VisualElement _root;
        private Label _title;
        private VisualElement _anim;
        private VisualElement _barFill;
        private Label _tips;
        private Label _version;
        private VisualElement _msgLayer;
        private int _animFrame;

        void Start()
        {
            Bind();
        }

        /// <summary>
        /// UIDocument 要等它自己 OnEnable 之後才有 rootVisualElement，所以用到時才去找元素
        /// </summary>
        private bool Bind()
        {
            if (_root != null)
                return true;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return false;
            _root = root;
            _title = root.Q<Label>("title");
            _anim = root.Q("anim");
            _barFill = root.Q("bar-fill");
            _tips = root.Q<Label>("tip");
            _version = root.Q<Label>("version");
            _msgLayer = root.Q("msg-layer");

            if (skin != null)
            {
                root.styleSheets.Add(skin);
                root.Q("cover").AddToClassList("patch-skin");
            }

            _title.text = PatchLocal.Get("PATCH_TITLE");
            _version.text = Application.version;
            SetProgress(0f);
            StartAnim();
            return true;
        }

        private void StartAnim()
        {
            if (animFrameCount <= 0)
            {
                _anim.style.display = DisplayStyle.None;
                return;
            }
            SetAnimFrame(0);
            _anim.schedule.Execute(() => SetAnimFrame((_animFrame + 1) % animFrameCount)).Every(Math.Max(1, animFrameMs));
        }

        private void SetAnimFrame(int frame)
        {
            _anim.RemoveFromClassList(animClassPrefix + (_animFrame + 1));
            _animFrame = frame;
            _anim.AddToClassList(animClassPrefix + (_animFrame + 1));
        }

        public void SetTitle(string text)
        {
            if (Bind())
                _title.text = text;
        }

        /// <summary>
        /// 進度條下方的步驟／下載進度文字（已經翻好的字）
        /// </summary>
        public void SetTips(string text)
        {
            if (Bind())
                _tips.text = text;
        }

        /// <summary>
        /// 0～1
        /// </summary>
        public void SetProgress(float value)
        {
            if (Bind())
                _barFill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }

        public void SetVersion(string text)
        {
            if (Bind())
                _version.text = text;
        }

        /// <summary>
        /// 显示对话框（按鈕顯示「確認」）。content 是已經翻好的文字
        /// </summary>
        public void ShowMessageBox(string content, Action ok)
        {
            ShowMessageBox(content, PatchLocal.Get("PATCH_BTN_OK"), ok);
        }

        /// <summary>
        /// 显示对话框。可同時開好幾個，後開的疊在上面；按下按鈕後關閉並執行 ok
        /// </summary>
        public void ShowMessageBox(string content, string okText, Action ok)
        {
            if (!Bind())
                return;

            var mask = new VisualElement();
            mask.AddToClassList("patch-msg-mask");
            var box = new VisualElement();
            box.AddToClassList("patch-msg-box");
            mask.Add(box);

            var title = new Label(PatchLocal.Get("PATCH_MSG_TITLE"));
            title.AddToClassList("patch-msg-title");
            box.Add(title);

            var text = new Label(content);
            text.AddToClassList("patch-msg-text");
            box.Add(text);

            var row = new VisualElement();
            row.AddToClassList("patch-msg-button-row");
            box.Add(row);

            var btn = new Button { text = okText };
            btn.AddToClassList("patch-msg-button");
            btn.clicked += () =>
            {
                mask.RemoveFromHierarchy();
                ok?.Invoke();
            };
            row.Add(btn);

            _msgLayer.Add(mask);
            // 讓手把／鍵盤的確認鍵直接按得到
            btn.schedule.Execute(() => btn.Focus());
        }
    }
}
