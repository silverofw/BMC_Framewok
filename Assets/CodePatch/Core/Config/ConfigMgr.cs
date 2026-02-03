using Luban;
using BMC.Core;
using UnityEngine;

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
