using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// 預設資源包。YooAsset v3 移除了 YooAssets 的預設包靜態介面，改由這裡自行持有。
        /// </summary>
        private ResourcePackage defaultPackage;

        /// <summary>
        /// 已回報過「不在資源包中」的圖集名稱，避免 Unity 重複請求時洗版
        /// </summary>
        private readonly HashSet<string> _missingAtlasNames = new();

        /// <summary>
        /// 初始化資源系統，並依序跑完每個資源包的補丁流程。
        /// </summary>
        /// <param name="ops">(資源包名稱, 該資源包的補丁流程)。YooAsset v3 沒有公開 API 可以啟動自訂 operation，
        /// 因此改用委派由呼叫端提供流程實作，這裡只負責依序等待。</param>
        public async UniTask InitAssets(EPlayMode playMode, (string, System.Func<UniTask>)[] ops)
        {
            // 初始化资源系统
            YooAssets.Initialize();
            foreach (var (packageName, patchTask) in ops)
            {
                // 开始补丁更新流程
                await patchTask();

                dic.Add(packageName, YooAssets.GetPackage(packageName));
            }

            // 设置默认的资源包
            defaultPackage = dic[DefaultPackage];

            SpriteAtlasManager.atlasRequested -= OnAtlasRequested; // 預防重複註冊
            SpriteAtlasManager.atlasRequested += OnAtlasRequested;
        }

        void Clear()
        {
            SpriteAtlasManager.atlasRequested -= OnAtlasRequested;
        }

        public async UniTask LoadSceneAsync(string path)
        {
            await defaultPackage.LoadSceneAsync(path);
        }

        public async UniTask<Sprite[]> LoadSprite(string location, CancellationToken cts = default)
        {
            // 同步預先檢查
            if (!Check(location))
            {
                Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
                return null;
            }
            SubAssetsHandle h = defaultPackage.LoadSubAssetsAsync<Sprite>(location);
            await h.ToUniTask(cancellationToken: cts);
            return h.GetSubAssetObjects<Sprite>().ToArray();
        }

        public async UniTask<TObject> LoadUIAssetAsync<TObject>(string location, bool instantiate = false, Transform parent = null,
            bool worldPositionStays = false, CancellationToken cts = default) where TObject : Object
        {
            if (!Check(location))
            {
                Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
                return null;
            }
            var h = defaultPackage.LoadAssetAsync<TObject>(location);
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
            return package.IsLocationValid(location);
        }

        public TObject LoadAsset<TObject>(string location) where TObject : Object
        {
            if (!Check(location))
            {
                Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
                return null;
            }
            //Log.Info(path);
            return defaultPackage.LoadAssetSync<TObject>(location).AssetObject as TObject;
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
            var h = defaultPackage.LoadAssetAsync<TObject>(location);
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
            return defaultPackage.LoadSubAssetsAsync<TObject>(location);
        }

        public async UniTask<string> LoadRawFilePathAsync(string location, CancellationToken cts = default)
        {
            var package = dic[RawPackage];
            if (!Check(package, location))
            {
                Log.Warning($"[YooAsset] 資源路徑不存在 (Location is invalid): {location}");
                return "";
            }

            // v3 取代 LoadRawFileAsync().GetRawFilePath()：先確保檔案就緒，再取得本機絕對路徑
            var operation = package.EnsureBundleFileAsync(new EnsureBundleFileOptions(location));
            await operation.ToUniTask(cancellationToken: cts);

            if (operation.Status == EOperationStatus.Succeeded)
            {
                string localFilePath = operation.Detail.BundleFilePath;
                Debug.Log($"[ResMgr] 絕對路徑: {localFilePath}");
                return localFilePath;
            }
            else
            {
                Debug.LogError($"[ResMgr] 資源加載失敗: {location} \nError: {operation.Error}");
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

            // SpriteAtlasManager.atlasRequested 是 Unity 的全域回調：
            // 專案中任何「Include in Build」未勾選的圖集都會進來，包含第三方套件的 demo 資源。
            // 不在資源包裡的圖集直接略過，否則 YooAsset 會噴 "Location is invalid" 錯誤。
            if (defaultPackage == null || !Check(defaultPackage, atlasName))
            {
                // Unity 可能重複請求同一張圖集，只警告一次避免洗版
                if (_missingAtlasNames.Add(atlasName))
                    Log.Warning($"[YooAsset] 圖集不在資源包中，已略過: {atlasName}");
                return;
            }

            // 使用 YooAsset 同步或非同步加載該圖集
            // 這裡以同步加載為例（實際專案中若圖集較大，建議先預加載或使用非同步）
            AssetHandle handle = defaultPackage.LoadAssetSync<SpriteAtlas>(atlasName);
            SpriteAtlas atlas = handle.AssetObject as SpriteAtlas;

            if (atlas != null)
            {
                // 將加載到的圖集透過 callback 交還給 Unity 系統
                // 注意：不可 Release，圖集必須在 Unity 使用期間保持存活
                callback(atlas);
            }
            else
            {
                Debug.LogError($"無法從 YooAsset 加載圖集: {atlasName}");
                handle.Release();
            }
        }
    }
}
