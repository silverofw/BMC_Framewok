using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// MultiListView 的測試面板。
    ///
    /// 放在除錯組件而非核心套件：它只是用來驗證列表行為的展示品，
    /// 不屬於框架要對外提供的通用介面。
    /// </summary>
    public class ListDemoPanel : UIPanel
    {
        public override bool maskControl => true;

        /// <summary>欄數上下限：低於 1 會除以零，過多則每格窄到看不出內容</summary>
        private const int MIN_COLUMNS = 1;
        private const int MAX_COLUMNS = 8;

        /// <summary>可見列數上下限：上限只是避免面板高過畫面，與資料量無關</summary>
        private const int MIN_VISIBLE_LINES = 1;
        private const int MAX_VISIBLE_LINES = 10;

        private MultiListView list;
        private Label statusLabel;
        private Label columnCountLabel;
        private Label rowCountLabel;
        private int dataCount;

        /// <summary>
        /// 目前選取的資料索引。刻意存「資料索引」而不是記住某個 VisualElement——
        /// 儲存格會被回收再利用，把狀態掛在元素上，捲動後就會套到別筆資料身上。
        /// </summary>
        private int selectedIndex = -1;

        public static async UniTask<ListDemoPanel> Show(UILayer layer = UILayer.UI_Top)
        {
            await UITMgr.Instance.EnsureRuntimeRootAsync();
            return await UITMgr.Instance.ShowPanel<ListDemoPanel>(layer);
        }

        protected override void OnInit()
        {
            var box = Root.Q<VisualElement>("box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            statusLabel = Root.Q<Label>("status");
            list = Root.Q<MultiListView>("list");
            if (list == null)
                return;

            // 建立儲存格外觀：對應 uGUI 版由 Inspector 指派的 PageObj
            list.makeItem = () =>
            {
                var cell = new VisualElement();
                cell.AddToClassList("bmc-listdemo-cell");

                var label = new Label { name = "label" };
                label.AddToClassList("bmc-listdemo-cell__label");
                cell.Add(label);

                // 點擊只在建立時註冊一次；若改在綁定時註冊，
                // 每次回收重綁都會再掛一個，最後一次點擊會觸發很多次。
                // 當下對應哪筆資料改由 userData 在綁定時寫入。
                cell.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.currentTarget is VisualElement target && target.userData is int index)
                        SelectIndex(index);
                });

                return cell;
            };

            // 綁定資料：對應 uGUI 版的 onItemUpdate
            list.onItemUpdate = (cell, index) =>
            {
                cell.userData = index;

                var label = cell.Q<Label>("label");
                if (label != null)
                    label.text = $"Item {index}";

                // 選取狀態由資料索引決定，捲動回來時才會正確還原
                cell.EnableInClassList("bmc-listdemo-cell--selected", index == selectedIndex);
            };

            columnCountLabel = Root.Q<Label>("col-count");
            rowCountLabel = Root.Q<Label>("row-count");

            BindButton("btn-50", () => SetCount(50));
            BindButton("btn-500", () => SetCount(500));
            BindButton("btn-scroll", ScrollRandom);

            BindButton("btn-col-minus", () => AddColumns(-1));
            BindButton("btn-col-plus", () => AddColumns(1));
            BindButton("btn-row-minus", () => AddVisibleLines(-1));
            BindButton("btn-row-plus", () => AddVisibleLines(1));

            SetCount(50);
        }

        private void BindButton(string name, System.Action action)
        {
            var button = Root.Q<UIButton>(name);
            if (button != null)
                button.OnClick += action;
        }

        private void SetCount(int count)
        {
            dataCount = UnityEngine.Mathf.Max(0, count);
            selectedIndex = -1;
            list.Refresh(dataCount);
            RefreshGridLabels();
            SetStatus($"資料 {dataCount} 筆 / 共 {TotalRowCount} 列");
        }

        /// <summary>
        /// 增減欄數。資料筆數不變，只改變每列放幾格，
        /// 因此列數會跟著反向變動——這正是驗證格線換算是否正確的地方。
        /// </summary>
        private void AddColumns(int delta)
        {
            int next = UnityEngine.Mathf.Clamp(list.scrollRow + delta, MIN_COLUMNS, MAX_COLUMNS);
            if (next == list.scrollRow)
                return;

            list.scrollRow = next;

            // 欄數變了，既有的列元素結構已經不對，必須整個重建
            list.Refresh(dataCount);
            RefreshGridLabels();
            SetStatus($"欄數 {next} / 共 {TotalRowCount} 列");
        }

        /// <summary>
        /// 增減畫面可見列數。這只改變列表的顯示高度，資料筆數完全不動——
        /// 對應 uGUI 版的 ShowLineCount。
        /// </summary>
        private void AddVisibleLines(int delta)
        {
            int next = UnityEngine.Mathf.Clamp(list.showLineCount + delta, MIN_VISIBLE_LINES, MAX_VISIBLE_LINES);
            if (next == list.showLineCount)
                return;

            list.showLineCount = next;
            RefreshGridLabels();
            SetStatus($"可見列 {next}（資料仍為 {dataCount} 筆）");
        }

        /// <summary>資料換算出的總列數，與可見列數是兩回事</summary>
        private int TotalRowCount =>
            list.scrollRow > 0 ? UnityEngine.Mathf.CeilToInt((float)dataCount / list.scrollRow) : 0;

        private void RefreshGridLabels()
        {
            if (columnCountLabel != null)
                columnCountLabel.text = list.scrollRow.ToString();
            if (rowCountLabel != null)
                rowCountLabel.text = list.showLineCount.ToString();
        }

        private void SelectIndex(int index)
        {
            selectedIndex = index;

            // 重綁目前顯示中的儲存格，讓選取樣式更新
            list.UpdateItems();

            SetStatus($"選取 Item {index}");
            Toast.Show($"clicked Item {index}");
        }

        private void ScrollRandom()
        {
            if (dataCount <= 0)
                return;

            int target = UnityEngine.Random.Range(0, dataCount);
            list.ScrollToIndex(target);
            SetStatus($"捲動至 Item {target}");
        }

        private void SetStatus(string msg)
        {
            if (statusLabel != null)
                statusLabel.text = msg;
        }
    }
}
