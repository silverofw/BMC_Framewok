using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq; // 必須引用 System.Linq 才能使用 OrderBy
using System.Reflection;
using UnityEngine;

namespace BMC.UI
{
    /// <summary>
    /// 全域除錯面板 (Debug Panel)
    /// <para>
    /// 核心架構：採用 Partial Class (分部類別) + Reflection (反射) 自動註冊機制。
    /// 開發者無需修改此主檔案即可新增除錯分頁。
    /// </para>
    /// 
    /// <para>【如何新增除錯分頁】：</para>
    /// <list type="number">
    ///     <item>建立一個新的腳本檔案，例如 <c>DebugPanel.Map.cs</c>。</item>
    ///     <item>類別宣告必須為 <c>public partial class DebugPanel</c>。</item>
    ///     <item>定義一個 <c>private void</c> 方法，名稱必須以 <c>"Group_"</c> 開頭。</item>
    ///     <item>命名規則建議使用編號排序：<c>Group_{00-99}_{模組名稱}</c> (系統會依名稱自動排序)。</item>
    ///     <item>在方法內呼叫 <c>AddDebugGroup()</c> 加入按鈕。</item>
    /// </list>
    /// </summary>
    public partial class DebugPanel : JoypadLRPanel
    {
        public override bool maskControl => true;

        protected override void Show()
        {
            // 執行自動註冊與排序
            RegisterAllPartialGroups();

            base.Show();
        }

        /// <summary>
        /// 使用反射 (Reflection) 掃描並執行所有分部類別中的註冊方法。
        /// <para>篩選條件：方法名稱以 "Group_" 開頭且無參數。</para>
        /// <para>排序邏輯：依方法名稱 (Name) 進行字串排序 (A-Z)。</para>
        /// </summary>
        private void RegisterAllPartialGroups()
        {
            var methods = this.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name.StartsWith("Group_") && m.GetParameters().Length == 0) // 篩選條件
                .OrderBy(m => m.Name); // 【關鍵修改】依名稱 A-Z 排序

            foreach (var method in methods)
            {
                try
                {
                    // 執行該分組方法
                    method.Invoke(this, null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DebugPanel] Error invoking {method.Name}: {e}");
                }
            }
        }

        // 建議命名規則：Group_{排序}_{名稱}
        // 00~10: 核心系統 / 11~50: 遊戲玩法 / 90+: 雜項
        private void Group_99_UI_Feature()
        {
            AddDebugGroup("MY FEATURE TITLE",
                ("Log", () => Log.Info("Hello")),
                ("Error", () => Log.Error("Error")),
                ("Toast", () => Toast.Show("Hello")),
                ("showMsg", () => {
                    UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug).ContinueWith((p) =>
                    {
                        p.Initial("Hello", "MSG");
                    }).Forget();
                }
            )
            );
        }

        // 封裝後的 helper (設為 protected 供 partial 使用)
        protected void AddDebugGroup(string categoryTitle, params (string btnName, Action onClick)[] actions)
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
            actionDic.Add(new(categoryTitle, delegateList));
        }
    }
}