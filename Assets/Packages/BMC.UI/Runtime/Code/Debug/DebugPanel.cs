using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BMC.UI
{
    /// <summary>
    /// 全域除錯面板 (核心邏輯 - 純註冊版)
    /// </summary>
    public partial class DebugPanel : JoypadLRPanel
    {
        // -----------------------------------------------------------------------
        // 1. 統一註冊入口 (Static Event)
        // 無論是內部還是外部功能，都透過訂閱此事件來加入按鈕
        // -----------------------------------------------------------------------
        public static event Action<DebugPanel> OnRegisterGroups;

        public override bool maskControl => true;

        protected override void Show()
        {
            // 每次開啟前先清空舊資料 (假設父類別沒有自動清空，若有則可省略)
            // actionDic.Clear(); 

            // 2. 觸發事件：讓所有訂閱者 (內部與外部) 執行註冊
            OnRegisterGroups?.Invoke(this);

            // 3. 排序：依據標題名稱 A-Z 進行排序
            // 假設 actionDic 內的元素有 Title 欄位，或為 Tuple/Record 的第一個成員
            // 若 actionDic 是 List<T>，這裡使用 Comparison 進行原地排序
            if (actionDic != null && actionDic.Count > 0)
            {
                // 注意：這裡假設 actionDic 的元素型別有一個名為 Title 或 Name 的屬性
                // 由於父類別 JoypadLRPanel 不可見，這裡示意使用 Linq OrderBy 重新指派
                // 請根據實際父類別結構調整，例如: x.Title 或 x.Item1
                actionDic = actionDic.OrderBy(x => x.Item1).ToList();
            }

            base.Show();
        }

        // -----------------------------------------------------------------------
        // 4. 開放 Public API 供註冊
        // -----------------------------------------------------------------------
        public void AddDebugGroup(string categoryTitle, params (string btnName, Action onClick)[] actions)
        {
            var delegateList = new List<Action<GameObject, int>>();
            foreach (var act in actions)
            {
                delegateList.Add((go, index) =>
                {
                    var item = go.GetComponent<JoypadItem>();
                    if (item != null) item.Init(act.btnName, act.onClick);
                });
            }

            // 加入至父類別的列表
            actionDic.Add(new(categoryTitle, delegateList));
        }
    }

    public static class GameDebugRegistrar
    {
        /// <summary>
        /// [RuntimeInitializeOnLoadMethod] 
        /// 遊戲啟動時自動執行，無需掛載 MonoBehaviour，也無需反射掃描
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            // 訂閱註冊事件
            DebugPanel.OnRegisterGroups += RegisterGameGroups;
        }

        private static void RegisterGameGroups(DebugPanel panel)
        {
            // 這些群組加入後，會被 DebugPanel 自動依標題排序

            // 例如 "A_Tools" 會排在 "B_Teleport" 前面
            panel.AddDebugGroup("MY FEATURE TITLE",
                ("Log", () => Log.Info("Hello")),
                ("Error", () => Log.Error("Error")),
                ("Toast", () => Toast.Show("Hello")),
                ("showMsg", () => {
                    UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug).ContinueWith((p) =>
                    {
                        p.Initial("Hello", "MSG");
                    }).Forget();
                })
            );
        }
    }
}