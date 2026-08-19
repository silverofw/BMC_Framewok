using System;
using BMC.Core;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 讓 VisualElement 自動跟隨語言切換。
    ///
    /// UI Toolkit 的元件不是 MonoBehaviour，沒有 OnEnable／OnDisable 可用，
    /// 改以 AttachToPanel／DetachFromPanel 作為訂閱與退訂的時機——
    /// 元件進入畫面時套用一次並開始監聽，離開畫面時自動解除，不會殘留訂閱。
    /// </summary>
    public static class LocalizationBinding
    {
        /// <summary>
        /// 綁定語言更新。
        /// </summary>
        /// <param name="element">要綁定的元件</param>
        /// <param name="apply">套用譯文的動作，會在進入畫面時與每次語言變更時被呼叫</param>
        public static void BindLocalization(this VisualElement element, Action apply)
        {
            if (element == null || apply == null)
                return;

            // 區域函式轉成的委派雖然每次都是新物件，但 -= 是以「方法 + 目標」比對，
            // 兩個 lambda 共用同一個閉包實例，因此退訂能正確配對。
            void Handler(UnityEngine.SystemLanguage _) => apply();

            element.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                apply();
                LocalMgr.Instance.OnLanguageChanged += Handler;
            });

            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                LocalMgr.Instance.OnLanguageChanged -= Handler;
            });
        }
    }
}
