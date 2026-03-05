using BMC.Core;
using BMC.UI;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using TMPro;
using UnityEngine;
public static class UIExtensions
{
    public static void Local(this UIText uIText, string key)
    {
        uIText.Set(LocalMgr.Instance.Local(key));
    }

    public static void Local(this UIText uIText, string key, object arg0)
    {
        uIText.Set(LocalMgr.Instance.LocalFormat(key, arg0));
    }

    /// <summary>
    /// 道具多語言名稱
    /// </summary>
    /// <param name="uIText"></param>
    /// <param name="key"></param>
    public static void LocalItem(this UIText uIText, int itemId)
    {
        uIText.Set(LocalMgr.Instance.Local($"Item_{itemId}"));
    }


    /// <summary>
    /// FROM DG.Tweening.DOTweenModuleUI
    /// </summary>
    /// <param name="target"></param>
    /// <param name="endValue"></param>
    /// <param name="duration"></param>
    /// <param name="richTextEnabled"></param>
    /// <param name="scrambleMode"></param>
    /// <param name="scrambleChars"></param>
    /// <returns></returns>
    public static TweenerCore<string, string, StringOptions> DOText(this TMP_Text target, string endValue, float duration, bool richTextEnabled = true, ScrambleMode scrambleMode = ScrambleMode.None, string scrambleChars = null)
    {
        if (endValue == null)
        {
            if (Debugger.logPriority > 0) Debugger.LogWarning("You can't pass a NULL string to DOText: an empty string will be used instead to avoid errors");
            endValue = "";
        }
        TweenerCore<string, string, StringOptions> t = DOTween.To(() => target.text, x => target.text = x, endValue, duration);
        t.SetOptions(richTextEnabled, scrambleMode, scrambleChars)
            .SetTarget(target);
        return t;
    }
}
