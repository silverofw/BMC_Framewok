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
        /// 拖到邊緣外之後，內容最多還能被拉開的距離(像素)，也就是阻尼函式的漸近上限。
        /// 阻尼函式：applied = maxOvershoot * over / (over + maxOvershoot)。
        /// over = 0 附近的斜率是 1(對 over 微分得 maxOvershoot^2 / (over + maxOvershoot)^2，代 0 剛好是 1)，
        /// 所以一開始完全跟手、不會有鈍感；之後漸趨平緩，over 再怎麼大也拖不過 maxOvershoot。
        /// </summary>
        public float maxOvershoot = 80f;

        /// <summary>
        /// 放手後回彈的指數衰減係數(每秒)。每幀做 Lerp(current, 0, 1 - exp(-springDecay * dt))，
        /// 收斂時間是 ln(起始位移 / 收斂門檻) / springDecay，與幀率無關(用的是 unscaledDeltaTime)。
        /// 14f 把 maxOvershoot 預設的 80px 收到 0.5px 以內約需 ln(80 / 0.5) / 14 ≈ 0.36 秒；
        /// 要更俐落就調大，想抓 0.2 秒的話是 ln(160) / 0.2 ≈ 25.4。
        /// </summary>
        public float springDecay = 14f;

        /// <summary>
        /// 目前疊在 contentRoot 上的「超出量」，量的是 scrollOffset 的座標系(正值 = 使用者還想往
        /// 右/下再多捲一點)，實際寫進 style.translate 時會取負，推導見 SetOvershoot。
        ///
        /// 這一段位移刻意不走 scrollView.scrollOffset ——「超出邊緣的位移」沒辦法用 scrollOffset
        /// 表達。實測 Unity 6000.5.3f1 的 UnityEngine.UIElementsModule：
        ///   ScrollView.scrollOffset 的 setter 是
        ///       horizontalScroller.value = value.x; verticalScroller.value = value.y;
        ///       m_ScrollOffset = new Vector2(horizontalScroller.value, verticalScroller.value);
        ///   —— 寫進去之後又從 scroller 把值讀回來；而 Scroller.value → BaseSlider&lt;T&gt;.value 是
        ///       TValueType val = (clamped ? GetClampedValue(value) : value);
        ///   clamped 預設 true，GetClampedValue 最後就是 Clamp(newValue, lowValue, highValue)。
        /// 所以超出範圍的值在 setter 裡就被夾掉了，讀回來永遠停在邊緣。
        /// </summary>
        private Vector2 overshootOffset;

        /// <summary>回彈用的排程項目。只建立一次，之後靠 Resume / Pause 重複使用。</summary>
        private IVisualElementScheduledItem springItem;

        /// <summary>回彈收斂門檻的平方值：位移小於 0.5px 人眼看不出來，直接歸零收工。</summary>
        private const float SpringEpsilonSqr = 0.25f;

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

            // 回彈動畫還在跑的時候又按下去：先停掉排程，並且從「當下看得到的位置」接著拖，
            // 不要瞬間跳回 0。OnPointerMove 算的是 raw = dragStartOffset - delta，按下那一瞬間
            // delta 是 0，所以 dragStartOffset 必須等於當下的視覺位置
            // (scrollOffset + 還沒彈完的超出量)，否則第一個 move 事件就會把超出量算成 0、閃一下。
            //
            // 這裡要加回去的是「阻尼前的原始超出量」，不是 translate 上的值：translate 存的是
            // applied = M*o/(o+M)，直接拿它當起點會再被阻尼一次、變成 f(applied) < applied，
            // 還是會往回縮一小段。用反函式 o = M*a/(M-a) 換算回去，第一個 move 事件算出來的
            // applied 才會剛好等於當下的 translate，完全無縫。
            StopSpringBack();
            dragStartOffset = scrollView.scrollOffset + new Vector2(
                InverseDampOvershoot(overshootOffset.x),
                InverseDampOvershoot(overshootOffset.y));
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

            // raw 是「完全跟手、不受任何限制」的目標捲動位置；clamped 是夾回可捲動範圍之後的值。
            // 兩者的差就是超出邊緣的量，只有真的拖過頭才非零，平常一路都是 (0, 0)。
            Vector2 raw = dragStartOffset - delta;
            Vector2 clamped = ClampScrollOffset(raw);
            scrollView.scrollOffset = clamped;
            ApplyOvershoot(raw - clamped);

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

            // 回彈統一掛在這裡，不是只掛在 OnPointerUp：PointerCaptureOutEvent 也走這條路
            // (指標被別的元素搶走、面板被關掉等等)，不這樣寫的話那些情況下超出量會卡住不彈回。
            // 門檻內的按放不會產生超出量，StartSpringBack 會直接什麼都不做。
            StartSpringBack();
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

        /// <summary>
        /// 兩軸的最大可捲動距離。刻意不去改 ClampScrollOffset(它的夾值算法要保持原樣)，
        /// 這裡單獨再算一份，給「該軸到底有沒有捲動空間」判斷用。
        /// </summary>
        private Vector2 MaxScrollOffset()
        {
            var viewport = scrollView.contentViewport.resolvedStyle;
            var content = contentRoot.resolvedStyle;
            return new Vector2(
                Mathf.Max(0f, content.width - viewport.width),
                Mathf.Max(0f, content.height - viewport.height));
        }

        /// <summary>
        /// 把超出量經過阻尼之後疊到 contentRoot 上。
        /// </summary>
        private void ApplyOvershoot(Vector2 overshoot)
        {
            // 只有「該軸真的有捲動空間」才給阻尼回彈。章節節點少的時候垂直根本捲不動
            // (maxScrollY == 0)，這時若也給回彈，任何略帶斜角的水平拖曳都會順便上下晃一下，
            // 手感變鬆。等同 iOS UIScrollView 的 alwaysBounceVertical 預設關閉。
            Vector2 maxScroll = MaxScrollOffset();
            if (maxScroll.x <= 0f)
                overshoot.x = 0f;
            if (maxScroll.y <= 0f)
                overshoot.y = 0f;

            SetOvershoot(new Vector2(DampOvershoot(overshoot.x), DampOvershoot(overshoot.y)));
        }

        /// <summary>
        /// 阻尼函式：applied = maxOvershoot * over / (over + maxOvershoot)。
        /// 逐軸處理，取絕對值算完再補回正負號(兩側對稱)。
        /// 性質：over = 0 時為 0、斜率 1(跟手)；單調遞增；over 趨近無限大時上限趨近 maxOvershoot。
        /// </summary>
        private float DampOvershoot(float over)
        {
            if (maxOvershoot <= 0f)
                return 0f;

            float abs = Mathf.Abs(over);
            if (abs <= 0f)
                return 0f;

            float applied = maxOvershoot * abs / (abs + maxOvershoot);
            return over < 0f ? -applied : applied;
        }

        /// <summary>
        /// DampOvershoot 的反函式：over = maxOvershoot * applied / (maxOvershoot - applied)。
        /// 給「回彈中途又按下去」用，把 translate 上的阻尼後數值換算回原始超出量，接續拖曳才不會跳。
        /// applied 恆小於 maxOvershoot(阻尼函式的上限)，不過還是夾一下分母，避免浮點誤差除到 0。
        /// </summary>
        private float InverseDampOvershoot(float applied)
        {
            if (maxOvershoot <= 0f)
                return 0f;

            float abs = Mathf.Min(Mathf.Abs(applied), maxOvershoot * 0.999f);
            if (abs <= 0f)
                return 0f;

            float over = maxOvershoot * abs / (maxOvershoot - abs);
            return applied < 0f ? -over : over;
        }

        /// <summary>
        /// 把超出量寫成 contentRoot 的 translate。
        ///
        /// 正負號推導：ScrollView 本身就是用「translate = -scrollOffset」實作捲動的 ——
        /// 6000.5.3f1 的 ScrollView.UpdateContentViewTransform 裡寫的是
        ///     translate.x = this.RoundToPanelPixelSize(0f - vector.x);
        ///     translate.y = this.RoundToPanelPixelSize(0f - vector.y);
        /// 其中 vector 就是 scrollOffset。可見 scrollOffset 變大 → 內容往左/往上移動。
        /// 超出量的語意是「使用者還想再往同一個方向多捲一點」，方向跟 scrollOffset 一致，
        /// 所以也要取負：translate = (-applied.x, -applied.y)。
        /// 寫成正號的話，往右邊緣拖會讓內容往右跑(等於反而彈出去)，一眼就看得出是錯的。
        ///
        /// 為什麼用 translate 而不是改 layout：translate 不參與排版，不會觸發 Yoga 重算，
        /// 也就不會讓 ScrollView 重新夾一次 scrollOffset。
        /// connectionCanvas 是 contentRoot 的子物件，連線會跟著卡片一起位移，這是對的。
        /// </summary>
        private void SetOvershoot(Vector2 value)
        {
            overshootOffset = value;
            contentRoot.style.translate = new Translate(-value.x, -value.y);
        }

        /// <summary>
        /// 放手後開始回彈。完全沒有超出量時(例如門檻內的按放)直接返回，
        /// 連 inline translate 都不寫，節點點擊的行為跟改動前一模一樣。
        /// </summary>
        private void StartSpringBack()
        {
            if (overshootOffset == Vector2.zero)
                return;

            if (overshootOffset.sqrMagnitude < SpringEpsilonSqr)
            {
                SetOvershoot(Vector2.zero);
                return;
            }

            // 排程物件只建立一次 —— schedule.Execute(...).Every(16) 建立當下就是啟動狀態，
            // 所以第一次不用再 Resume；之後每次放手都重用同一顆，不會每次都 new 一個。
            if (springItem == null)
                springItem = schedule.Execute(StepSpringBack).Every(16);
            else
                springItem.Resume();
        }

        private void StopSpringBack()
        {
            if (springItem != null)
                springItem.Pause();
        }

        /// <summary>
        /// 指數衰減回 0。沒有用 USS transition 做這件事：這個 Unity 版本的 USS parser 連 calc()
        /// 都不支援(之前已經被坑過一次)，用 C# 排程既可控也可驗。
        /// </summary>
        private void StepSpringBack()
        {
            // dt 用 unscaledDeltaTime：這張地圖是 UI，不該被 Time.timeScale 影響 ——
            // 暫停選單把 timeScale 設成 0 的時候，回彈也要照樣跑完。
            float dt = Time.unscaledDeltaTime;
            Vector2 current = Vector2.Lerp(overshootOffset, Vector2.zero, 1f - Mathf.Exp(-springDecay * dt));

            if (current.sqrMagnitude < SpringEpsilonSqr)
            {
                SetOvershoot(Vector2.zero);
                StopSpringBack();
                return;
            }

            SetOvershoot(current);
        }

        /// <summary>
        /// 立刻把超出量歸零並停掉回彈排程。切換章節 / 重建節點 / ScrollToNode 都要呼叫，
        /// 否則上一次拖曳殘留的偏移會疊在新版面或新捲動位置上。
        /// </summary>
        private void ResetOvershoot()
        {
            StopSpringBack();
            if (overshootOffset != Vector2.zero)
                SetOvershoot(Vector2.zero);
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

            // 程式化捲動要落在精確位置，殘留的回彈偏移會讓畫面看起來偏掉；
            // 也必須停掉排程，否則接下來 await 的那兩個 frame 裡它會繼續把 translate 寫回去。
            ResetOvershoot();

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
            // 重建節點(切換章節、RefreshStoryLayout)之前先把拖曳殘留的回彈偏移歸零：
            // contentRoot.Clear() 只清子元素，inline 的 translate 會留著，不歸零就會偏到新版面上。
            // GenerateNodesBFS 一定是在這之後才跑，所以節點重建的入口由這裡一併涵蓋。
            ResetOvershoot();

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
