using BMC.UIToolkit;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BMC.Story
{
    // 供外部傳入的章節資料結構
    public class ChapterData
    {
        public string ChapterId;
        public string ChapterName;
        // 參數化：由外部定義如何取得該章節的 byte[] (例如從 Addressables 或記憶體快取)
        public Func<byte[]> GetChapterBytes;
    }

    /// <summary>
    /// UI Toolkit 版章節/節點地圖面板，對應 uGUI 版的 StoryLinePanel。
    /// 本身是薄殼(章節切換 UI)，實際的節點圖佈局/連線都委派給內部的 StoryGraphView——
    /// 消費端若要客製整個節點地圖畫面(例如遊戲端的 EFMStoryLinePanel)，直接用「組合」的方式
    /// 自己包一個 StoryGraphView，不需要繼承或整份改寫本類別。
    /// </summary>
    public class StoryLinePanel : UIPanel
    {
        protected StoryGraphView GraphView { get; private set; }

        private UILabel chapterText;
        private UIButton chapterBtn;
        private UIButton chapterCloseBtn;
        private VisualElement chapterRoot;
        private VisualElement chapterListRoot;

        private readonly List<ChapterData> chapters = new List<ChapterData>();
        private bool isInitChapter;

        // 追蹤當前預覽與實際遊玩的章節狀態
        private string activePlayingChapterId;          // StoryPlayer 正在跑的章節 ID
        private ChapterData currentlyDisplayingChapter;  // UI 畫面上正在預覽的章節

        protected override void OnInit()
        {
            var graphContainer = Root.Q<VisualElement>("graph-view-container");
            GraphView = new StoryGraphView { name = "graph-view" };
            graphContainer?.Add(GraphView);
            GraphView.NodeClicked += HandleNodeClicked;

            chapterText = Root.Q<UILabel>("chapter-text");
            chapterBtn = Root.Q<UIButton>("chapter-button");
            chapterCloseBtn = Root.Q<UIButton>("chapter-close-button");
            chapterRoot = Root.Q<VisualElement>("chapter-root");
            chapterListRoot = Root.Q<VisualElement>("chapter-list-root");

            if (chapterRoot != null)
                chapterRoot.style.display = DisplayStyle.None;

            if (chapterCloseBtn != null)
                chapterCloseBtn.OnClick += CloseChapterList;

            StoryPlayer.Instance.Pause();
        }

        protected override void OnClose()
        {
            StoryPlayer.Instance.Play();
        }

        /// <summary>
        /// 外部呼叫入口：初始化面板與章節清單
        /// </summary>
        /// <param name="chapterList">所有可選的章節清單</param>
        /// <param name="currentChapterId">StoryPlayer 目前正在遊玩的章節 ID</param>
        public void InitializePanel(List<ChapterData> chapterList, string currentChapterId)
        {
            activePlayingChapterId = currentChapterId;

            chapters.Clear();
            ChapterData startChapter = null;
            foreach (var chapter in chapterList)
            {
                if (chapter.GetChapterBytes == null)
                    continue;

                chapters.Add(chapter);
                if (chapter.ChapterId == currentChapterId)
                    startChapter = chapter;
            }

            isInitChapter = false;
            if (chapterBtn != null)
            {
                chapterBtn.OnClick -= OpenChapterList;
                chapterBtn.OnClick += OpenChapterList;
            }

            // 預設顯示當前正在遊玩的章節 (若找不到就顯示第一個)
            if (startChapter != null)
                PreviewChapter(startChapter).Forget();
            else if (chapters.Count > 0)
                PreviewChapter(chapters[0]).Forget();
        }

        private void OpenChapterList()
        {
            if (chapterRoot != null)
                chapterRoot.style.display = DisplayStyle.Flex;

            if (isInitChapter || chapterListRoot == null)
                return;
            isInitChapter = true;

            chapterListRoot.Clear();
            foreach (var chapter in chapters)
            {
                var btn = new UIButton { text = chapter.ChapterName };
                btn.OnClick += () =>
                {
                    PreviewChapter(chapter).Forget();
                    CloseChapterList();
                };
                chapterListRoot.Add(btn);
            }
        }

        private void CloseChapterList()
        {
            if (chapterRoot != null)
                chapterRoot.style.display = DisplayStyle.None;
        }

        public UniTask PreviewChapter(int index)
        {
            if (index < 0 || index >= chapters.Count)
                return UniTask.CompletedTask;
            return PreviewChapter(chapters[index]);
        }

        /// <summary>
        /// 預覽章節：只更新 UI，不影響 StoryPlayer 內部資料
        /// </summary>
        private async UniTask PreviewChapter(ChapterData chapter)
        {
            currentlyDisplayingChapter = chapter;
            byte[] bytes = chapter.GetChapterBytes?.Invoke();
            if (bytes == null)
                return;

            chapterText?.Set(chapter.ChapterName);

            StoryPackage tempPackage = StoryPackage.Parser.ParseFrom(bytes);
            await RefreshStoryLayout(tempPackage.Nodes[0], tempPackage);
        }

        public UniTask RefreshStoryLayout(StoryNode startNode, StoryPackage package) =>
            GraphView.RefreshStoryLayout(startNode, package);

        public UniTask ScrollToNode(StoryNode targetNode) => GraphView.ScrollToNode(targetNode);

        /// <summary>
        /// 處理玩家確認點擊某個節點的行為
        /// </summary>
        private void HandleNodeClicked(StoryNode node)
        {
            // 檢查玩家點擊的節點，是否屬於另一個尚未載入 StoryPlayer 的章節
            if (currentlyDisplayingChapter != null && currentlyDisplayingChapter.ChapterId != activePlayingChapterId)
            {
                // 若是不同章節，才呼叫 StoryPlayer.LoadStory 進行實際資料替換
                byte[] chapterBytes = currentlyDisplayingChapter.GetChapterBytes?.Invoke();
                if (chapterBytes != null)
                {
                    StoryPlayer.Instance.LoadStory(chapterBytes);
                    activePlayingChapterId = currentlyDisplayingChapter.ChapterId;
                }
            }

            // 播放指定節點並關閉面板
            StoryPlayer.Instance.PlayNode(node.Id);
            ClosePanel();
        }
    }
}
