using BMC.Core;
using UnityEngine;
namespace BMC.Patch.Core
{
    public class LocalMgr : Singleton<LocalMgr>
    {
        private string SC_LANGUAGE = "SC_LANGUAGE";

        public SystemLanguage Get()
        {
            var index = SaveMgr.Instance.GetCoreInt(SC_LANGUAGE, (int)SystemLanguage.English);
            return (SystemLanguage)index;
        }
        public void Set(SystemLanguage language)
        {
            SaveMgr.Instance.SetCore(SC_LANGUAGE, $"{(int)language}");
            SaveMgr.Instance.SaveCurrentSlot();
        }

        public string Local(string key)
        {
            var c = ConfigMgr.Instance.Tables.Tblocalization.GetOrDefault(key);
            switch (Get())
            {
                case SystemLanguage.ChineseTraditional:
                    return c.Tc;
                case SystemLanguage.ChineseSimplified:
                    return c.Sc;
                case SystemLanguage.Japanese:
                    return c.Jp;
                default:
                    return c.En;
            }
        }
    }
}
