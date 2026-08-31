using BMC.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.Story
{
    /// <summary>
    /// UI Toolkit 版節點圖(BFS 分欄佈局＋連線特效＋捲動)，從 uGUI 版 StoryLinePanel 抽出來的部分——
    /// 純 VisualElement，不是 UIPanel，讓消費端可以用「組合」的方式把它嵌進自己的面板版面裡
    /// (取代舊版 EFMStoryLinePanel 用 [SerializeField] 持有一整個 StoryLinePanel 的做法)。
    /// </summary>
    public class StoryGraphView : VisualElement
    {
        /// <summary>建立節點項目的工廠方法，換掉這個就能換整個節點項目的型別(取代舊版換 prefab 參考)。</summary>
        public Func<StoryLineItem> ItemFactory { get; set; }

        public event Action<StoryNode> NodeClicked;

        public float itemWidth = 300f;
        public float depthSpacing = 500f;

        private readonly ScrollView scrollView;
        private readonly VisualElement contentRoot;
        private readonly ConnectionCanvas connectionCanvas;

        private readonly Dictionary<int, VisualElement> depthColumns = new Dictionary<int, VisualElement>();
        private readonly Dictionary<StoryNode, VisualElement> nodeToElementMap = new Dictionary<StoryNode, VisualElement>();
        private readonly Dictionary<StoryNode, int> nodeDepthMap = new Dictionary<StoryNode, int>();
        private int currentMaxDepth;

        private VisualTreeAsset itemTemplate;

        /// <summary>
        /// 已載入的節點項目模板(EnsureItemTemplateAsync 完成後才有值)，供覆寫 ItemFactory 的消費端
        /// 重複使用同一份模板建立自己的項目子類別，不用另外再載一次。
        /// </summary>
        public VisualTreeAsset ItemTemplate => itemTemplate;

        public StoryGraphView()
        {
            AddToClassList("story-graph-view");

            scrollView = new ScrollView(ScrollViewMode.Horizontal) { name = "graph-scroll" };
            scrollView.AddToClassList("story-graph-view__scroll");
            Add(scrollView);

            contentRoot = new VisualElement { name = "graph-content" };
            contentRoot.AddToClassList("story-graph-view__content");
            scrollView.Add(contentRoot);

            connectionCanvas = new ConnectionCanvas { name = "graph-connections" };
            connectionCanvas.AddToClassList("story-graph-view__connections");
            contentRoot.Add(connectionCanvas);
            contentRoot.RegisterCallback<GeometryChangedEvent>(_ => connectionCanvas.MarkDirtyRepaint());

            ItemFactory = DefaultCreateItem;
        }

        private StoryLineItem DefaultCreateItem() => new StoryLineItem(itemTemplate);

        /// <summary>
        /// 預先載入節點項目的 UXML 模板(有快取，重複呼叫安全)。子類若換了 ItemFactory 用自己的項目型別，
        /// 需要自己保證對應模板在 RefreshStoryLayout 呼叫前已經就緒。
        /// </summary>
        public async UniTask EnsureItemTemplateAsync()
        {
            if (itemTemplate != null)
                return;

            if (!ResMgr.Instance.Check(StoryLineItem.TemplateAddress))
            {
                Log.Error($"[StoryGraphView] 找不到節點項目模板位址: '{StoryLineItem.TemplateAddress}'");
                return;
            }

            itemTemplate = await ResMgr.Instance.LoadAssetAsync<VisualTreeAsset>(StoryLineItem.TemplateAddress, false);
        }

        public async UniTask RefreshStoryLayout(StoryNode startNode, StoryPackage package)
        {
            if (startNode == null || package == null)
                return;

            await EnsureItemTemplateAsync();

            ClearOldLayout();

            Dictionary<string, StoryNode> idLookup = new Dictionary<string, StoryNode>();
            foreach (var node in package.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Id) && !idLookup.ContainsKey(node.Id))
                    idLookup.Add(node.Id, node);
            }

            GenerateNodesBFS(startNode, idLookup);
            DrawConnections(idLookup);

            if (StoryPlayer.Instance.CrtNode != null)
                await ScrollToNode(StoryPlayer.Instance.CrtNode);
            else
                await ScrollToNode(startNode);
        }

        private void GenerateNodesBFS(StoryNode startNode, Dictionary<string, StoryNode> idLookup)
        {
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
                    if (string.IsNullOrEmpty(targetId))
                        continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode nextNode) && !visited.Contains(nextNode))
                    {
                        queue.Enqueue((nextNode, currentDepth + 1));
                        visited.Add(nextNode);

                        nodeDepthMap[nextNode] = currentDepth + 1;
                        currentMaxDepth = Mathf.Max(currentMaxDepth, currentDepth + 1);
                    }
                }
            }
        }

        private void CreateNodeUI(StoryNode node, int depth)
        {
            VisualElement column = GetColumnForDepth(depth);

            StoryLineItem item = ItemFactory != null ? ItemFactory() : DefaultCreateItem();
            item.Init(node, () => NodeClicked?.Invoke(node));
            column.Add(item);

            nodeToElementMap[node] = item;
        }

        private VisualElement GetColumnForDepth(int depth)
        {
            if (depthColumns.TryGetValue(depth, out var existing))
                return existing;

            var column = new VisualElement { name = $"Column_Depth_{depth}" };
            column.AddToClassList("story-graph-view__column");
            column.style.width = itemWidth;
            column.style.marginRight = depthSpacing - itemWidth;

            // index 0 是 connectionCanvas，欄位一律排在它後面、依深度遞增排序
            contentRoot.Insert(depth + 1, column);

            depthColumns.Add(depth, column);
            return column;
        }

        private void DrawConnections(Dictionary<string, StoryNode> idLookup)
        {
            List<ConnectionCanvas.Connection> links = new List<ConnectionCanvas.Connection>();
            foreach (var kvp in nodeToElementMap)
            {
                StoryNode parentNode = kvp.Key;
                VisualElement parentElement = kvp.Value;

                foreach (string targetId in GetTargetNodeIds(parentNode))
                {
                    if (string.IsNullOrEmpty(targetId))
                        continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode childNode) &&
                        nodeToElementMap.TryGetValue(childNode, out VisualElement childElement))
                    {
                        links.Add(new ConnectionCanvas.Connection { start = parentElement, end = childElement });
                    }
                }
            }
            connectionCanvas.SetConnections(links);
        }

        public async UniTask ScrollToNode(StoryNode targetNode)
        {
            if (targetNode == null || !nodeDepthMap.ContainsKey(targetNode))
                return;

            await WaitForLayoutAsync();

            int targetDepth = nodeDepthMap[targetNode];

            float viewportWidth = scrollView.contentViewport.resolvedStyle.width;
            float contentWidth = contentRoot.resolvedStyle.width;
            float maxScrollX = Mathf.Max(0f, contentWidth - viewportWidth);

            float normalizedPos = currentMaxDepth > 0 ? Mathf.Clamp01((float)targetDepth / currentMaxDepth) : 0f;
            scrollView.scrollOffset = new Vector2(normalizedPos * maxScrollX, scrollView.scrollOffset.y);
        }

        /// <summary>
        /// 等 Yoga 版面算完一次。UI Toolkit 的佈局是非同步的，跟 uGUI 的
        /// Canvas.ForceUpdateCanvases() 同步佈局不一樣，捲動位置要算對寬度必須先等這個。
        /// </summary>
        private UniTask WaitForLayoutAsync()
        {
            if (contentRoot.resolvedStyle.width > 0f)
                return UniTask.CompletedTask;

            var tcs = new UniTaskCompletionSource();
            void Handler(GeometryChangedEvent e)
            {
                contentRoot.UnregisterCallback<GeometryChangedEvent>(Handler);
                tcs.TrySetResult();
            }
            contentRoot.RegisterCallback<GeometryChangedEvent>(Handler);
            return tcs.Task;
        }

        public void ClearOldLayout()
        {
            contentRoot.Clear();
            contentRoot.Add(connectionCanvas);

            depthColumns.Clear();
            nodeToElementMap.Clear();
            nodeDepthMap.Clear();
            currentMaxDepth = 0;
            connectionCanvas.SetConnections(new List<ConnectionCanvas.Connection>());
        }

        public static IEnumerable<string> GetTargetNodeIds(StoryNode node)
        {
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
        {
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
    }
}
