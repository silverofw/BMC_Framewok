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
            UIMgr.Instance.ShowPanel<Toast>(UICanvasType.UI_2, false).ContinueWith((p) => {
                p.Init(info, delayFrame);
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
