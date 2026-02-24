using Luban;
using BMC.Core;
using UnityEngine;
using BMC.Patch.Core;

public class ConfigMgr : Singleton<ConfigMgr>
{
    public bmc.Tables Tables { get; private set; } = null;

    protected override void Init()
    {
        base.Init();
        Tables = new bmc.Tables(LoadByteBuf);
    }

#if USE_BYTES_CONFIG
    private static ByteBuf LoadByteBuf(string file)
    {
        var asset = ResMgr.Instance.LoadAsset<TextAsset>(file);
        return new ByteBuf(asset.bytes);
    }
#endif

#if USE_JSON_CONFIG
    private static JSONNode LoadByteBuf(string file)
    {
        return JSON.Parse(File.ReadAllText($"{Application.dataPath}/Res/Configs/json/{file}.json", System.Text.Encoding.UTF8));
    }
#endif
}


public class ConfigLang : LangData
{
    public override string Local(string key)
    {
        var c = ConfigMgr.Instance.Tables.Tblocalization.GetOrDefault(key);
        if(c == null)
            return key;
        switch (LocalMgr.Instance.CrtLang)
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