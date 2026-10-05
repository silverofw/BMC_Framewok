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

        /// <summary>拖曳判定為滾動的位移門檻(像素)，比照 BMC.UIToolkit.MultiListView 的 dragThreshold。</summary>
        public float dragThreshold = 10f;

        private Vector2 dragStartPointer;
        private Vector2 dragStartOffset;
        private int dragPointerId = -1;
        private bool isPointerDown;
        private bool isDragging;

        /// <summary>
        /// 已載入的節點項目模板(EnsureItemTemplateAsync 完成後才有值)，供覆寫 ItemFactory 的消費端
        /// 重複使用同一份模板建立自己的項目子類別，不用另外再載一次。
        /// </summary>
        public VisualTreeAsset ItemTemplate => itemTemplate;

        public StoryGraphView()
        {
            AddToClassList("story-graph-view");
            style.flexGrow = 1f;

            // 兩軸都要能捲：橫向是劇情深度，縱向是同深度的節點疊在同一欄裡。同深度節點一多
            // (例如 0-9 之後一次分出五個節點，一張卡約 250px、五張超過 1200px)整欄就比螢幕高，
            // 只有單軸可捲時下面幾張既被切掉也滑不到。
            //
            // 單軸模式還有一個更隱性的問題：那時候 contentContainer 的高度等於可視高度，比它高的
            // 內容算「溢出」而不是「可捲動範圍」，再加上下面那段垂直置中，整份內容在
            // scrollOffset.y = 0 時就已經被往上推了 (內容高 - 可視高) / 2 ——而 scrollOffset.y
            // 不能是負的，被推上去那一段永遠捲不回來。實測 1200x600 可視區 + depth 10 疊五張卡：
            // 15 張卡裡有 12 張(depth 0~10 每欄的第一張，加上 depth 10 的第二張)不管怎麼捲都
            // 沒辦法完整看到。改成雙軸之後 contentContainer 改為貼合內容高度，這個問題一起消失。
            scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "graph-scroll" };
            scrollView.AddToClassList("story-graph-view__scroll");
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            // 下面四行純粹是把「內容比可視區矮時，圖表垂直置中」這個外觀補回來(拿掉不影響可捲動
            // 範圍，每一張卡照樣到得了)。雙軸模式下 ScrollView 的內部佈局跟單軸模式差很多，
            // 以下全部實測於 Unity 6000.5.3f1：
            // 1. 單軸模式的 contentViewport 會填滿整個 ScrollView；雙軸模式改成「貼合內容高度」，
            //    內容矮的時候整張圖會縮在上緣、下面空一塊。先把可視區高度變回確定值，第 3 點的
            //    百分比才解析得出來(對著高度不確定的父層算百分比，Yoga 會直接忽略)。
            // 2. contentContainer 的 flex-direction 單軸模式是 row(交叉軸=垂直)、雙軸模式是
            //    column(交叉軸=水平)。所以舊版靠 contentRoot 的 alignSelf = Center 做垂直置中，
            //    搬到雙軸模式只會變成「水平置中」——垂直置中改由 contentContainer 的
            //    justify-content 負責(主軸方向剛好對調)。
            // 3. contentContainer 高度貼合內容，內容矮時沒有多餘空間可以置中，要用 min-height
            //    至少撐到可視高度；內容高的時候 min-height 撐不到它，照樣貼合內容、照樣可捲，
            //    也就不會重演上面那種「置中把內容推出可捲範圍」的狀況。
            var viewportRow = scrollView.contentViewport.parent;
            if (viewportRow != null)
                viewportRow.style.flexGrow = 1f;
            scrollView.contentViewport.style.height = Length.Percent(100f);
            scrollView.contentContainer.style.minHeight = Length.Percent(100f);
            scrollView.contentContainer.style.justifyContent = Justify.Center;

            contentRoot = new VisualElement { name = "graph-content" };
            contentRoot.AddToClassList("story-graph-view__content");
            // 分欄佈局是結構性需求(不是外觀細節)，直接寫死在 C# 裡，不依賴消費端的 UXML 有沒有帶對應
            // 樣式表——漏接樣式表時，VisualElement 預設的 flex-direction 是 column，欄位會全部疊成一直排。
            contentRoot.style.flexDirection = FlexDirection.Row;
            scrollView.Add(contentRoot);

            // UI Toolkit 的 ScrollView 預設只吃滾輪與捲軸，滑鼠在內容上拖曳不會平移
            // (跟 BMC.UIToolkit.MultiListView 需要自己接手拖曳捲動的原因一樣)。
            // 用 TrickleDown 在節點項目(UIButton)之前先看到事件，超過門檻才攔截為拖曳，
            // 門檻內的按放仍會正常觸發節點點擊。
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(_ => ResetDrag());

            connectionCanvas = new ConnectionCanvas { name = "graph-connections" };
            connectionCanvas.AddToClassList("story-graph-view__connections");
            connectionCanvas.style.position = Position.Absolute;
            connectionCanvas.style.left = 0;
            connectionCanvas.style.top = 0;
            connectionCanvas.style.right = 0;
            connectionCanvas.style.bottom = 0;
            contentRoot.Add(connectionCanvas);
            contentRoot.RegisterCallback<GeometryChangedEvent>(_ => connectionCanvas.MarkDirtyRepaint());

            ItemFactory = DefaultCreateItem;
        }

        private StoryLineItem DefaultCreateItem() => new StoryLineItem(itemTemplate);

        #region 拖曳捲動

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;

            isPointerDown = true;
            isDragging = false;
            dragPointerId = evt.pointerId;
            dragStartPointer = evt.position;
            dragStartOffset = scrollView.scrollOffset;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isPointerDown || evt.pointerId != dragPointerId)
                return;

            Vector2 delta = (Vector2)evt.position - dragStartPointer;

            if (!isDragging)
            {
                // 門檻比的是位移向量長度，不是單軸分量。只看 X 的話，純垂直拖曳(X 位移 0)永遠
                // 過不了門檻：畫面不會跟著動，而且因為一直沒進入拖曳狀態、沒擷取指標，放手時
                // 節點項目照樣收到 PointerUp 合成出 ClickEvent——變成「想往下滑結果開了一個節點」。
                // 用平方比較省掉開根號，跟 delta.magnitude < dragThreshold 等價。
                if (delta.sqrMagnitude < dragThreshold * dragThreshold)
                    return;

                isDragging = true;
                // 擷取指標後續事件：節點項目(UIButton)收不到 PointerUp 就不會合成出 ClickEvent，
                // 拖曳結束後才不會誤觸點擊。門檻內的按放則完全不受影響，正常觸發節點點擊。
                this.CapturePointer(evt.pointerId);
            }

            scrollView.scrollOffset = ClampScrollOffset(dragStartOffset - delta);

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!isPointerDown || evt.pointerId != dragPointerId)
                return;

            bool wasDragging = isDragging;
            if (wasDragging && this.HasPointerCapture(evt.pointerId))
                this.ReleasePointer(evt.pointerId);

            ResetDrag();

            if (wasDragging)
                evt.StopPropagation();
        }

        private void ResetDrag()
        {
            isPointerDown = false;
            isDragging = false;
            dragPointerId = -1;
        }

        /// <summary>
        /// 把捲動位置夾回可視範圍。兩軸同一條式子(內容尺寸 - 可視尺寸，不足就是 0)，
        /// 只是把原本的單軸 ClampScrollX 擴成 Vector2 版。
        /// </summary>
        private Vector2 ClampScrollOffset(Vector2 value)
        {
            var viewport = scrollView.contentViewport.resolvedStyle;
            var content = contentRoot.resolvedStyle;
            float maxScrollX = Mathf.Max(0f, content.width - viewport.width);
            float maxScrollY = Mathf.Max(0f, content.height - viewport.height);
            return new Vector2(
                Mathf.Clamp(value.x, 0f, maxScrollX),
                Mathf.Clamp(value.y, 0f, maxScrollY));
        }

        #endregion

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

        private UniTask refreshQueue = UniTask.CompletedTask;

        /// <summary>
        /// 確認過的真實案例：同一次點擊，觸發面板開啟的按鈕 OnPointerUp 有時會被送出兩次
        /// (舊有的 uGUI 按鈕/輸入系統行為，不是這裡能處理的範圍)，導致這個方法被重疊呼叫——
        /// 後面那次的 ClearOldLayout 會把前面那次還在非同步載入預覽圖的節點項目從視覺樹上
        /// 拔掉，載入完成時只能拿到 panel==null。與其去堵輸入端的重複事件，這裡直接讓自己
        /// 對重疊呼叫具備韌性：用一個簡單的非同步佇列，確保永遠是「前一次完全跑完，才開始
        /// 清空重建下一次」，不管重疊呼叫的來源是什麼。
        /// </summary>
        public async UniTask RefreshStoryLayout(StoryNode startNode, StoryPackage package)
        {
            if (startNode == null || package == null)
                return;

            var previous = refreshQueue;
            var tcs = new UniTaskCompletionSource();
            refreshQueue = tcs.Task;
            await previous;

            try
            {
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
            finally
            {
                tcs.TrySetResult();
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
            // 先掛上視覺樹再 Init：YooAsset 資源第二次以後多半是快取命中，LoadAssetAsync 會
            // 同步完成而不是真的讓出流程，Init() 內部的 LoadPreview 整段(包含最後檢查
            // panel==null)會在這裡就跑完，早於 column.Add(item)——順序顛倒會讓每一個
            // 「其實載入成功」的項目都因為當下還沒掛上視覺樹而被誤判成失敗。
            column.Add(item);
            item.Init(node, () => NodeClicked?.Invoke(node));

            nodeToElementMap[node] = item;
        }

        private VisualElement GetColumnForDepth(int depth)
        {
            if (depthColumns.TryGetValue(depth, out var existing))
                return existing;

            var column = new VisualElement { name = $"Column_Depth_{depth}" };
            column.AddToClassList("story-graph-view__column");
            column.style.flexDirection = FlexDirection.Column;
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

            // X 軸維持原本的「深度比例」算法，一個字都沒改。
            float normalizedPos = currentMaxDepth > 0 ? Mathf.Clamp01((float)targetDepth / currentMaxDepth) : 0f;
            float targetX = normalizedPos * maxScrollX;

            // Y 軸沒有「深度比例」這種東西可以套(深度是橫向概念)，改成把目標節點自己的垂直中心
            // 對齊可視區中心：同深度的節點垂直疊在同一欄裡，欄位一高(例如 0-9 之後一次分出五個
            // 節點)光調 X 會讓目標節點停在可視區外。
            // 內容沒有比可視區高的章節，maxScrollY 就是 0，ClampScrollOffset 會把它夾回 0，
            // 結果跟改動前(Y 不動、開面板時本來就是 0)完全一樣。
            float targetY = scrollView.scrollOffset.y;
            if (nodeToElementMap.TryGetValue(targetNode, out VisualElement targetElement) && targetElement.parent != null)
            {
                // scrollOffset 量的是 contentContainer 座標系，所以換算到 contentContainer；
                // layout 是「相對於自己父層(欄位)」的矩形，要從 parent 的座標系換算起。
                Rect inContent = targetElement.parent.ChangeCoordinatesTo(scrollView.contentContainer, targetElement.layout);
                targetY = inContent.center.y - scrollView.contentViewport.resolvedStyle.height * 0.5f;
            }

            scrollView.scrollOffset = ClampScrollOffset(new Vector2(targetX, targetY));
        }

        /// <summary>
        /// 等 Yoga 版面算完一次。UI Toolkit 的佈局是非同步的，跟 uGUI 的
        /// Canvas.ForceUpdateCanvases() 同步佈局不一樣，捲動位置要算對寬度必須先等這個。
        ///
        /// 原本靠「resolvedStyle.width 是否已經 > 0」判斷要不要等：這在第一次開面板時會踩雷——
        /// contentRoot 換到新章節前(ClearOldLayout 之前)如果剛好殘留舊版面的寬度(例如同一顆
        /// StoryGraphView 被重疊呼叫、或前一次佈局的殘值還沒被下一次 Yoga pass 蓋掉)，這裡會誤判
        /// 「已經算完」直接跳過等待，读到的其實是新節點加進去之前的舊寬度，捲動位置因此算得
        /// 偏短，停在接近第一個節點的地方，要重開一次面板(這次殘值已經跟新內容一致)才會準。
        /// 改成固定等兩個 frame：不管 geometry 事件會不會觸發(新舊寬度剛好相同時事件不會發生，
        /// 用事件等待可能整個掛住)，兩個 frame 後 Yoga 一定至少跑過一次完整版面重算。
        /// </summary>
        private async UniTask WaitForLayoutAsync()
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            await UniTask.Yield(PlayerLoopTiming.Update);
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
                    if (evt.GamePuzzle.BranchNodeIds != null)
                        foreach (var id in evt.GamePuzzle.BranchNodeIds)
                            if (!string.IsNullOrEmpty(id)) yield return id;
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
