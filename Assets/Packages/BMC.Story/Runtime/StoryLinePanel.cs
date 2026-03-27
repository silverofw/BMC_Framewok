using BMC.UI;
using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.Story
{
    // [新增] 供外部傳入的章節資料結構
    public class ChapterData
    {
        public string ChapterId;
        public string ChapterName;
        // 參數化：由外部定義如何取得該章節的 byte[] (例如從 Addressables 或記憶體快取)
        public Func<byte[]> GetChapterBytes;
    }

    public class StoryLinePanel : UIPanel
    {
        [Header("UI References - Node Layout")]
        [SerializeField] private Transform contentRoot;
        [SerializeField] private ConnectionDrawer connectionDrawer;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Prefabs")]
        [SerializeField] private GameObject storyItemPrefab;
        [SerializeField] private GameObject columnPrefab;

        [Header("Layout Settings")]
        [SerializeField] private float itemWidth = 300f;
        [SerializeField] private float depthSpacing = 400f;

        [Header("UI References - Chapter Selection")]
        [SerializeField] private UIButton chapterBtn;
        [SerializeField] private UIButton chapterCloseBtn;
        [SerializeField] private Transform chapterListRoot;
        [SerializeField] private GameObject chapterButtonPrefab;

        // --- 內部狀態 ---
        private Dictionary<int, Transform> depthColumns = new Dictionary<int, Transform>();
        private Dictionary<StoryNode, RectTransform> nodeToRectMap = new Dictionary<StoryNode, RectTransform>();
        private Dictionary<StoryNode, int> nodeDepthMap = new Dictionary<StoryNode, int>();
        private int currentMaxDepth = 0;

        // [新增] 追蹤當前預覽與實際遊玩的章節狀態
        private string activePlayingChapterId;       // StoryPlayer 正在跑的章節 ID
        private ChapterData currentlyDisplayingChapter; // UI 畫面上正在預覽的章節

        void Awake()
        {
            if (storyItemPrefab) storyItemPrefab.SetActive(false);
            if (chapterButtonPrefab) chapterButtonPrefab.SetActive(false);
        }

        void Start()
        {
            StoryPlayer.Instance.Pause();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            StoryPlayer.Instance.Play();
        }

        /// <summary>
        /// [新增] 外部呼叫入口：初始化面板與章節清單
        /// </summary>
        /// <param name="chapters">所有可選的章節清單</param>
        /// <param name="currentChapterId">StoryPlayer 目前正在遊玩的章節 ID</param>
        public void InitializePanel(List<ChapterData> chapters, string currentChapterId)
        {
            activePlayingChapterId = currentChapterId;

            // 1. 清理舊的章節按鈕
            foreach (Transform child in chapterListRoot)
            {
                if (child.gameObject != chapterButtonPrefab)
                    Destroy(child.gameObject);
            }

            // 2. 生成新的章節按鈕
            ChapterData startChapter = null;
            foreach (var chapter in chapters)
            {
                if (chapter.GetChapterBytes == null) continue;

                var btnObj = Instantiate(chapterButtonPrefab, chapterListRoot);
                btnObj.SetActive(true);

                var btnText = btnObj.GetComponentInChildren<UIText>();
                if (btnText != null) btnText.Set(chapter.ChapterName);

                var btn = btnObj.GetComponent<UIButton>();
                if (btn != null)
                {
                    btn.OnClick = ()=> PreviewChapter(chapter);
                }

                if (chapter.ChapterId == currentChapterId)
                {
                    startChapter = chapter;
                }
            }

            chapterBtn.OnClick = () => { chapterListRoot.gameObject.SetActive(true); };
            chapterCloseBtn.OnClick = () => { chapterListRoot.gameObject.SetActive(false); };



            // 3. 預設顯示當前正在遊玩的章節 (若找不到就顯示第一個)
            if (startChapter != null)
            {
                PreviewChapter(startChapter);
            }
            else if (chapters.Count > 0)
            {
                PreviewChapter(chapters[0]);
            }
        }

        /// <summary>
        /// [新增] 預覽章節：只更新 UI，不影響 StoryPlayer 內部資料
        /// </summary>
        private void PreviewChapter(ChapterData chapter)
        {
            currentlyDisplayingChapter = chapter;
            byte[] bytes = chapter.GetChapterBytes?.Invoke();
            if (bytes == null) return;

            // 僅在 UI 層面解析暫時的 Package 用於排版
            StoryPackage tempPackage = StoryPackage.Parser.ParseFrom(bytes);

            // 找出 StartNode (對齊 StoryPlayer 預設找 "Start" 的邏輯)
            StoryNode startNode = null;
            foreach (var node in tempPackage.Nodes)
            {
                if (node.Id == "Start")
                {
                    startNode = node;
                    break;
                }
            }

            if (startNode != null)
            {
                RefreshStoryLayout(startNode, tempPackage);
            }
            else
            {
                Debug.LogWarning($"[StoryLinePanel] 章節 {chapter.ChapterId} 找不到 'Start' 節點！");
            }
        }

        public void RefreshStoryLayout(StoryNode startNode, StoryPackage package)
        {
            if (startNode == null || package == null) return;

            ClearOldLayout();
            SetupLayoutGroup();

            Dictionary<string, StoryNode> idLookup = new Dictionary<string, StoryNode>();
            foreach (var node in package.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Id) && !idLookup.ContainsKey(node.Id))
                {
                    idLookup.Add(node.Id, node);
                }
            }

            GenerateNodesBFS(startNode, idLookup);
            Canvas.ForceUpdateCanvases();
            DrawConnections(idLookup);

            // [修改] 判斷預覽的章節是否為當前遊玩的章節，來決定要捲動到當前進度還是開頭
            if (currentlyDisplayingChapter != null && currentlyDisplayingChapter.ChapterId == activePlayingChapterId && StoryPlayer.Instance.CrtNode != null)
            {
                ScrollToNode(StoryPlayer.Instance.CrtNode).Forget();
            }
            else
            {
                ScrollToNode(startNode).Forget();
            }
        }

        private void SetupLayoutGroup()
        { /* 保持原樣不變 */
            if (contentRoot == null) return;
            HorizontalLayoutGroup hGroup = contentRoot.GetComponent<HorizontalLayoutGroup>();
            if (hGroup != null)
            {
                float spacing = depthSpacing - itemWidth;
                hGroup.spacing = spacing;
                hGroup.childControlWidth = false;
                hGroup.childForceExpandWidth = false;
            }
        }

        private void GenerateNodesBFS(StoryNode startNode, Dictionary<string, StoryNode> idLookup)
        { /* 保持原樣不變 */
            Queue<(StoryNode node, int depth)> queue = new Queue<(StoryNode, int)>();
            HashSet<StoryNode> visited = new HashSet<StoryNode>();

            queue.Enqueue((startNode, 0));
            visited.Add(startNode);
            nodeDepthMap[startNode] = 0;
            currentMaxDepth = 0;

            while (queue.Count > 0)
            {
                var (currentNode, currentDepth) = queue.Dequeue();

                CreateNodeUI(currentNode, currentDepth);

                foreach (string targetId in GetTargetNodeIds(currentNode))
                {
                    if (string.IsNullOrEmpty(targetId)) continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode nextNode))
                    {
                        if (!visited.Contains(nextNode))
                        {
                            queue.Enqueue((nextNode, currentDepth + 1));
                            visited.Add(nextNode);

                            nodeDepthMap[nextNode] = currentDepth + 1;
                            currentMaxDepth = Mathf.Max(currentMaxDepth, currentDepth + 1);
                        }
                    }
                }
            }
        }

        private void CreateNodeUI(StoryNode node, int depth)
        {
            Transform parentColumn = GetColumnForDepth(depth);
            GameObject itemObj = Instantiate(storyItemPrefab, parentColumn);

            StoryLineItem uiComponent = itemObj.GetComponent<StoryLineItem>();
            if (uiComponent != null)
            {
                uiComponent.Init(node, () => {
                    // [新增] 點擊節點時的跳轉邏輯
                    HandleNodeClicked(node);
                });
            }
            itemObj.SetActive(true);

            RectTransform itemRect = itemObj.GetComponent<RectTransform>();
            if (itemRect != null)
            {
                itemRect.sizeDelta = new Vector2(itemWidth, itemRect.sizeDelta.y);
            }

            nodeToRectMap[node] = itemRect;
        }

        /// <summary>
        /// [新增] 處理玩家確認點擊某個節點的行為
        /// </summary>
        private void HandleNodeClicked(StoryNode node)
        {
            // 檢查玩家點擊的節點，是否屬於另一個尚未載入 StoryPlayer 的章節
            if (currentlyDisplayingChapter.ChapterId != activePlayingChapterId)
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

        private Transform GetColumnForDepth(int depth)
        { /* 保持原樣不變 */
            if (depthColumns.ContainsKey(depth)) return depthColumns[depth];

            GameObject newCol = Instantiate(columnPrefab, contentRoot);
            newCol.name = $"Column_Depth_{depth}";
            newCol.transform.SetSiblingIndex(depth + 1);
            newCol.gameObject.SetActive(true);

            RectTransform colRect = newCol.GetComponent<RectTransform>();
            if (colRect != null) colRect.sizeDelta = new Vector2(itemWidth, colRect.sizeDelta.y);

            LayoutElement layoutElem = newCol.GetComponent<LayoutElement>();
            if (layoutElem == null) layoutElem = newCol.AddComponent<LayoutElement>();
            layoutElem.minWidth = itemWidth;
            layoutElem.preferredWidth = itemWidth;
            layoutElem.flexibleWidth = 0;

            depthColumns.Add(depth, newCol.transform);
            return newCol.transform;
        }

        private void DrawConnections(Dictionary<string, StoryNode> idLookup)
        { /* 保持原樣不變 */
            List<ConnectionDrawer.Connection> links = new List<ConnectionDrawer.Connection>();
            foreach (var kvp in nodeToRectMap)
            {
                StoryNode parentNode = kvp.Key;
                RectTransform parentRect = kvp.Value;

                foreach (string targetId in GetTargetNodeIds(parentNode))
                {
                    if (string.IsNullOrEmpty(targetId)) continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode childNode))
                    {
                        if (nodeToRectMap.TryGetValue(childNode, out RectTransform childRect))
                        {
                            links.Add(new ConnectionDrawer.Connection { start = parentRect, end = childRect });
                        }
                    }
                }
            }
            connectionDrawer.SetConnections(links);
            connectionDrawer.SetAllDirty();
        }

        public static IEnumerable<string> GetTargetNodeIds(StoryNode node)
        { /* 保持原樣不變 */
            if (!string.IsNullOrEmpty(node.AutoJumpNodeId)) yield return node.AutoJumpNodeId;
            if (node.AutoJumpAffectionRules != null)
                foreach (var rule in node.AutoJumpAffectionRules)
                    if (!string.IsNullOrEmpty(rule.TargetNodeId)) yield return rule.TargetNodeId;
            if (node.OnEnterEvents != null)
                foreach (var evt in node.OnEnterEvents)
                    foreach (var id in GetTargetsFromEvent(evt)) yield return id;
            if (node.OnExitEvents != null)
                foreach (var evt in node.OnExitEvents)
                    foreach (var id in GetTargetsFromEvent(evt)) yield return id;
        }

        public static IEnumerable<string> GetTargetsFromEvent(StoryEvent evt)
        { /* 保持原樣不變 */
            switch (evt.ActionCase)
            {
                case StoryEvent.ActionOneofCase.ShowChoices:
                    foreach (var c in evt.ShowChoices.Choices)
                        if (!string.IsNullOrEmpty(c.TargetNodeId)) yield return c.TargetNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameDice:
                    if (!string.IsNullOrEmpty(evt.GameDice.SuccessNodeId)) yield return evt.GameDice.SuccessNodeId;
                    if (!string.IsNullOrEmpty(evt.GameDice.FailNodeId)) yield return evt.GameDice.FailNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameRussianRoulette:
                    if (!string.IsNullOrEmpty(evt.GameRussianRoulette.WinNodeId)) yield return evt.GameRussianRoulette.WinNodeId;
                    if (!string.IsNullOrEmpty(evt.GameRussianRoulette.LoseNodeId)) yield return evt.GameRussianRoulette.LoseNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameQte:
                    if (!string.IsNullOrEmpty(evt.GameQte.SuccessNodeId)) yield return evt.GameQte.SuccessNodeId;
                    if (!string.IsNullOrEmpty(evt.GameQte.FailNodeId)) yield return evt.GameQte.FailNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GamePuzzle:
                    if (!string.IsNullOrEmpty(evt.GamePuzzle.SuccessNodeId)) yield return evt.GamePuzzle.SuccessNodeId;
                    if (!string.IsNullOrEmpty(evt.GamePuzzle.FailNodeId)) yield return evt.GamePuzzle.FailNodeId;
                    break;
                case StoryEvent.ActionOneofCase.PlayAvgDialog:
                    if (evt.PlayAvgDialog.Frames != null)
                        foreach (var frame in evt.PlayAvgDialog.Frames)
                            if (frame.FrameType == DialogFrame.Types.FrameType.WithJumpNode && !string.IsNullOrEmpty(frame.TargetNodeId))
                                yield return frame.TargetNodeId;
                    break;
            }
        }

        public async UniTask ScrollToNode(StoryNode targetNode)
        { /* 保持原樣不變 */
            if (targetNode == null || !nodeDepthMap.ContainsKey(targetNode)) return;

            await UniTask.WaitForEndOfFrame();
            scrollRect.velocity = Vector2.zero;

            int targetDepth = nodeDepthMap[targetNode];

            if (currentMaxDepth > 0)
            {
                float normalizedPos = (float)targetDepth / currentMaxDepth;
                scrollRect.horizontalNormalizedPosition = Mathf.Clamp01(normalizedPos);
            }
            else
            {
                scrollRect.horizontalNormalizedPosition = 0f;
            }
        }

        public void ClearOldLayout()
        { /* 保持原樣不變 */
            StopAllCoroutines();

            List<GameObject> toDestroy = new List<GameObject>();
            foreach (Transform child in contentRoot)
            {
                if (child == connectionDrawer.transform) continue;
                toDestroy.Add(child.gameObject);
            }
            foreach (var obj in toDestroy)
            {
                if (Application.isPlaying) Destroy(obj);
                else DestroyImmediate(obj);
            }

            depthColumns.Clear();
            nodeToRectMap.Clear();
            nodeDepthMap.Clear();
            currentMaxDepth = 0;
            connectionDrawer.SetConnections(new List<ConnectionDrawer.Connection>());
            connectionDrawer.transform.SetAsFirstSibling();
        }
    }
}