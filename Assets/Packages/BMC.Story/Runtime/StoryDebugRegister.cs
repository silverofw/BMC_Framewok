using BMC.Core;
using BMC.UI;
using BMC.UIToolkit;
using Cysharp.Threading.Tasks;

namespace BMC.Story
{
    public static class StoryDebugRegister
    {
        public static void Init()
        {
            // 訂閱註冊事件：uGUI 版與 UI Toolkit 版的除錯面板共用同一個事件(見
            // BMC.UIToolkit.UIToolkitDebugRegister 的說明)，這裡只需要掛一次。
            DebugPanel.OnRegisterGroups -= RegisterGroups;
            DebugPanel.OnRegisterGroups += RegisterGroups;
        }

        private static void RegisterGroups(IDebugGroupHost panel)
        {
            panel.AddDebugGroup(
                "Story",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("Story", () => {
                        UITMgr.Instance.ShowPanel<StoryPanel>().Forget();
                    }),
                    ("LINE", () => {
                        UITMgr.Instance.ShowPanel<StoryLinePanel>().Forget();
                    }
            )
            );
        }
    }
}
