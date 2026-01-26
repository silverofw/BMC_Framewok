using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
namespace BMC.UI
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
    public class UIPanelAnimaTool : MonoBehaviour
    {
        DOTweenAnimation[] list = new DOTweenAnimation[0];
        // Start is called before the first frame update
        void Start()
        {
            list = GetComponents<DOTweenAnimation>();
        }

        public void DOPlay()
        {
            foreach (var anim in list)
            {
                anim.DORestart();
                //anim.DOPlay();
            }
        }

        public void DOPlayBackwards(Action callback = null)
        {
            float maxDuration = 0f;

            // 1. 找出所有動畫中時間最長的 (包含延遲)
            foreach (var anim in list)
            {
                float thisDuration = anim.duration + anim.delay;
                if (thisDuration > maxDuration) maxDuration = thisDuration;

                // 執行倒放
                anim.DOPlayBackwards();
            }

            // 2. 如果有 callback，使用延遲呼叫來觸發
            // 這樣無論動畫是否有實際移動，時間到了都會準時呼叫
            if (callback != null)
            {
                // 注意：如果這個 GameObject 在倒放過程中被 Destroy，這個 Timer 還是會執行
                // 如果需要跟隨 GameObject 生命週期，請在 DelayedCall 後面加上 .SetTarget(this)
                DOVirtual.DelayedCall(maxDuration, () =>
                {
                    callback.Invoke();
                }).SetId("CheckCallback"); // SetId 可選，方便 Debug
            }
        }
    }
}
