using BMC.UI;
using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.Story
{
    public class StoryLinePanel : UIPanel
    {
        [Header("UI References")]
        [SerializeField] private Transform contentRoot;
        [SerializeField] private ConnectionDrawer connectionDrawer;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Prefabs")]
        [SerializeField] private GameObject storyItemPrefab;
        [SerializeField] private GameObject columnPrefab;

        [Header("Layout Settings")]
        [SerializeField] private float itemWidth = 300f;
        [SerializeField] private float depthSpacing = 400f; // 深度之間的距離 (Stride)

        // --- 內部狀態 ---
        private Dictionary<int, Transform> depthColumns = new Dictionary<int, Transform>();

        // 用來記錄 Node 物件對應到的 UI RectTransform (畫線與定位用)
        private Dictionary<StoryNode, RectTransform> nodeToRectMap = new Dictionary<StoryNode, RectTransform>();

        // 記錄每個 Node 的深度資料
        private Dictionary<StoryNode, int> nodeDepthMap = new Dictionary<StoryNode, int>();
        private int currentMaxDepth = 0; // [新增] 記錄當前最大深度

        void Awake()
        {
            if (storyItemPrefab) storyItemPrefab.SetActive(false);
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

        // --- 核心入口 ---
        public void RefreshStoryLayout(StoryNode startNode, StoryPackage package)
        {
            if (startNode == null || package == null) return;

            // 1. 清理舊畫面
            ClearOldLayout();

            // 2. 設定 Layout Group 參數
            SetupLayoutGroup();

            // 3. 建立速查表
            Dictionary<string, StoryNode> idLookup = new Dictionary<string, StoryNode>();
            foreach (var node in package.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Id) && !idLookup.ContainsKey(node.Id))
                {
                    idLookup.Add(node.Id, node);
                }
            }

            // 4. 開始排版
            GenerateNodesBFS(startNode, idLookup);

            // 5. 強制刷新 Layout
            Canvas.ForceUpdateCanvases();

            // 6. 畫線
            DrawConnections(idLookup);

            // 7. 自動捲動到 StartNode
            ScrollToNode(StoryPlayer.Instance.CrtNode).Forget();
        }

        private void SetupLayoutGroup()
        {
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

                // 使用新的通用方法獲取所有目標 ID
                foreach (string targetId in GetTargetNodeIds(currentNode))
                {
                    if (string.IsNullOrEmpty(targetId)) continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode nextNode))
                    {
                        if (!visited.Contains(nextNode))
                        {
                            queue.Enqueue((nextNode, currentDepth + 1));
                            visited.Add(nextNode);

                            // 更新深度表與最大深度
                            nodeDepthMap[nextNode] = currentDepth + 1;
                            currentMaxDepth = Mathf.Max(currentMaxDepth, currentDepth + 1);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Layout Error] Node '{currentNode.Id}' 指向了不存在的 ID: '{targetId}'");
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
                    StoryPlayer.Instance.PlayNode(node.Id);
                    ClosePanel();
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

        private Transform GetColumnForDepth(int depth)
        {
            if (depthColumns.ContainsKey(depth)) return depthColumns[depth];

            GameObject newCol = Instantiate(columnPrefab, contentRoot);
            newCol.name = $"Column_Depth_{depth}";
            newCol.transform.SetSiblingIndex(depth + 1);
            newCol.gameObject.SetActive(true);

            RectTransform colRect = newCol.GetComponent<RectTransform>();
            if (colRect != null)
            {
                colRect.sizeDelta = new Vector2(itemWidth, colRect.sizeDelta.y);
            }

            LayoutElement layoutElem = newCol.GetComponent<LayoutElement>();
            if (layoutElem == null) layoutElem = newCol.AddComponent<LayoutElement>();
            layoutElem.minWidth = itemWidth;
            layoutElem.preferredWidth = itemWidth;
            layoutElem.flexibleWidth = 0;

            depthColumns.Add(depth, newCol.transform);
            return newCol.transform;
        }

        private void DrawConnections(Dictionary<string, StoryNode> idLookup)
        {
            List<ConnectionDrawer.Connection> links = new List<ConnectionDrawer.Connection>();
            foreach (var kvp in nodeToRectMap)
            {
                StoryNode parentNode = kvp.Key;
                RectTransform parentRect = kvp.Value;

                // 使用新的通用方法獲取所有連線目標
                foreach (string targetId in GetTargetNodeIds(parentNode))
                {
                    if (string.IsNullOrEmpty(targetId)) continue;

                    if (idLookup.TryGetValue(targetId, out StoryNode childNode))
                    {
                        if (nodeToRectMap.TryGetValue(childNode, out RectTransform childRect))
                        {
                            links.Add(new ConnectionDrawer.Connection
                            {
                                start = parentRect,
                                end = childRect
                            });
                        }
                    }
                }
            }
            connectionDrawer.SetConnections(links);
            connectionDrawer.SetAllDirty();
        }

        /// <summary>
        /// 核心輔助方法：遞歸獲取節點中所有可能跳轉的目標 ID
        /// (包含 AutoJump, ShowChoices, GameDice, GameRoulette, GameQTE)
        /// </summary>
        private IEnumerable<string> GetTargetNodeIds(StoryNode node)
        {
            // 1. 自動跳轉
            if (!string.IsNullOrEmpty(node.AutoJumpNodeId)) yield return node.AutoJumpNodeId;

            // 2. OnEnterEvents
            if (node.OnEnterEvents != null)
            {
                foreach (var evt in node.OnEnterEvents)
                {
                    foreach (var id in GetTargetsFromEvent(evt)) yield return id;
                }
            }

            // 3. OnExitEvents
            if (node.OnExitEvents != null)
            {
                foreach (var evt in node.OnExitEvents)
                {
                    foreach (var id in GetTargetsFromEvent(evt)) yield return id;
                }
            }
        }

        private IEnumerable<string> GetTargetsFromEvent(StoryEvent evt)
        {
            switch (evt.ActionCase)
            {
                case StoryEvent.ActionOneofCase.ShowChoices:
                    foreach (var c in evt.ShowChoices.Choices)
                    {
                        if (!string.IsNullOrEmpty(c.TargetNodeId)) yield return c.TargetNodeId;
                    }
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
            }
        }

        private async UniTask ScrollToNode(StoryNode targetNode)
        {
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
        {
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