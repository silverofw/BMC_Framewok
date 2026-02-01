using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening; // 引入 DOTween
namespace BMC.UI
{
    public class MultiListView : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("對應的 ScrollRect")]
        [SerializeField]
        private ScrollRect scrollRect;

        [Tooltip("回收物件的放置節點")]
        [SerializeField]
        private Transform poolRoot;

        [Tooltip("列表項目的預製物 (Prefab)")]
        [SerializeField]
        private RectTransform PageObj;

        [Header("Layout")]
        [Tooltip("一行有幾個項目")]
        [SerializeField]
        [Range(1, 100)] // 防止設定為 0 造成除以零錯誤
        private int ScrollRow = 2;

        [Tooltip("單個項目的大小 (寬, 高)")]
        [SerializeField]
        private Vector2 ScrollSize = new Vector2(300, 400);

        [Tooltip("項目之間的間距 (X: 水平間距, Y: 垂直間距)")]
        [SerializeField]
        public Vector2 Spacing = Vector2.zero;

        [Header("Data")]
        [Tooltip("總資料筆數")]
        [SerializeField]
        public int DataCount = 10;

        [Tooltip("畫面可見的行數 (緩衝區)")]
        [SerializeField]
        public int ShowLineCount = 3;

        // 狀態變數
        private bool isInit;
        private int _pageCount;
        private RectTransform[] _pages = new RectTransform[0];
        private List<RectTransform> _poolPages = new List<RectTransform>();

        // 緩存變數
        private RectTransform _viewportRect;
        private RectTransform ViewportRect
        {
            get
            {
                if (_viewportRect == null && scrollRect != null)
                    _viewportRect = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.GetComponent<RectTransform>();
                return _viewportRect;
            }
        }

        private float _chunkHeight;
        private Tween _scrollTween;

        // 輔助屬性：單元格實際佔用的空間 (大小 + 間距)
        private Vector2 CellSize => ScrollSize + Spacing;

        // 事件
        public Action<RectTransform, int> onItemUpdate;

        void Start()
        {
            if (PageObj != null)
                PageObj.gameObject.SetActive(false);

            scrollRect.onValueChanged.AddListener(OnScroll);

            if (!isInit)
                Refresh(DataCount);
        }

        private void OnDestroy()
        {
            // 清理監聽與動畫，防止內存洩漏
            if (scrollRect != null)
                scrollRect.onValueChanged.RemoveListener(OnScroll);

            _scrollTween?.Kill();

            // 清理所有生成的物件引用
            _pages = null;
            _poolPages.Clear();
        }

        public void Refresh(int dataCount)
        {
            // 1. 回收所有當前顯示的 Page
            for (int i = 0, len = _pages.Length; i < len; i++)
            {
                if (_pages[i] != null) Recycle(_pages[i]);
            }

            DataCount = dataCount;

            // 2. 初始化 Content 參數
            var content = scrollRect.content;
            content.pivot = new Vector2(0.5f, 1);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.anchoredPosition = Vector2.zero;

            // 計算 Content 總高度
            int totalRows = Mathf.CeilToInt((float)DataCount / ScrollRow);
            float totalHeight = totalRows * CellSize.y;
            if (totalRows > 0) totalHeight -= Spacing.y; // 扣掉最後一行多餘的間距

            content.sizeDelta = new Vector2(0, totalHeight);
            scrollRect.enabled = DataCount > ScrollRow * (ShowLineCount - 1);

            // 3. 準備 Pool 與 Page 陣列
            _pageCount = ScrollRow * (ShowLineCount + 1);
            _chunkHeight = (_pageCount / ScrollRow) * CellSize.y;

            if (_pages.Length != _pageCount)
            {
                _pages = new RectTransform[_pageCount];
            }

            // 4. 生成初始項目
            for (int i = 0; i < _pageCount; i++)
            {
                var index = i;
                _pages[index] = GetNewPage();
                _pages[index].SetParent(content, false);
                _pages[index].name = $"PAGE_{index}";
                _pages[index].sizeDelta = ScrollSize;

                _pages[index].localPosition = GetLocalPositionByIndex(index);

                UpdateItemContent(_pages[index], index);
            }

            isInit = true;
        }

        public void UpdateItems()
        {
            foreach (var page in _pages)
            {
                if (page == null) continue;
                int index = GetIndex(page);
                UpdateItemContent(page, index);
            }
        }

        private void UpdateItemContent(RectTransform page, int index)
        {
            if (index < DataCount && index >= 0)
            {
                page.gameObject.SetActive(true);
                onItemUpdate?.Invoke(page, index);
            }
            else
            {
                page.gameObject.SetActive(false);
            }
        }

        public void OnScroll(Vector2 value)
        {
            if (!isInit || _pages.Length == 0) return;

            float contentY = scrollRect.content.anchoredPosition.y;
            float currentViewHeight = ViewportRect != null ? ViewportRect.rect.height : 0f;

            // 緩衝區設定
            float buffer = ScrollSize.y * 0.6f;
            float viewTop = -contentY + buffer;
            float viewBottom = -contentY - currentViewHeight - buffer;

            foreach (var page in _pages)
            {
                if (page == null) continue;

                float pageY = page.localPosition.y;
                bool changed = false;

                // 安全計數器，防止極端情況下的無窮迴圈
                int safetyCounter = 0;
                const int MAX_LOOP = 100;

                // 往下補位
                if (pageY > viewTop)
                {
                    while (pageY > viewTop && safetyCounter++ < MAX_LOOP)
                    {
                        float nextY = pageY - _chunkHeight;
                        if (nextY <= -scrollRect.content.sizeDelta.y - CellSize.y)
                            break;

                        pageY = nextY;
                        changed = true;
                    }
                }
                // 往上補位
                else if (pageY < viewBottom)
                {
                    while (pageY < viewBottom && safetyCounter++ < MAX_LOOP)
                    {
                        float nextY = pageY + _chunkHeight;
                        if (nextY > CellSize.y / 2)
                            break;

                        pageY = nextY;
                        changed = true;
                    }
                }

                if (changed)
                {
                    page.localPosition = new Vector3(page.localPosition.x, pageY, 0);
                    UpdateItemContent(page, GetIndex(page));
                }
            }
        }

        /// <summary>
        /// 根據座標反推資料 Index
        /// </summary>
        int GetIndex(RectTransform rectTransform)
        {
            float posY = -rectTransform.localPosition.y - (ScrollSize.y / 2);
            int row = Mathf.FloorToInt(posY / CellSize.y);

            float totalRowWidth = (ScrollRow * ScrollSize.x) + ((ScrollRow - 1) * Spacing.x);
            float posX = rectTransform.localPosition.x + (totalRowWidth / 2) - (ScrollSize.x / 2);
            int col = Mathf.RoundToInt(posX / CellSize.x);

            return (row * ScrollRow) + col;
        }

        /// <summary>
        /// 根據 Index 計算標準座標
        /// </summary>
        private Vector2 GetLocalPositionByIndex(int index)
        {
            int row = index / ScrollRow;
            int col = index % ScrollRow;

            float totalRowWidth = (ScrollRow * ScrollSize.x) + ((ScrollRow - 1) * Spacing.x);

            float x = (col * CellSize.x) - (totalRowWidth / 2) + (ScrollSize.x / 2);
            float y = -ScrollSize.y / 2 - (CellSize.y * row);

            return new Vector2(x, y);
        }

        RectTransform GetNewPage()
        {
            RectTransform page;
            if (_poolPages.Count > 0)
            {
                // List 移除最後一個元素效能最佳 (O(1))
                int lastIndex = _poolPages.Count - 1;
                page = _poolPages[lastIndex];
                _poolPages.RemoveAt(lastIndex);
            }
            else
            {
                page = Instantiate(PageObj, scrollRect.content);
            }
            return page;
        }

        void Recycle(RectTransform page)
        {
            page.SetParent(poolRoot);
            page.gameObject.SetActive(false);
            _poolPages.Add(page);
        }

        /// <summary>
        /// 滾動到指定索引
        /// </summary>
        public void ScrollToIndex(int index, bool useAnimation = true, float duration = 0.5f, float alignment = 0.5f)
        {
            if (!isInit || index < 0 || index >= DataCount) return;

            if (ViewportRect != null && ViewportRect.rect.height == 0)
            {
                Canvas.ForceUpdateCanvases();
            }

            float currentViewportHeight = ViewportRect != null ? ViewportRect.rect.height : 0f;

            int row = index / ScrollRow;
            float itemTopY = row * CellSize.y;

            // 計算目標 Scroll 位置 (Alignment: 0=頂部, 0.5=中間, 1=底部)
            float targetY = (itemTopY + ScrollSize.y * alignment) - (currentViewportHeight * alignment);

            float maxScrollY = scrollRect.content.sizeDelta.y - currentViewportHeight;
            targetY = Mathf.Clamp(targetY, 0, Mathf.Max(0, maxScrollY));

            scrollRect.StopMovement();
            _scrollTween?.Kill();

            if (useAnimation)
            {
                _scrollTween = scrollRect.content.DOAnchorPosY(targetY, duration)
                    .SetEase(Ease.OutCubic)
                    .OnUpdate(() => OnScroll(Vector2.zero));
            }
            else
            {
                Vector2 pos = scrollRect.content.anchoredPosition;
                pos.y = targetY;
                scrollRect.content.anchoredPosition = pos;
                OnScroll(Vector2.zero);
            }
        }
    }
}