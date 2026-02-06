using BMC.UI;
using Cysharp.Threading.Tasks;
using HybridCLR;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

public class Patch : MonoBehaviour
{
    [SerializeField] private EPlayMode playMode = EPlayMode.EditorSimulateMode;
    [SerializeField] private PatchWindow patchWindow;
    [SerializeField] private string cdnUrl = "https://cdn.pages.dev/";

    void Start()
    {
        PatchWindow.cdnUrl = cdnUrl;
        Init().Forget();
    }

    async UniTask Init()
    {
#if !UNITY_EDITOR
        playMode = EPlayMode.OfflinePlayMode;
#endif
        //var list = new string[] { ResMgr.DefaultPackage, ResMgr.RawPackage };
        var list = new string[] { ResMgr.DefaultPackage };
        var ops = new List<(string, GameAsyncOperation)>();
        foreach (var p in list)
        {
            ops.Add((p, new PatchOperation(p, playMode)));
        }

        await ResMgr.Instance.InitAssets(playMode, ops.ToArray());
        patchWindow.UpdatePatchInfo();
        await LoadMetadataForAOTAssemblies();
        await LoadDLL(new string[] { "CodePatch.dll" });

        await UIMgr.Instance.LoadGlobalCanvas();
        await ResMgr.Instance.LoadSceneAsync("Entry");
    }

    public async UniTask LoadDLL(string[] patchDlls)
    {
        foreach (var dll in patchDlls)
        {
#if !UNITY_EDITOR
            // Editor环境下，xxx.dll.bytes已经被自动加载，不需要加载，重复加载反而会出问题。
            var asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(dll);
            System.Reflection.Assembly.Load(asset.bytes);
#endif
            Debug.Log($"Load DLL: {dll}");
        }
    }

    async UniTask LoadMetadataForAOTAssemblies()
    {
        HomologousImageMode mode = HomologousImageMode.SuperSet;

        // Directly use the auto-generated list from HybridCLR
        // This ensures your code is always in sync with your settings.
        foreach (var dll in AOTGenericReferences.PatchedAOTAssemblyList)
        {
            // Your existing loading logic is perfect
            var AOTMetadataDll = await ResMgr.Instance.LoadAssetAsync<TextAsset>(dll);
            LoadImageErrorCode err = LoadImageErrorCode.OK;
#if !UNITY_EDITOR
            err = RuntimeApi.LoadMetadataForAOTAssembly(AOTMetadataDll.bytes, mode);
#endif
            Debug.Log($"LoadMetadataForAOTAssembly:{AOTMetadataDll.name}. mode:{mode} ret:{err}");
        }
    }
}
