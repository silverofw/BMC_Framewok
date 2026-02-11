using UnityEngine;
namespace BMC.Core
{
    public class LocalMgr : Singleton<LocalMgr>
    {
        public LangData Data;
        public SystemLanguage CrtLang = SystemLanguage.English;
        private string SC_LANGUAGE = "SC_LANGUAGE";

        /// <summary>
        /// 讀取記錄檔案的語言資料，遊戲流程需要添加
        /// </summary>
        /// <returns></returns>
        public SystemLanguage Load()
        {
            var index = SaveMgr.Instance.GetCoreInt(SC_LANGUAGE, (int)SystemLanguage.English);
            CrtLang = (SystemLanguage)index;
            return CrtLang;
        }
        public void Set(SystemLanguage language)
        {
            SaveMgr.Instance.SetCore(SC_LANGUAGE, $"{(int)language}");
            SaveMgr.Instance.SaveCurrentSlot();
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
    }

    /// <summary>
    /// 客製化讀取多語言資料來源
    /// </summary>
    public abstract class LangData
    {
        public abstract string Local(string key);
    }
}
