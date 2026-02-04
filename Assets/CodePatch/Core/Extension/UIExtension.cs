using UnityEngine;
using BMC.UI;

public static class UIExtensions
{
    /// <summary>
    /// 多語言
    /// </summary>
    /// <param name="uIText"></param>
    /// <param name="key"></param>
    public static void Local(this UIText uIText, string key)
    {
        uIText.Set(LocalMgr.Instance.Local(key));
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
}
