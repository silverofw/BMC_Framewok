using BMC.Core;
using Cysharp.Threading.Tasks;
using HybridCLR;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using YooAsset;
public class ResMgr : Singleton<ResMgr>
{
    public const string DefaultPackage = "DefaultPackage";
    public const string RawPackage = "RawPackage";

    private Dictionary<string, ResourcePackage> dic = new();

    public async UniTask InitAssets(EPlayMode playMode, (string, GameAsyncOperation)[] ops)
    {
        // 初始化资源系统
        YooAssets.Initialize();
        foreach (var p in ops)
        {
            // 开始补丁更新流程
            YooAssets.StartOperation(p.Item2);
            await p.Item2.ToUniTask();

            dic.Add(p.Item1, YooAssets.GetPackage(p.Item1));
        }

        // 设置默认的资源包
        var gamePackage = dic[DefaultPackage];
        YooAssets.SetDefaultPackage(gamePackage);
    }

    public async UniTask LoadSceneAsync(string path)
    {
        await YooAssets.LoadSceneAsync(path);
    }

    public async UniTask<Sprite[]> LoadSprite(string location, CancellationToken cts = default)
    {
        // 同步預先檢查
        if (!YooAssets.CheckLocationValid(location))
        {
            Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
            return null;
        }
        SubAssetsHandle h = YooAssets.LoadSubAssetsAsync<Sprite>(location);
        await h.ToUniTask(cancellationToken: cts);
        return h.GetSubAssetObjects<Sprite>();
    }

    public async UniTask<TObject> LoadUIAssetAsync<TObject>(string location, bool instantiate = false, Transform parent = null,
        bool worldPositionStays = false, CancellationToken cts = default) where TObject : Object
    {
        var c = YooAssets.CheckLocationValid(location);
        if (!c)
        {
            Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
            return null;
        }
        var h = YooAssets.LoadAssetAsync<TObject>(location);
        await h.ToUniTask(cancellationToken: cts);
        if (instantiate)
        {
            var go = GameObject.Instantiate(h.AssetObject, parent, worldPositionStays) as TObject;
            h.Release();
            return go;
        }
        else
        {
            return h.AssetObject as TObject;
        }
    }

    public TObject LoadAsset<TObject>(string location) where TObject : Object
    {
        //Log.Info(path);
        return YooAssets.LoadAssetSync<TObject>(location).AssetObject as TObject;
    }

    /// <summary>
    /// 直接Instantiate + 釋放Handle
    /// </summary>
    /// <typeparam name="TObject"></typeparam>
    /// <param name="location"></param>
    /// <returns></returns>
    public async UniTask<TObject> LoadAssetAsync<TObject>(string location, bool instantiate = true, Transform parent = null,
        CancellationTokenSource cts = default) where TObject : Object
    {
        var h = YooAssets.LoadAssetAsync<TObject>(location);
        await h.ToUniTask(cancellationToken: cts?.Token ?? CancellationToken.None);
        if (instantiate)
        {
            var go = GameObject.Instantiate(h.AssetObject, parent) as TObject;
            h.Release();
            return go;
        }
        else
        {
            return h.AssetObject as TObject;
        }
    }

    public SubAssetsHandle LoadSubAssets<TObject>(string location) where TObject : Object
    {
        return YooAssets.LoadSubAssetsAsync<TObject>(location);
    }

    public async UniTask<string> LoadRawFilePathAsync(string location, CancellationToken cts = default)
    {
        var package = dic[RawPackage];
        if (!package.CheckLocationValid(location))
        {
            Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
            return "";
        }
        string localVideoPath = "";

        var handle = package.LoadRawFileAsync(location);
        await handle.ToUniTask(cancellationToken: cts);

        if (handle.Status == EOperationStatus.Succeed)
        {
            Debug.Log($"[ResMgr] 絕對路徑: {localVideoPath}");
            return handle.GetRawFilePath();
        }
        else
        {
            Debug.LogError($"[ResMgr] 資源加載失敗: {location} \nError: {handle.LastError}");
            return "";
        }
    }
}
