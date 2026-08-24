using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;

namespace BMC.UI
{
    /// <summary>
    /// RectTransform / CanvasGroup 的 DOTween 捷徑，用核心 API 重寫。
    ///
    /// 【為什麼不直接用 DOTween.Modules 的版本】那些擴充方法定義在
    /// Assets/Plugins/DOTween/Modules 的 DOTween.Modules 組件裡，而 Packages/ 底下的組件
    /// 引用不到 Assets/ 底下的組件 —— 而且 Unity 是「靜默丟掉」那個引用，不會報錯。
    ///
    /// 這不只影響本專案要把 BMC.UI 搬進 Packages/。BMC.UI 是要給別的專案引用的套件，
    /// 消費端從 Asset Store 把 DOTween 裝進他們自己的 Assets/Plugins 之後，套件形式的
    /// BMC.UI 一樣引用不到 DOTween.Modules，同一個坑會在他們那邊重演。
    /// 相依 DOTween.Modules 等於 BMC.UI 根本不能以套件散佈。
    ///
    /// 核心的 DOTween.dll 是預編譯外掛，對套件組件一律可見，所以只用 DOTween.To 就沒這個問題。
    /// 實作與 DOTweenModuleUI 的對應方法逐行等價（含 SetOptions / SetTarget），
    /// 換掉之後行為不變。
    ///
    /// 刻意宣告成 internal：如果公開出去，消費端同時 using BMC.UI 與 DG.Tweening 時，
    /// 兩邊的同名擴充方法會變成模稜兩可的呼叫而編譯失敗。
    /// </summary>
    internal static class TweenShortcuts
    {
        /// <summary>對應 DOTweenModuleUI.DOAnchorPosY。</summary>
        public static TweenerCore<Vector2, Vector2, VectorOptions> DOAnchorPosY(
            this RectTransform target, float endValue, float duration, bool snapping = false)
        {
            TweenerCore<Vector2, Vector2, VectorOptions> t = DOTween.To(
                () => target.anchoredPosition,
                x => target.anchoredPosition = x,
                new Vector2(0, endValue),
                duration);
            t.SetOptions(AxisConstraint.Y, snapping).SetTarget(target);
            return t;
        }

        /// <summary>對應 DOTweenModuleUI.DOFade(CanvasGroup)。</summary>
        public static TweenerCore<float, float, FloatOptions> DOFade(
            this CanvasGroup target, float endValue, float duration)
        {
            TweenerCore<float, float, FloatOptions> t = DOTween.To(
                () => target.alpha,
                x => target.alpha = x,
                endValue,
                duration);
            t.SetTarget(target);
            return t;
        }
    }
}
