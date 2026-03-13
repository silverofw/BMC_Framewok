using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;
namespace BMC.AI
{
    public static class AIDebugRegister
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
                "AI",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("AgentPanel", () => UIMgr.Instance.ShowPanel<AgentPanel>().Forget())
            );
        }
    }
}