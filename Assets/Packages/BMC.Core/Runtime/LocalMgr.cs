using System;
using UnityEngine;
namespace BMC.Core
{
    public class LocalMgr : Singleton<LocalMgr>
    {
        public SystemLanguage CrtLang { get; private set; } = SystemLanguage.English;
        public const string SC_LANGUAGE = "SC_LANGUAGE";

        private LangData Data;

        /// <summary>
        /// 語言或語系資料變更時發出。
        /// UI 元件訂閱後即可即時換字，不必關閉重開介面。
        /// </summary>
        public event Action<SystemLanguage> OnLanguageChanged;

        /// <summary>
        /// 語系資料是否已載入。
        /// 尚未載入時 Local() 只會原樣回傳 key，UI 元件可據此決定
        /// 保留原本編排好的預設文字，而不是把 key 直接顯示給玩家。
        /// </summary>
        public bool IsReady => Data != null;

        public SystemLanguage Load(LangData data, SystemLanguage language)
        {
            Data = data;
            CrtLang = language;

            // 資料來源換掉等同全部譯文都變了，一律通知
            RaiseLanguageChanged();
            return CrtLang;
        }

        public void Set(SystemLanguage language)
        {
            if (CrtLang == language)
                return;

            CrtLang = language;
            RaiseLanguageChanged();
        }

        /// <summary>
        /// 逐一呼叫訂閱者並隔離例外：
        /// 單一介面元件出錯不該讓其他元件跟著漏掉這次語言更新。
        /// </summary>
        private void RaiseLanguageChanged()
        {
            var handlers = OnLanguageChanged;
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<SystemLanguage>)handler).Invoke(CrtLang);
                }
                catch (Exception e)
                {
                    Log.Error($"[LocalMgr] 語言變更回呼發生例外: {e}");
                }
            }
        }

        public string Local(string key)
        {
            if (Data == null)
            {
                Log.Warning("Init Data first");
                return key;
            }
            return Data.Local(key);
        }

        public void Local(string key, System.Action<string> action)
        {
            action?.Invoke(Local(key));
        }
        public string LocalFormat(string key, params object[] args)
        {
            return string.Format(Local(key), args);
        }
    }

    /// <summary>
    /// 客製化讀取多語言資料來源
    /// </summary>
    public abstract class LangData
    {
        public abstract string Local(string key);
    }
}
