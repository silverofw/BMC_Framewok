using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版全域除錯面板，對應 uGUI 版 BMC.UI 的 DebugPanel。
    ///
    /// 註冊分兩層，開啟時都會收集：
    /// 1. BMC.UI.DebugPanel.OnRegisterGroups —— 與 F2 共用，COMMON／AUDIO／Story 等
    ///    專案分頁只掛在那裡，這裡不轉發的話 F4 就會只剩 UI Toolkit 自己的兩頁。
    /// 2. 本類別的 OnRegisterGroups —— 給只關心 UI Toolkit 面板的訂閱者。
    ///
    /// 操作方式同樣對齊 uGUI 版：繼承 JoypadPanel，方向鍵／十字鍵移動游標、
    /// A 執行、B 返回、LB／RB 換頁，輸入由 UIMgr 從 UIEvent.INPUT_* 轉發進來
    /// （預設來源為可選組件 BMC.UIToolkit.Joypad）。滑鼠點擊仍然可用。
    /// </summary>
    public class DebugPanel : JoypadPanel, BMC.UI.IDebugGroupHost
    {
        // -----------------------------------------------------------------------
        // 1. 統一註冊入口 (Static Event)
        // 無論是內部還是外部功能，都透過訂閱此事件來加入按鈕
        // -----------------------------------------------------------------------
        public static event Action<DebugPanel> OnRegisterGroups;

        public override bool maskControl => true;

        /// <summary>
        /// 每列欄數，對應 uGUI 版 DebugPanel prefab 的 gridWidth = 1。
        /// 右邊是單欄滿寬按鈕，上下移動一次一顆。
        /// </summary>
        protected override int gridWidth => 1;

        protected override ScrollView joypadScroll => itemScroll;

        private readonly List<(string title, List<(string name, Action onClick)> actions)> groups = new();

        private ScrollView itemScroll;
        private VisualElement tabList;
        private VisualElement itemGrid;
        private Label statusLabel;

        private readonly List<UIButton> tabButtons = new();

        /// <summary>目前分頁索引，對應 uGUI 版 JoypadLRPanel.selectedPageIndex</summary>
        private int selectedPageIndex;

        public static async UniTask<DebugPanel> Show(UILayer layer = UILayer.UI_Debug)
        {
            await UIMgr.Instance.EnsureRuntimeRootAsync();
            return await UIMgr.Instance.ShowPanel<DebugPanel>(layer);
        }

        protected override void OnInit()
        {
            // 面板本體要攔下點擊，否則會冒泡到遮罩觸發關閉
            var box = Root.Q<VisualElement>("box");
            box?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            tabList = Root.Q<VisualElement>("tab-list");
            itemScroll = Root.Q<ScrollView>("item-scroll");
            itemGrid = Root.Q<VisualElement>("item-grid");
            statusLabel = Root.Q<Label>("status");

            // 分頁鈕與項目都不讓 UI Toolkit 內建焦點導覽插手，
            // 手把操作一律只走 JoypadPanel 這一套。
            Root.Query<Button>().ForEach(b => b.focusable = false);

            groups.Clear();

            // 先收 F2 那份共用註冊（COMMON／AUDIO／UI BASIC／Story…），
            // 再收只掛在本類別上的訂閱。順序之後會依標題 A-Z 重排。
            BMC.UI.DebugPanel.CollectGroups(this);
            OnRegisterGroups?.Invoke(this);

            // 2. 排序：依據標題名稱 A-Z 進行排序
            if (groups.Count > 0)
            {
                var sorted = groups.OrderBy(g => g.title).ToList();
                groups.Clear();
                groups.AddRange(sorted);
            }

            BuildTabs();
            SelectPage(0);
        }

        // -----------------------------------------------------------------------
        // 3. 開放 Public API 供註冊
        // 簽名比照 uGUI 版 BMC.UI.DebugPanel.AddDebugGroup，
        // 同一份註冊程式碼可以原封不動掛到兩套 UI 上。
        // -----------------------------------------------------------------------
        public void AddDebugGroup(string categoryTitle, params (string btnName, Action onClick)[] actions)
        {
            var list = new List<(string, Action)>(actions.Length);
            foreach (var act in actions)
                list.Add((act.btnName, act.onClick));

            groups.Add((categoryTitle, list));
        }

        // -----------------------------------------------------------------------
        // 畫面建構
        // -----------------------------------------------------------------------

        /// <summary>
        /// 建立分頁鈕，對應 uGUI 版 JoypadLRPanel.InitDic 裡複製 pageItem 的那一段。
        /// </summary>
        private void BuildTabs()
        {
            if (tabList == null)
                return;

            tabList.Clear();
            tabButtons.Clear();

            for (int i = 0; i < groups.Count; i++)
            {
                int index = i;
                var tab = new UIButton { text = groups[i].title };
                tab.AddToClassList("bmc-debug-tab");

                // 分頁鈕不參與方向鍵選取（方向鍵是給右側按鈕列用的），
                // 也不讓它搶走焦點，但仍要能用滑鼠點。LB／RB 換頁。
                tab.focusable = false;
                tab.OnClick += () => SelectPage(index);

                tabList.Add(tab);
                tabButtons.Add(tab);
            }
        }

        /// <summary>
        /// 切換分頁並重建按鈕格，對應 uGUI 版 JoypadLRPanel.updateUI。
        /// </summary>
        private void SelectPage(int pageIndex)
        {
            if (itemGrid == null)
                return;

            selectedPageIndex = groups.Count > 0 ? Mathf.Clamp(pageIndex, 0, groups.Count - 1) : 0;

            itemGrid.Clear();
            joypadItems.Clear();

            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList("bmc-debug-tab--active", i == selectedPageIndex);

            if (groups.Count == 0)
            {
                SetStatus("尚未註冊任何除錯群組：請訂閱 DebugPanel.OnRegisterGroups 並呼叫 AddDebugGroup。");
                return;
            }

            var group = groups[selectedPageIndex];
            var items = new List<JoypadItem>(group.actions.Count);

            for (int i = 0; i < group.actions.Count; i++)
            {
                int index = i;
                var action = group.actions[i];

                var item = new JoypadItem();
                item.AddToClassList("bmc-button");
                item.AddToClassList("bmc-debug-item");

                // 滑鼠點擊與手把 A 鍵都走這裡：順便把游標移到被點的那一顆，
                // 之後改用手把才會從這個位置繼續移動。
                item.Init(action.name, () =>
                {
                    selectedItemIndex = index;
                    updateJoyItems();
                    action.onClick?.Invoke();
                });

                itemGrid.Add(item);
                items.Add(item);
            }

            // 交給 JoypadPanel 接管游標（選取樣式與捲動都在基底處理）
            InitJoyPad(items);

            SetStatus($"{group.title}：{group.actions.Count} 項（分頁 {selectedPageIndex + 1}/{groups.Count}）");
        }

        private void SetStatus(string msg)
        {
            if (statusLabel != null)
                statusLabel.text = msg;
        }

        // -----------------------------------------------------------------------
        // 輸入：格線移動與 A／B 由 JoypadPanel 處理，這裡只補上換頁
        // -----------------------------------------------------------------------

        public override void OnInputShoulderLeft() => MovePage(-1);

        public override void OnInputShoulderRight() => MovePage(1);

        /// <summary>
        /// 循環換頁，對應 uGUI 版的 OnInputShoulderLeft／OnInputShoulderRight。
        /// </summary>
        private void MovePage(int delta)
        {
            if (groups.Count == 0)
                return;

            int next = (selectedPageIndex + delta) % groups.Count;
            if (next < 0)
                next += groups.Count;

            SelectPage(next);
        }
    }
}
