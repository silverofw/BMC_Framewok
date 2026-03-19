using BMC.Core;
using BMC.UI;
using UnityEngine;
using Cysharp.Threading.Tasks;
namespace BMC.Patch.Core
{
    public static class CommonDebugRegister
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
                "COMMON",
                    ("FPS", () => UIInputTrigger.ShowFPS = !UIInputTrigger.ShowFPS),
                    ("切換語言 英文", () => setLang(SystemLanguage.English)),
                    ("切換語言 繁中", () => setLang(SystemLanguage.ChineseTraditional)),
                    ("切換語言 簡中", () => setLang(SystemLanguage.ChineseSimplified)),
                    ("切換語言 日文", () => setLang(SystemLanguage.Japanese)),
                    ("讀取紀錄 0", () => {
                        SaveMgr.Instance.EnableDebugLogs = true;
                        SaveMgr.Instance.SwitchAndLoadSlot(0);
                    } ),
                    ("刪除紀錄 0", () => {
                        SaveMgr.Instance.DeleteSlot(0);
                        SceneMgr.Instance.GotoScene("Entry", false);
                    } ),
                    ("測試多語言(Continue)", () => {
                        var index = SaveMgr.Instance.GetCoreInt(LocalMgr.SC_LANGUAGE, (int)SystemLanguage.English);
                        LocalMgr.Instance.Load(new ConfigLang(), (SystemLanguage)index);
                        Log.Info($"[{LocalMgr.Instance.CrtLang}] {LocalMgr.Instance.Local("Continue")}");
                    } ),
                    ("重新運行遊戲", () => SceneMgr.Instance.GotoScene("Entry", false)),
                    ("離開遊戲", () => {
                        UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug).ContinueWith(p => {
                            p.Initial("QUIT GAME?", "HINT", Application.Quit);
                        }).Forget();
                    } )
            );

            panel.AddDebugGroup(
                "AUDIO",
                    ("PLAY SFX", () => AudioMgr.Instance.Play("common_button_click")),
                    ("PLAY BGM", () => AudioMgr.Instance.PlayBGM("BGM_01")),
                    ("SetSfxMute", () => AudioMgr.Instance.SetSfxMute(!AudioMgr.Instance.IsSfxMuted)),
                    ("SetBgmMute", () => AudioMgr.Instance.SetBgmMute(!AudioMgr.Instance.IsBgmMuted)),
                    ("SfxVolume 1", () => AudioMgr.Instance.SetSfxVolume(1f)),
                    ("SfxVolume 0.5", () => AudioMgr.Instance.SetSfxVolume(0.5f)),
                    ("BgmVolume 1", () => AudioMgr.Instance.SetBgmVolume(1f)),
                    ("BgmVolume 0.5", () => AudioMgr.Instance.SetBgmVolume(0.5f))
            );
            void setLang(SystemLanguage language)
            {
                SaveMgr.Instance.SetCore(LocalMgr.SC_LANGUAGE, $"{(int)language}");
                SaveMgr.Instance.SaveCurrentSlot();
                LocalMgr.Instance.Set(language);
            }
        }
    }
}