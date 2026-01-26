using BMC.UI;
using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace BMC.Story
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
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

        // --- 核心入口 ---
        public void RefreshStoryLayout(StoryNode startNode, StoryPackage package)
        {
            if (startNode == null || package == null) return;

            // 1. 清理舊畫面
            ClearOldLayout();

            // 2. 設定 Layout Group 參數 (確保間距符合 400 的要求)
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

            // 嘗試取得 HorizontalLayoutGroup，如果沒有就加一個 (或是手動設定已存在的)
            HorizontalLayoutGroup hGroup = contentRoot.GetComponent<HorizontalLayoutGroup>();
            if (hGroup != null)
            {
                // 計算 Spacing：如果是 "間隔"(Gap)，則 Spacing = 400
                // 如果是 "每個深度的跨度"(Stride)，則 Spacing = 400 - 300 = 100
                // 這裡通常解釋為 Stride (中心點到中心點或是左邊到左邊的距離)
                float spacing = depthSpacing - itemWidth;
                hGroup.spacing = spacing;

                // 確保子物件不會被強制撐大，而是維持我們設定的 Width
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
            currentMaxDepth = 0; // 重置最大深度

            while (queue.Count > 0)
            {
                var (currentNode, currentDepth) = queue.Dequeue();

                CreateNodeUI(currentNode, currentDepth);

                foreach (var choice in currentNode.Choices)
                {
                    if (string.IsNullOrEmpty(choice.TargetNodeId)) continue;

                    if (idLookup.TryGetValue(choice.TargetNodeId, out StoryNode nextNode))
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
                        Debug.LogWarning($"[Layout Error] Node '{currentNode.Id}' 指向了不存在的 ID: '{choice.TargetNodeId}'");
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

            // 確保 Item 寬度正確 (如果 Prefab 已經是 300 則不需要，但以防萬一)
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

            // [關鍵修改] 強制設定 Column 寬度為 300
            RectTransform colRect = newCol.GetComponent<RectTransform>();
            if (colRect != null)
            {
                colRect.sizeDelta = new Vector2(itemWidth, colRect.sizeDelta.y);
            }

            // 如果 Column 上有 LayoutElement，也要設定它，以防被父層 LayoutGroup 忽略
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

                foreach (var choice in parentNode.Choices)
                {
                    if (string.IsNullOrEmpty(choice.TargetNodeId)) continue;
                    if (idLookup.TryGetValue(choice.TargetNodeId, out StoryNode childNode))
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
        /// 使用 nodeDepthMap 與 currentMaxDepth 計算百分比，直接設定 horizontalNormalizedPosition
        /// </summary>
        private async UniTask ScrollToNode(StoryNode targetNode)
        {
            if (targetNode == null || !nodeDepthMap.ContainsKey(targetNode)) return;

            // 等待一幀讓 ScrollRect 初始化內部狀態
            await UniTask.WaitForEndOfFrame();

            // 停止慣性
            scrollRect.velocity = Vector2.zero;

            int targetDepth = nodeDepthMap[targetNode];

            if (currentMaxDepth > 0)
            {
                // 計算當前深度在總深度中的比例 (0 ~ 1)
                // Depth 0 -> 0.0
                // Depth Max -> 1.0
                // Depth Mid -> 0.5 (大約置中)
                float normalizedPos = (float)targetDepth / currentMaxDepth;
                Log.Info($"[Scroll] Target Depth: {targetDepth}, Max Depth: {currentMaxDepth}, Normalized Pos: {normalizedPos}");
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