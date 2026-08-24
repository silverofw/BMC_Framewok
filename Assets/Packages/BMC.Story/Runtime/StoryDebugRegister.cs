using BMC.Core;
using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BMC.Story
{
    public static class StoryDebugRegister
    {
        public static void Init()
        {
            // 訂閱註冊事件
            DebugPanel.OnRegisterGroups -= RegisterGroups;
            DebugPanel.OnRegisterGroups += RegisterGroups;
        }

        private static void RegisterGroups(IDebugGroupHost panel)
        {
            panel.AddDebugGroup(
                "Story",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("Story", () => {
                        UIMgr.Instance.ShowPanel<StoryPanel>().Forget();
                    }),
                    ("LINE", () => {
                        UIMgr.Instance.ShowPanel<StoryLinePanel>().ContinueWith(p => {

                        }).Forget();
                    }
            )
            );
        }
    }
}