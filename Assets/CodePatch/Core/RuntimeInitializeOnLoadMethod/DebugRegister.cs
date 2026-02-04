using BMC.Core;
using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;

public static class CommonDebugRegister
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void Init()
    {
        // 訂閱註冊事件
        DebugPanel.OnRegisterGroups += panel => {
            panel.AddDebugGroup("COMMON",
                ("切換語言 英文", () => LocalMgr.Instance.Set(SystemLanguage.English)),
                ("切換語言 繁中", () => LocalMgr.Instance.Set(SystemLanguage.ChineseTraditional)),
                ("切換語言 簡中", () => LocalMgr.Instance.Set(SystemLanguage.ChineseSimplified)),
                ("切換語言 日文", () => LocalMgr.Instance.Set(SystemLanguage.Japanese)),
                ("刪除紀錄 0", () => {
                    SaveMgr.Instance.DeleteSlot(0);
                    UIMgr.Instance.GotoScene("Entry").Forget();
                }),
                ("返回登入大廳", () => UIMgr.Instance.GotoScene("Entry").Forget())
            );
        };
    }
}