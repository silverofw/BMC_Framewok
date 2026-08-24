using BMC.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 把本套件的測試功能掛上除錯面板，同時支援兩套 UI：
    /// uGUI 版的 BMC.UI.DebugPanel 與 UI Toolkit 版的 BMC.UIToolkit.DebugPanel。
    /// 兩者的 AddDebugGroup 簽名一致，因此分頁內容只需定義一份。
    ///
    /// 刻意獨立成 BMC.UIToolkit.Debug 組件：BMC.UI 會帶入 DOTween／TextMeshPro／InputSystem／
    /// UIEffect 等一整串依賴，核心的 BMC.UIToolkit 不應該為了除錯功能而背上這些。
    /// 不需要這個橋接時，整個 Runtime/Debug 資料夾刪掉即可，核心不受影響。
    ///
    /// 此處刻意不使用 using BMC.UI，因為兩個套件都有 UIMgr／Toast／MsgPanel 同名型別，
    /// 全部改為完整名稱以免解析到非預期的那一個。
    /// </summary>
    public static class UIToolkitDebugRegister
    {
        private const string GROUP_TOOLKIT = "UI TOOLKIT";
        private const string GROUP_SYSTEM = "UI SYSTEM";

        /// <summary>
        /// 自動註冊。與其他 BMC 套件由 Entry 手動呼叫 Init 不同，
        /// 這裡採用 BMC.UI.GameDebugRegistrar 的作法自行掛載，
        /// 避免熱更新組件（CodePatch）為了除錯功能新增組件參照。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Init()
        {
            // 只掛共用事件：UIT DebugPanel 開啟時會轉發同一個 Invoke，
            // 再訂一份本類別的 OnRegisterGroups 會讓 UI TOOLKIT／UI SYSTEM 出現兩次。
            BMC.UI.DebugPanel.OnRegisterGroups -= RegisterGroups;
            BMC.UI.DebugPanel.OnRegisterGroups += RegisterGroups;
        }

        /// <summary>
        /// 掛上 UI TOOLKIT／UI SYSTEM 分頁。F2 與 F4 都會走到這裡；
        /// 只有 uGUI 面板額外放「開 UIT 除錯面板」的入口（F4 自己就是那塊面板）。
        /// </summary>
        private static void RegisterGroups(BMC.UI.IDebugGroupHost panel)
        {
            var toolkit = new List<(string, Action)>(ToolkitActions());
            if (panel is BMC.UI.DebugPanel)
            {
                toolkit.Add(("DebugPanel (UIT)", () =>
                {
                    // 兩套 UI 走不同的算圖路徑，同時開著會疊在一起，
                    // 因此先關掉 uGUI 版的除錯面板，再開 UI Toolkit 版。
                    var ugui = BMC.UI.UIMgr.Instance;
                    ugui.closePanel(ugui.GetPanel<BMC.UI.DebugPanel>(), true, () => DebugPanel.Show().Forget());
                }));
            }

            panel.AddDebugGroup(GROUP_TOOLKIT, toolkit.ToArray());
            panel.AddDebugGroup(GROUP_SYSTEM, SystemActions());
        }

        /// <summary>
        /// 介面本身的展示：開得起來、長得對不對。
        /// </summary>
        private static (string, Action)[] ToolkitActions() => new (string, Action)[]
        {
            ("Toast", () => Toast.Show("Hello from UI Toolkit")),
            ("MsgPanel", () =>
            {
                MsgPanel.Show(
                    "這是 UI Toolkit 版的通用彈窗。",
                    "MsgPanel",
                    () => Toast.Show("confirm"),
                    () => Toast.Show("cancel")).Forget();
            }),
            ("MsgPanel (Confirm Only)", () =>
            {
                // 不帶 action 時只會留下單顆關閉鈕
                MsgPanel.Show("只有一顆按鈕的提示樣式。", "Notice").Forget();
            }),
            ("PreviewPanel", () => PreviewPanel.Show().Forget()),
            ("MultiListView", () => ListDemoPanel.Show().Forget()),
            ("ChatPanel", () => ChatPanel.Show().Forget()),
            ("Loading", () =>
            {
                // 讀取畫面會蓋住整個螢幕，先把除錯面板收起來再開，
                // 與 uGUI 版 GameDebugRegistrar 的作法一致。
                CloseDebugPanels(() =>
                {
                    LoadPanel.Show(async () =>
                    {
                        await UniTask.WaitForSeconds(1f);
                        LoadPanel.Instance.SetProgress(33, "p33");

                        await UniTask.WaitForSeconds(1f);
                        LoadPanel.Instance.SetProgress(66, "p66");

                        await UniTask.WaitForSeconds(1f);
                        LoadPanel.Instance.SetMaxProgress("p100");
                    });
                });
            }),
            ("Loading autoFinish", () =>
            {
                CloseDebugPanels(() =>
                {
                    LoadPanel.Show(async () => { await UniTask.WaitForSeconds(1f); }, null, true);
                });
            }),
        };

        /// <summary>
        /// 關掉兩套除錯面板後再執行動作。讀取畫面之類的全螢幕介面
        /// 不論從哪一套面板按下去，都不該讓面板留在底下。
        /// </summary>
        private static void CloseDebugPanels(Action next)
        {
            var uit = UIMgr.Instance.GetPanel<DebugPanel>();
            if (uit != null && !uit.IsClosed)
                uit.ClosePanel();

            var ugui = BMC.UI.UIMgr.Instance;
            var uguiPanel = ugui.GetPanel<BMC.UI.DebugPanel>();
            if (uguiPanel != null)
            {
                ugui.closePanel(uguiPanel, true, next);
                return;
            }

            next?.Invoke();
        }

        /// <summary>
        /// 底層機制的驗證：分層、根節點、面板清單與多語言。
        /// </summary>
        private static (string, Action)[] SystemActions() => new (string, Action)[]
        {
            ("MsgPanel on Scene Layer", () => ShowOnSceneLayer().Forget()),
            ("Create Scene Root", () => CreateSceneRoot().Forget()),
            ("Root Info", () => ReportRoot().Forget()),
            ("List Panels", ListPanels),
            ("Close All Panels", CloseAllPanels),
            ("Load Test Lang", LoadTestLang),
            ("Switch Language", SwitchLanguage),
            ("Log", () => Log.Info("Hello")),
            ("Error", () => Log.Error("Error")),
            ("FullScreen Switch", ToggleFullScreen),
        };

        /// <summary>
        /// 測試場景層：需要先有場景根節點，否則 GetLayer 會擋下來。
        /// </summary>
        private static async UniTaskVoid ShowOnSceneLayer()
        {
            await UIMgr.Instance.EnsureRuntimeRootAsync();
            if (!UIMgr.Instance.IsRootReady)
                return;

            if (!UIMgr.Instance.IsSceneInit)
                UIMgr.Instance.CreateSceneRoot();

            MsgPanel.Show("這則彈窗掛在場景層 SCENE_UI_1。", "Scene Layer",
                layer: UILayer.SCENE_UI_1).Forget();
        }

        private static async UniTaskVoid CreateSceneRoot()
        {
            await UIMgr.Instance.EnsureRuntimeRootAsync();
            if (!UIMgr.Instance.IsRootReady)
                return;

            UIMgr.Instance.CreateSceneRoot();
            Toast.Show("scene root created");
        }

        private static async UniTaskVoid ReportRoot()
        {
            await UIMgr.Instance.EnsureRuntimeRootAsync();
            Toast.Show($"root: {UIMgr.Instance.IsRootReady} / scene: {UIMgr.Instance.IsSceneInit}");
        }

        /// <summary>
        /// 列出目前開啟的面板。UI Toolkit 的面板不是 GameObject，Hierarchy 看不到，
        /// 只能靠這種方式或 Window &gt; UI Toolkit &gt; Debugger 觀察。
        /// </summary>
        private static void ListPanels()
        {
            var open = UIMgr.Instance.OpenPanels;
            if (open.Count == 0)
            {
                Toast.Show("no open panel");
                return;
            }

            var names = new string[open.Count];
            for (int i = 0; i < open.Count; i++)
                names[i] = $"{open[i].GetType().Name}({open[i].Layer})";

            string info = string.Join(", ", names);
            Log.Info($"[UIToolkit] open panels ({open.Count}): {info}");
            Toast.Show($"{open.Count} panel(s): {info}");
        }

        /// <summary>
        /// 載入除錯用語系表。
        /// 專案正式的 Luban 語系表沒有 UI 通用鍵值（確認／取消／關閉），
        /// 先按這顆再按 Switch Language，才看得到介面即時換字。
        /// </summary>
        private static void LoadTestLang()
        {
            LocalMgr.Instance.Load(new TestLangData(), LocalMgr.Instance.CrtLang);
            Toast.Show($"test lang loaded ({LocalMgr.Instance.CrtLang})");
        }

        /// <summary>
        /// 在幾種語言之間輪流切換，用來驗證開著的介面會不會即時換字。
        /// 未載入語系資料時 UILabel／UIButton 會保留原文，屆時畫面不會有變化，
        /// 因此這裡也把目前語言用 Toast 印出來，確認事件本身有發出。
        /// </summary>
        private static void SwitchLanguage()
        {
            var order = new[]
            {
                SystemLanguage.English,
                SystemLanguage.ChineseTraditional,
                SystemLanguage.ChineseSimplified,
                SystemLanguage.Japanese,
            };

            int index = System.Array.IndexOf(order, LocalMgr.Instance.CrtLang);
            var next = order[(index + 1) % order.Length];

            LocalMgr.Instance.Set(next);
            Toast.Show($"language: {next} (data ready: {LocalMgr.Instance.IsReady})");
        }

        private static void ToggleFullScreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            if (Screen.fullScreen)
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
            Toast.Show($"[{Screen.fullScreen}] fullScreen");
        }

        private static void CloseAllPanels()
        {
            // 先快照再關閉：關閉一個面板會連帶關閉它的子面板，
            // 直接走訪即時清單會在中途被改動而漏關或越界。
            var snapshot = new List<UIPanel>(UIMgr.Instance.OpenPanels);
            foreach (var panel in snapshot)
            {
                if (!panel.IsClosed)
                    panel.ClosePanel();
            }
        }
    }
}
