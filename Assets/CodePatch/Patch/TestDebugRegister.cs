using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;
namespace BMC.AI
{
    public static class TestDebugRegister
    {
        public static void Init()
        {
            // 訂閱註冊事件
            DebugPanel.OnRegisterGroups -= RegisterGroups;
            DebugPanel.OnRegisterGroups += RegisterGroups;
        }

        private static void RegisterGroups(DebugPanel panel)
        {
            panel.AddDebugGroup(
                "TEST",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("CaptureCardPlayer", () => UIMgr.Instance.ShowPanel<CaptureCardPlayer>().Forget())
            );
        }
    }
}