using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版多欄虛擬化列表，對應 uGUI 版 BMC.UI 的 MultiListView。
    ///
    /// 實作策略與 uGUI 版不同：uGUI 版自己維護物件池、依捲動位置手動搬移每個項目；
    /// UI Toolkit 內建的 ListView 已經有虛擬化與回收，但只支援單欄，
    /// 因此這裡讓 ListView 的每個項目代表「一列」，列內再橫排 scrollRow 個儲存格。
    /// 回收與捲動交給 ListView，本類別只負責格線索引換算與資料綁定。
    /// </summary>
    [UxmlElement]
    public partial class MultiListView : VisualElement
    {
        /// <summary>一列有幾個項目</summary>
        [UxmlAttribute]
        public int scrollRow { get; set; } = 2;

        /// <summary>
        /// 單一項目寬度。設為 0 或負值時改為等分列寬——
        /// 欄數會動態變動時用這個，儲存格會自動收放而不會撐出列表之外。
        /// </summary>
        [UxmlAttribute]
        public float itemWidth { get; set; } = 300f;

        /// <summary>單一項目高度</summary>
        [UxmlAttribute]
        public float itemHeight { get; set; } = 120f;

        /// <summary>項目水平間距</summary>
        [UxmlAttribute]
        public float spacingX { get; set; } = 0f;

        /// <summary>項目垂直間距</summary>
        [UxmlAttribute]
        public float spacingY { get; set; } = 0f;

        private int showLineCountValue;

        /// <summary>
        /// 畫面上要顯示幾列，對應 uGUI 版的 ShowLineCount。
        /// 設定後會直接決定本控制項的高度（列數 × 列高）。
        ///
        /// 設為 0 或負值則不介入高度，改由外部 USS 決定——
        /// 需要讓列表填滿剩餘空間（flex-grow）時用這種方式。
        ///
        /// 注意：uGUI 版的 ShowLineCount 同時決定物件池大小，
        /// 這裡的回收由 ListView 自行處理，所以本屬性單純只影響顯示高度。
        /// </summary>
        [UxmlAttribute]
        public int showLineCount
        {
            get => showLineCountValue;
            set
            {
                showLineCountValue = value;
                ApplyViewHeight();
            }
        }

        /// <summary>
        /// 是否可以直接拖曳內容捲動。
        /// UI Toolkit 的 ScrollView 預設只吃滾輪與捲軸，滑鼠在內容上拖曳不會平移；
        /// uGUI 的 ScrollRect 則是拖曳驅動的。開啟此項以取得與 uGUI 版一致的操作手感。
        /// </summary>
        [UxmlAttribute]
        public bool dragToScroll { get; set; } = true;

        /// <summary>
        /// 判定為拖曳的位移門檻（像素）。未超過此距離的按放仍視為點擊。
        /// 預設值比照 uGUI 版 BMC.UI.UIButton 的 clickTolerance。
        /// </summary>
        [UxmlAttribute]
        public float dragThreshold { get; set; } = 10f;

        /// <summary>總資料筆數</summary>
        public int DataCount { get; private set; }

        /// <summary>
        /// 建立單一儲存格的外觀。未指定時會建立空的 VisualElement。
        /// 對應 uGUI 版由 Inspector 指派的 PageObj 預製物。
        /// </summary>
        public Func<VisualElement> makeItem;

        /// <summary>
        /// 綁定資料到儲存格，參數為 (儲存格, 資料索引)。
        /// 對應 uGUI 版的 onItemUpdate，只是型別由 RectTransform 換成 VisualElement。
        /// </summary>
        public Action<VisualElement, int> onItemUpdate;

        private readonly ListView listView;
        private int[] rowSource = Array.Empty<int>();
        private IVisualElementScheduledItem scrollAnimation;

        private ScrollView cachedScrollView;
        private Vector2 dragStartPointer;
        private float dragStartOffsetY;
        private int dragPointerId = -1;
        private bool isPointerDown;
        private bool isDragging;

        /// <summary>
        /// ListView 沒有公開內部的 ScrollView，只能查詢取得；取不到時相關功能自動略過。
        /// </summary>
        private ScrollView ScrollViewRef => cachedScrollView ??= listView?.Q<ScrollView>();

        /// <remarks>
        /// 本控制項不會自行決定尺寸：行內樣式的優先度高於 USS，
        /// 若在這裡寫死 flex-grow，使用端就無法用樣式指定固定高度。
        /// 請由外部以 USS 給它高度或 flex-grow，行為與 ScrollView／ListView 一致。
        /// </remarks>
        public MultiListView()
        {
            listView = new ListView
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                showBorder = false,
            };
            listView.makeItem = MakeRow;
            listView.bindItem = BindRow;
            listView.style.flexGrow = 1f;
            // flex 子元素預設不會縮到小於內容高度。資料量一大，內部 ListView 就會
            // 被自己的內容撐開、把同層的其他 UI 擠出版面，必須明確歸零。
            listView.style.minHeight = 0f;
            Add(listView);

            // 用 TrickleDown 在儲存格之前先看到事件，才能在超過門檻時攔截為拖曳
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(_ => ResetDrag());
        }

        #region 拖曳捲動

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!dragToScroll || evt.button != 0)
                return;

            var scrollView = ScrollViewRef;
            if (scrollView == null)
                return;

            // 使用者接手操作，停掉進行中的捲動動畫
            scrollAnimation?.Pause();
            scrollAnimation = null;

            isPointerDown = true;
            isDragging = false;
            dragPointerId = evt.pointerId;
            dragStartPointer = evt.position;
            dragStartOffsetY = scrollView.scrollOffset.y;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!isPointerDown || evt.pointerId != dragPointerId)
                return;

            var scrollView = ScrollViewRef;
            if (scrollView == null)
                return;

            float delta = evt.position.y - dragStartPointer.y;

            if (!isDragging)
            {
                if (Mathf.Abs(delta) < dragThreshold)
                    return;

                isDragging = true;

                // 擷取指標後續事件：儲存格收不到 PointerUp，就不會合成出 ClickEvent，
                // 因此拖曳結束時不會誤觸點擊。門檻內的按放則完全不受影響。
                this.CapturePointer(evt.pointerId);
            }

            // UI Toolkit 的 y 軸向下為正，scrollOffset.y 增加代表內容往上捲，
            // 因此指標往上移（delta 為負）要讓 offset 變大。
            var offset = scrollView.scrollOffset;
            offset.y = ClampScrollY(scrollView, dragStartOffsetY - delta);
            scrollView.scrollOffset = offset;

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
        /// 夾在可捲動範圍內。直接寫入 scrollOffset 不保證會被裁切，
        /// 不夾住的話可以一路拖到內容之外。
        /// </summary>
        private static float ClampScrollY(ScrollView scrollView, float value)
        {
            var scroller = scrollView.verticalScroller;
            if (scroller == null)
                return value;

            return Mathf.Clamp(value, scroller.lowValue, Mathf.Max(scroller.lowValue, scroller.highValue));
        }

        #endregion

        /// <summary>
        /// 設定資料筆數並重建列表，對應 uGUI 版的 Refresh。
        /// </summary>
        public void Refresh(int dataCount)
        {
            DataCount = Mathf.Max(0, dataCount);

            int columns = Mathf.Max(1, scrollRow);
            int rows = Mathf.CeilToInt((float)DataCount / columns);

            // ListView 只看 itemsSource 的筆數，內容本身用不到，
            // 這裡用列索引陣列當來源即可。
            rowSource = new int[rows];
            for (int i = 0; i < rows; i++)
                rowSource[i] = i;

            // 固定列高必須含垂直間距，否則捲動位置與實際版面會逐列累積誤差
            listView.fixedItemHeight = Mathf.Max(1f, itemHeight + spacingY);
            listView.itemsSource = rowSource;

            // 用 Rebuild 而非 RefreshItems：欄數或尺寸可能已變，
            // 既有的列元素結構需要整個重建。
            listView.Rebuild();

            // 列高可能隨 itemHeight／spacingY 改變，重新套一次顯示高度
            ApplyViewHeight();
        }

        /// <summary>
        /// 依 showLineCount 換算並套用控制項高度。
        /// </summary>
        private void ApplyViewHeight()
        {
            if (showLineCountValue <= 0)
            {
                // 交還給樣式表決定，不留下行內樣式蓋掉 USS
                style.height = StyleKeyword.Null;
                return;
            }

            style.height = showLineCountValue * Mathf.Max(1f, itemHeight + spacingY);
        }

        /// <summary>
        /// 重新綁定目前顯示中的項目，資料內容變動但筆數不變時使用。
        /// 對應 uGUI 版的 UpdateItems。
        /// </summary>
        public void UpdateItems() => listView.RefreshItems();

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("bmc-multilist__row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = itemHeight;
            row.style.marginBottom = spacingY;

            int columns = Mathf.Max(1, scrollRow);
            for (int i = 0; i < columns; i++)
            {
                var cell = makeItem != null ? makeItem() : new VisualElement();
                cell.style.height = itemHeight;

                if (itemWidth > 0f)
                {
                    cell.style.width = itemWidth;
                }
                else
                {
                    // flexBasis 歸零再平均分配剩餘空間，各欄才會真正等寬；
                    // 只給 flexGrow 的話會先扣掉各自的內容寬度，欄寬會不一致。
                    cell.style.flexGrow = 1f;
                    cell.style.flexBasis = 0f;
                }

                if (i > 0)
                    cell.style.marginLeft = spacingX;
                row.Add(cell);
            }
            return row;
        }

        private void BindRow(VisualElement row, int rowIndex)
        {
            int columns = Mathf.Max(1, scrollRow);
            for (int c = 0; c < row.childCount && c < columns; c++)
            {
                var cell = row[c];
                int dataIndex = (rowIndex * columns) + c;

                if (dataIndex < DataCount)
                {
                    cell.style.visibility = Visibility.Visible;
                    onItemUpdate?.Invoke(cell, dataIndex);
                }
                else
                {
                    // 用 visibility 而非 display：display:none 會讓元素退出版面，
                    // 最後一列不滿時剩餘欄位會跑版，visibility 則保留空位。
                    cell.style.visibility = Visibility.Hidden;
                }
            }
        }

        /// <summary>
        /// 捲動到指定資料索引，對應 uGUI 版的 ScrollToIndex。
        /// </summary>
        /// <param name="index">資料索引</param>
        /// <param name="useAnimation">是否播放捲動動畫</param>
        /// <param name="duration">動畫秒數</param>
        public void ScrollToIndex(int index, bool useAnimation = true, float duration = 0.5f)
        {
            if (index < 0 || index >= DataCount)
                return;

            int rowIndex = index / Mathf.Max(1, scrollRow);

            scrollAnimation?.Pause();
            scrollAnimation = null;

            var scrollView = listView.Q<ScrollView>();
            if (!useAnimation || duration <= 0f || scrollView == null)
            {
                listView.ScrollToItem(rowIndex);
                return;
            }

            float from = scrollView.scrollOffset.y;
            float to = ClampScrollY(scrollView, rowIndex * listView.fixedItemHeight);
            float startTime = Time.unscaledTime;

            // uGUI 版用 DOTween 做這段補間；這裡改用 UI Toolkit 的排程器，
            // 讓本套件維持不相依 DOTween。
            scrollAnimation = schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - startTime) / duration);
                // 對應 uGUI 版的 Ease.OutCubic
                float eased = 1f - Mathf.Pow(1f - t, 3f);

                var offset = scrollView.scrollOffset;
                offset.y = Mathf.Lerp(from, to, eased);
                scrollView.scrollOffset = offset;

                if (t >= 1f)
                {
                    scrollAnimation?.Pause();
                    scrollAnimation = null;
                }
            }).Every(16);
        }
    }
}
