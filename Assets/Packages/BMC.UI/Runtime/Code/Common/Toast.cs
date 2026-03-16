using BMC.Core;
using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BMC.UI
{
    public class Toast : UIPanel
    {
        public static void Show(string info, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {info}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(info, delayFrame);
            }).Forget();
        }
        public static void Local(string key, float delayFrame = 3f)
        {
            Log.Info($"[Toast] {key}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(LocalMgr.Instance.Local(key), delayFrame);
            }).Forget();
        }
        public static void LocalFormat(string key, params object[] args)
        {
            Log.Info($"[Toast] {key}");
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_Debug, false).ContinueWith((p) => {
                p.Init(LocalMgr.Instance.LocalFormat(key, args), 3f);
            }).Forget();
        }

        [SerializeField] private UIText text;

        public void Init(string info, float delay = 3f)
        {
            text.Set(info);
            wait(delay).Forget();
        }

        async UniTask wait(float delay)
        {
            await UniTask.WaitForSeconds(delay);
            ClosePanel();
        }
    }
}
