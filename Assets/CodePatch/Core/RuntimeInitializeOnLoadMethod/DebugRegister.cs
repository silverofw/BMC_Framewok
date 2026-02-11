using BMC.Core;
using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;
namespace BMC.Patch.Core
{
    public static class CommonDebugRegister
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            // 訂閱註冊事件
            DebugPanel.OnRegisterGroups += panel => {
                panel.AddDebugGroup("COMMON",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("切換語言 英文", () => LocalMgr.Instance.Set(SystemLanguage.English)),
                    ("切換語言 繁中", () => LocalMgr.Instance.Set(SystemLanguage.ChineseTraditional)),
                    ("切換語言 簡中", () => LocalMgr.Instance.Set(SystemLanguage.ChineseSimplified)),
                    ("切換語言 日文", () => LocalMgr.Instance.Set(SystemLanguage.Japanese)),
                    ("讀取紀錄 0", () => {
                        SaveMgr.Instance.EnableDebugLogs = true;
                        SaveMgr.Instance.SwitchAndLoadSlot(0);
                    } ),
                    ("刪除紀錄 0", () => {
                        SaveMgr.Instance.DeleteSlot(0);
                        SceneMgr.Instance.GotoScene("Entry");
                    } ),
                    ("測試多語言(Continue)", () => {
                        LocalMgr.Instance.Load();
                        LocalMgr.Instance.Data = new ConfigLang();
                        Log.Info($"[{LocalMgr.Instance.CrtLang}] {LocalMgr.Instance.Local("Continue")}");
                    } ),             
                    ("重新運行遊戲", () => SceneMgr.Instance.GotoScene("Entry"))
                );
            };
        }
    }
}