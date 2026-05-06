using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.U2D;
using YooAsset;
namespace BMC.Core
{
    public class ResMgr : Singleton<ResMgr>
    {
        private string DefaultPackage = "DefaultPackage";
        private string RawPackage = "RawPackage";

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

            SpriteAtlasManager.atlasRequested += OnAtlasRequested;
        }

        void Clear()
        {
            SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
        }

        public async UniTask LoadSceneAsync(string path)
        {
            await YooAssets.LoadSceneAsync(path);
        }

        public async UniTask<Sprite[]> LoadSprite(string location, CancellationToken cts = default)
        {
            // 同步預先檢查
            if (!Check(location))
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
            if (!Check(location))
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

        public bool Check(string location)
        {
            foreach (var item in dic)
            {
                if (Check(item.Value, location))
                    return true;
            }
            return false;
        }
        public bool Check(ResourcePackage package, string location)
        {
            return package.CheckLocationValid(location);
        }

        public TObject LoadAsset<TObject>(string location) where TObject : Object
        {
            if (!Check(location))
            {
                Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
                return null;
            }
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
            if (!Check(package, location))
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

        /// <summary>
        /// yooasset 圖集加載回調，當 UI 顯示時發現缺少圖集，會觸發此方法
        /// </summary>
        /// <param name="atlasName"></param>
        /// <param name="callback"></param>
        private void OnAtlasRequested(string atlasName, System.Action<SpriteAtlas> callback)
        {
            // 當 UI 顯示時發現缺少圖集，會觸發此方法
            // atlasName 會是 "AltasLobby" (您的圖集名稱)

            // 使用 YooAsset 同步或非同步加載該圖集
            // 這裡以同步加載為例（實際專案中若圖集較大，建議先預加載或使用非同步）
            AssetHandle handle = YooAssets.LoadAssetSync<SpriteAtlas>(atlasName);
            SpriteAtlas atlas = handle.AssetObject as SpriteAtlas;

            if (atlas != null)
            {
                // 將加載到的圖集透過 callback 交還給 Unity 系統
                callback(atlas);
            }
            else
            {
                Debug.LogError($"無法從 YooAsset 加載圖集: {atlasName}");
            }
        }
    }
}
