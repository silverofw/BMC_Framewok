using UnityEngine;
namespace BMC.Core
{
    public class LocalMgr : Singleton<LocalMgr>
    {
        public SystemLanguage CrtLang { get; private set; } = SystemLanguage.English;
        public const string SC_LANGUAGE = "SC_LANGUAGE";

        private LangData Data;

        public SystemLanguage Load(LangData data, SystemLanguage language)
        {
            Data = data;
            CrtLang = language;
            return CrtLang;
        }

        public void Set(SystemLanguage language)
        {
            CrtLang = language;
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
