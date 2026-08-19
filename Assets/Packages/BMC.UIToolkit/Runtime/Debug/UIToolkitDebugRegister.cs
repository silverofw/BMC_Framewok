using BMC.Core;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 在 BMC.UI 的全域除錯面板中掛上「UI TOOLKIT」分頁，用來測試本套件的基礎介面。
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
        /// <summary>
        /// 自動註冊。與其他 BMC 套件由 Entry 手動呼叫 Init 不同，
        /// 這裡採用 BMC.UI.GameDebugRegistrar 的作法自行掛載，
        /// 避免熱更新組件（CodePatch）為了除錯功能新增組件參照。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Init()
        {
            BMC.UI.DebugPanel.OnRegisterGroups -= RegisterGroups;
            BMC.UI.DebugPanel.OnRegisterGroups += RegisterGroups;
        }

        private static void RegisterGroups(BMC.UI.DebugPanel panel)
        {
            panel.AddDebugGroup(
                "UI TOOLKIT",
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
                ("MsgPanel on Scene Layer", () => ShowOnSceneLayer().Forget()),
                ("Create Scene Root", () => CreateSceneRoot().Forget()),
                ("Root Info", () => ReportRoot().Forget()),
                ("List Panels", ListPanels),
                ("Close All Panels", CloseAllPanels),
                ("Load Test Lang", LoadTestLang),
                ("Switch Language", SwitchLanguage)
            );
        }

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
