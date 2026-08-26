using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 簡易聊天室面板。
    ///
    /// 沒有任何連線層，收到的訊息由本地模擬產生，用途是驗證 UI Toolkit 在
    /// 輸入、動態新增內容、自動捲動到底這幾件事上的行為。
    /// 放在除錯組件而非核心套件——真正要接聊天服務時，訊息來源應該由外部注入。
    /// </summary>
    public class ChatPanel : UIPanel
    {
        public override bool maskControl => true;

        /// <summary>模擬對方回覆的延遲秒數</summary>
        private const float REPLY_DELAY = 1.2f;

        private ScrollView messageScroll;
        private TextField inputField;

        public static async UniTask<ChatPanel> Show(UILayer layer = UILayer.UI_Top)
        {
            await UITMgr.Instance.EnsureRuntimeRootAsync();
            return await UITMgr.Instance.ShowPanel<ChatPanel>(layer);
        }

        protected override void OnInit()
        {
            var box = Root.Q<VisualElement>("box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            messageScroll = Root.Q<ScrollView>("messages");
            inputField = Root.Q<TextField>("input");

            var sendButton = Root.Q<UIButton>("btn-send");
            if (sendButton != null)
                sendButton.OnClick += Send;

            var clearButton = Root.Q<UIButton>("btn-clear");
            if (clearButton != null)
                clearButton.OnClick += Clear;

            // Enter 送出。TextField 不會自己處理 Enter，要自行攔 KeyDown。
            inputField?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;

                Send();

                // 攔下事件，避免換行字元被寫進輸入框
                evt.StopPropagation();
            });

            AppendSystem("輸入訊息後按 Enter 或「送出」。對方會在一秒後自動回覆。");
        }

        private void Send()
        {
            string text = inputField?.value?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            AppendMessage("我", text, true);

            inputField.value = string.Empty;
            inputField.Focus();

            ReplyAsync(text).Forget();
        }

        /// <summary>
        /// 模擬對方回覆。面板關閉後 Root 會被移出畫面，
        /// 這時再往上加訊息沒有意義，因此先檢查 IsClosed。
        /// </summary>
        private async UniTaskVoid ReplyAsync(string origin)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(REPLY_DELAY));

            if (IsClosed)
                return;

            AppendMessage("對方", $"收到：{origin}", false);
        }

        private void Clear()
        {
            messageScroll?.Clear();
            AppendSystem("已清空訊息。");
        }

        private void AppendSystem(string text) => AppendLine(null, text, "bmc-chat-line--system");

        private void AppendMessage(string sender, string text, bool isSelf)
        {
            string meta = $"{sender}  {DateTime.Now:HH:mm:ss}";
            AppendLine(meta, text, isSelf ? "bmc-chat-line--self" : null);
        }

        private void AppendLine(string meta, string text, string modifierClass)
        {
            if (messageScroll == null)
                return;

            var line = new VisualElement();
            line.AddToClassList("bmc-chat-line");
            if (!string.IsNullOrEmpty(modifierClass))
                line.AddToClassList(modifierClass);

            if (!string.IsNullOrEmpty(meta))
            {
                var metaLabel = new Label(meta);
                metaLabel.AddToClassList("bmc-chat-meta");
                line.Add(metaLabel);
            }

            var bubble = new VisualElement();
            bubble.AddToClassList("bmc-chat-bubble");

            var textLabel = new Label(text);
            textLabel.AddToClassList("bmc-chat-bubble__text");
            bubble.Add(textLabel);

            line.Add(bubble);
            messageScroll.Add(line);

            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (messageScroll == null)
                return;

            // 新元素這一幀還沒排版，contentContainer 的高度仍是舊值，
            // 直接捲會停在倒數第二則。延後一幀等版面算完再捲。
            messageScroll.schedule.Execute(() =>
            {
                var content = messageScroll.contentContainer;
                if (content == null)
                    return;

                messageScroll.scrollOffset = new Vector2(0f, content.layout.height);
            }).ExecuteLater(0);
        }
    }
}
