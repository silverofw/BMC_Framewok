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
                    ("CaptureCardPlayer", () => UIMgr.Instance.ShowPanel<CaptureCardPlayer>().Forget()),
                    ("SW A", () => SwitchControllerHelper.PressButton_A()),
                    ("SW B", () => SwitchControllerHelper.PressButton_B()),
                    ("SW X", () => SwitchControllerHelper.PressButton_X()),
                    ("SW Y", () => SwitchControllerHelper.PressButton_Y()),
                    ("SW HOME", () => SwitchControllerHelper.PressButton_HOME()),
                    ("SW UP", () => SwitchControllerHelper.PressButton_UP()),
                    ("SW DOWN", () => SwitchControllerHelper.PressButton_DOWN()),
                    ("SW LEFT", () => SwitchControllerHelper.PressButton_LEFT()),
                    ("SW RIGHT", () => SwitchControllerHelper.PressButton_RIGHT())
            );
        }
    }
}