using BMC.Core;
using BMC.UI;
using Cysharp.Threading.Tasks;
using HybridCLR;
using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

public class Patch : MonoBehaviour
{
    [SerializeField] private EPlayMode playMode = EPlayMode.EditorSimulateMode;
    /// <summary>
    /// 编辑器下，默认使用 playMode，构建后默认使用 buildPlayMode
    /// </summary>
    [SerializeField] private EPlayMode buildPlayMode = EPlayMode.HostPlayMode;
    [SerializeField] private PatchWindow patchWindow;
    [SerializeField] private string cdnUrl = "https://cdn.pages.dev/";
    [SerializeField] private string[] packages = new string[] { "DefaultPackage", "RawPackage" };
    /// <summary>
    /// 順序不可對調，因為有些DLL可能依賴其他DLL，必須先加載被依賴的DLL。
    /// 這裡要跟HybridCLR的設定一樣
    /// </summary>
    [SerializeField] private string[] patchDlls = new string[] { "BMC.Patch.Core.dll", "CodePatch.dll" };

    private CancellationTokenSource cts = new CancellationTokenSource();
    void Start()
    {
        PatchWindow.cdnUrl = cdnUrl;
        Init().Forget();
    }

    async UniTask Init()
    {
#if !UNITY_EDITOR
        playMode = buildPlayMode;
#endif
        if (!await checkAppVersion())
            return;
        var ops = new List<(string, GameAsyncOperation)>();
        foreach (var p in packages)
        {
            ops.Add((p, new PatchOperation(p, playMode)));
        }

        await ResMgr.Instance.InitAssets(playMode, ops.ToArray());
        patchWindow.UpdatePatchInfo();
        await LoadMetadataForAOTAssemblies();

        await LoadDLL(patchDlls);

        await UIMgr.Instance.LoadGlobalCanvas();
        await ResMgr.Instance.LoadSceneAsync("Entry");
    }

    /// <summary>
    /// 範本:
    /// { "appVersion": "0.1.0" }
    /// </summary>
    /// <returns></returns>
    async UniTask<bool> checkAppVersion()
    {
        if (playMode == EPlayMode.EditorSimulateMode || playMode == EPlayMode.OfflinePlayMode)
            return true;

        var serverAppVersion = "";
        var platform = Application.platform;
        if (Application.isEditor)
            platform = RuntimePlatform.Android; // for test
        var url = $"{cdnUrl}CDN/{platform}/AppVersion.json";
        Debug.Log($"[Network] 請求 URL: {url}");
        using (var webRequest = new UnityWebRequest(url))
        {
            webRequest.timeout = 15;
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("charset", "utf-8");

            try
            {
                await webRequest.SendWebRequest().ToUniTask(cancellationToken: cts.Token);

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    if (webRequest.error == "Request timed out")
                        Debug.LogError($"[Network] 請求超時");
                    else
                        Debug.LogError($"[Network] 請求失敗: {webRequest.error}");
                }
                else
                {
                    string jsonResponse = webRequest.downloadHandler.text;
                    Debug.Log($"[Network] 收到 JSON 回應：{jsonResponse}");
                    serverAppVersion = JObject.Parse(jsonResponse)["appVersion"].ToString();
                }
            }
            catch (OperationCanceledException)
            {
                // 當 CancellationToken 被觸發時，會進入這裡
                Debug.Log("[Network] 請求已被使用者取消。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Network] 錯誤或發生例外: {ex.Message}");
            }
        }


        var appVersion = Application.version;
        if (appVersion != serverAppVersion)
        {
            // 版本不匹配，跳轉商店更新
            Debug.LogWarning($"[版本檢查] 版本不匹配，請更新應用程式。當前版本: {appVersion}, 伺服器版本: {serverAppVersion}");
            patchWindow.ShowMessageBox("PLEASE_UPDATE_APPLICATION", () =>
            {
                //Application.OpenURL(ServerMgr.GetStoreUrl());
            });
            return false;
        }
        Debug.Log($"[版本檢查] 版本匹配，繼續遊戲。當前版本: {appVersion}");
        return true;
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
