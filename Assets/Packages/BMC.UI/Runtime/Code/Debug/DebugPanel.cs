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
            OnRegisterGroups?.Invoke(this);

            // 3. 排序：依據標題名稱 A-Z 進行排序
            // 假設 actionDic 內的元素有 Title 欄位，或為 Tuple/Record 的第一個成員
            // 若 actionDic 是 List<T>，這裡使用 Comparison 進行原地排序
            if (actionDic != null && actionDic.Count > 0)
            {
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            // 訂閱註冊事件
            DebugPanel.OnRegisterGroups += panel => {
                panel.AddDebugGroup("UI BASIC",
                    ("Log", () => Log.Info("Hello")),
                    ("Error", () => Log.Error("Error")),
                    ("Toast", () => Toast.Show("Hello")),
                    ("showMsg", () => {
                        UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug).ContinueWith((p) =>
                        {
                            p.Initial("Hello", "MSG");
                        }).Forget();
                    }),
                    ("Loading", () => {
                        UIMgr.Instance.closePanel(UIMgr.Instance.GetPanel<DebugPanel>(), true, () => {
                            LoadPanel.Show(async () => {

                                await UniTask.WaitForSeconds(1f);
                                LoadPanel.Instance.SetProgress(33, "p20");

                                await UniTask.WaitForSeconds(1f);
                                LoadPanel.Instance.SetProgress(66, "p20");

                                await UniTask.WaitForSeconds(1f);
                                LoadPanel.Instance.SetMaxProgress("p20");
                            });
                        });
                    } ),
                    ("Loading autoFinish", () => {
                        UIMgr.Instance.closePanel(UIMgr.Instance.GetPanel<DebugPanel>(), true, () => {
                            LoadPanel.Show(async () => {
                                await UniTask.WaitForSeconds(1f);
                            }, null, true);
                        });
                    } ),
                    ("FullScreen Switch", () => {
                        Screen.fullScreen = !Screen.fullScreen;
                        if (Screen.fullScreen)
                            Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                        Toast.Show($"[{Screen.fullScreen}] fullScreen");
                    })
                );
            };
        }
    }
}