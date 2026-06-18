using BMC.Core;
using Cysharp.Threading.Tasks;
using Google.Protobuf;
using InfiniteMap.Proto;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace InfiniteMap.Unity
{
    // 定義向外部索取資料的委派
    public delegate EntityProto FetchEntityDataDelegate(long guid);

    /// <summary>
    /// 純 C# 的世界控制器 (不再繼承 MonoBehaviour)
    /// 負責與 World 框架溝通，並處理 Unity 端的 IO 序列化與實體管理
    /// </summary>
    public class InfiniteWorldController
    {
        // =========================================================
        // 核心設定屬性
        // =========================================================
        public int WorldId { get; private set; }
        public int ChunkSize { get; private set; }
        public int LoadRadius { get; private set; }
        public float TileSize { get; private set; }

        // =========================================================
        // 檔案並發鎖管理 (解決 IOException: Sharing violation)
        // =========================================================
        private static readonly Dictionary<string, SemaphoreSlim> _fileLocks = new Dictionary<string, SemaphoreSlim>();
        private static readonly object _fileLocksLock = new object();

        private static SemaphoreSlim GetFileLock(string filePath)
        {
            lock (_fileLocksLock)
            {
                if (!_fileLocks.TryGetValue(filePath, out var semaphore))
                {
                    semaphore = new SemaphoreSlim(1, 1);
                    _fileLocks[filePath] = semaphore;
                }
                return semaphore;
            }
        }

        // =========================================================
        // 框架事件 (IoC 反轉控制) - 供 AtomECSMgr 綁定
        // =========================================================

        /// <summary> 當區塊載入，需要生成實體時觸發 (傳出 Proto 以及該區塊的最後存檔時間) </summary>
        public event System.Action<EntityProto, long> OnEntitySpawn;

        /// <summary> 
        /// 當區塊準備存檔時觸發 (傳入 GUID，請外部回傳最新的 Proto 屬性狀態) 
        /// 註：不使用 event 關鍵字，以便外部編輯器系統可以直接 Invoke 索取資料
        /// </summary>
        public FetchEntityDataDelegate OnEntitySerialize;

        /// <summary> 當區塊卸載完成時觸發 (傳出 GUID，請外部銷毀對應的 ECS/GameObject) </summary>
        public event System.Action<long> OnEntityDestroy;

        // 純 C# 的世界核心邏輯
        private World _world;

        // 存檔路徑管理
        private string _saveDirectory;
        private string _saveBasePath;

        // 紀錄玩家上一次的位置，避免每幀無意義的計算
        private Vector3 _lastPlayerPos;
        private bool _isFirstUpdate = true;

        /// <summary>
        /// 系統初始化
        /// </summary>
        public void Init(int worldId, int chunkSize, int loadRadius, float tileSize, string saveBasePath)
        {
            WorldId = worldId;
            ChunkSize = chunkSize;
            LoadRadius = loadRadius;
            TileSize = tileSize;
            _saveBasePath = saveBasePath;

            // 初始化存檔資料夾路徑，使用 saveBasePath/Zone_{WorldId} 的資料夾結構
            _saveDirectory = Path.Combine(saveBasePath, $"Zone_{WorldId}");
            if (!Directory.Exists(_saveDirectory))
            {
                Directory.CreateDirectory(_saveDirectory);
            }

            // 1. 初始化純 C# 地圖框架
            _world = new World(ChunkSize, LoadRadius);

            // 2. 綁定資料的載入與儲存委派 (橋接 Unity I/O 與 底層邏輯)
            _world.OnLoadChunkAsync = LoadChunkFromDiskAsync;
            _world.OnSaveChunkAsync = SaveAndUnloadChunkAsync;
        }

        /// <summary>
        /// 外部驅動的 Tick 邏輯 (放在 Update 中呼叫)
        /// </summary>
        public void Tick(Vector3 playerPosition)
        {
            if (_world == null) return;

            // 效能優化：首次執行 or 玩家移動超過 half tile 才更新區塊
            if (_isFirstUpdate || Vector3.Distance(playerPosition, _lastPlayerPos) > TileSize * 0.5f)
            {
                _isFirstUpdate = false;
                _lastPlayerPos = playerPosition;

                // 改為 XY 平面映射：X->x, Y->y, Z->h (作為高度)
                // 將 Unity Vector3 轉換為底層框架的 Pos3
                Pos3 playerPos = new Pos3(
                    Mathf.FloorToInt(playerPosition.x / TileSize),
                    Mathf.FloorToInt(playerPosition.y / TileSize),
                    Mathf.FloorToInt(playerPosition.z / TileSize) // z軸做為 H 傳入 Pos3
                );

                // 觸發區塊更新 (Fire and Forget 背景執行)
                _world.UpdateFocusAsync(playerPos).Forget();
            }
        }

        // =========================================================
        // 運行時動態操作介面 (Runtime API：建立、刪除、移動實體)
        // =========================================================

        /// <summary>
        /// 註冊新建立的實體到當前區塊中 (如玩家建造的牆壁、剛招募的貓咪)
        /// </summary>
        public void AddRuntimeEntity(long guid, Pos3 pos)
        {
            if (guid == 0)
            {
                Debug.LogWarning($"[InfiniteWorldController] 拒絕加入：嘗試將 GUID 為 0 的實體加入 Chunk ({pos.x}, {pos.y}, {pos.h})。這通常是因為實體未被正確配發 ID。");
                return;
            }

            if (_world == null) return;
            CPos cPos = pos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.AddEntity(guid, pos);
            }
        }

        /// <summary>
        /// 將實體從區塊中徹底移除 (如建築被破壞、貓咪戰死)
        /// </summary>
        public void RemoveRuntimeEntity(long guid, Pos3 pos)
        {
            if (guid == 0 || _world == null) return;
            CPos cPos = pos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.RemoveEntity(guid, pos);
            }
        }

        /// <summary>
        /// 更新實體在區塊中的位置 (支援跨 Chunk 移動)
        /// </summary>
        public void MoveRuntimeEntity(long guid, Pos3 oldPos, Pos3 newPos)
        {
            if (guid == 0 || _world == null || oldPos == newPos) return;

            CPos oldCPos = oldPos.ToCPos(ChunkSize);
            CPos newCPos = newPos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks == null) return;

            // 從舊區塊移除
            if (activeChunks.TryGetValue(oldCPos, out Chunk oldChunk))
            {
                oldChunk.RemoveEntity(guid, oldPos);
            }

            // 加入新區塊
            if (activeChunks.TryGetValue(newCPos, out Chunk newChunk))
            {
                newChunk.AddEntity(guid, newPos);
            }
        }

        // =========================================================
        // 系統管理 API (存檔、切換 Zone、跨 Zone 實體轉移)
        // =========================================================

        /// <summary>
        /// 將指定實體（如主角 Atom）安全導出、從目前地圖的活躍區塊中移除，並合併寫入目標 Zone 地圖的目標位置 Chunk 存檔中。
        /// </summary>
        public async UniTask<bool> TransferEntityToZoneAsync(long guid, int targetWorldId, Pos3 targetPos)
        {
            if (guid == 0) return false;

            // 1. 序列化實體當前最新的屬性狀態
            if (OnEntitySerialize == null)
            {
                Debug.LogError("[InfiniteWorldController] TransferEntityToZoneAsync 失敗：OnEntitySerialize 委派尚未綁定！");
                return false;
            }

            EntityProto entityProto = OnEntitySerialize.Invoke(guid);
            if (entityProto == null)
            {
                Debug.LogError($"[InfiniteWorldController] 序列化實體 {guid} 失敗，無法進行跨 Zone 轉移。");
                return false;
            }

            // 2. 從目前所在的活躍區塊中移除此實體（防止原場景卸載存檔時產生分身）
            Pos3 oldPos = new Pos3(entityProto.Pos.X, entityProto.Pos.Y, entityProto.Pos.H);
            RemoveRuntimeEntity(guid, oldPos);

            // 3. 更新目標位置屬性
            entityProto.Pos.X = targetPos.x;
            entityProto.Pos.Y = targetPos.y;
            entityProto.Pos.H = targetPos.h;

            // 4. 計算目標區塊座標 (cx, cy)
            int cx = Mathf.FloorToInt((float)targetPos.x / ChunkSize);
            int cy = Mathf.FloorToInt((float)targetPos.y / ChunkSize);

            // 5. 確保目標 Zone 的資料夾存在
            string targetDirectory = Path.Combine(_saveBasePath, $"Zone_{targetWorldId}");
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string fileName = $"chunk_{targetWorldId}_{cx}_{cy}.bytes";
            string filePath = Path.Combine(targetDirectory, fileName);

            ChunkProto chunkProto = null;

            // 套用路徑鎖，避免多個任務同時讀寫此檔案
            var fileLock = GetFileLock(filePath);
            await fileLock.WaitAsync();

            try
            {
                // 6. 讀取目標區塊已有的存檔數據
                string location = $"chunk_{targetWorldId}_{cx}_{cy}";

                // (1) 優先找本地玩家的存檔
                if (File.Exists(filePath))
                {
                    try
                    {
                        byte[] existingData = await File.ReadAllBytesAsync(filePath);
                        chunkProto = ChunkProto.Parser.ParseFrom(existingData);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[InfiniteWorldController] 讀取目標區塊檔案失敗: {ex.Message}");
                    }
                }
                // (2) 如果本地沒存檔，直接透過 YooAsset 抓取官方預設地圖資料
                else
                {
                    byte[] defaultData = null;
                    try
                    {
                        if (ResMgr.Instance.Check(location))
                        {
                            var asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(location);
                            if (asset != null)
                            {
                                defaultData = asset.bytes;
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[InfiniteWorldController] 目標預設區塊 YooAsset 加載失敗: {e.Message}");
                    }

                    // 如果有抓到預設地圖，以此為基礎來寫入跨區實體
                    if (defaultData != null && defaultData.Length > 0)
                    {
                        try
                        {
                            chunkProto = ChunkProto.Parser.ParseFrom(defaultData);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"[InfiniteWorldController] 預設區塊反序列化失敗: {e.Message}");
                        }
                    }
                }

                // 若目標區塊檔不存在，則建立全新 Proto 實例
                if (chunkProto == null)
                {
                    chunkProto = new ChunkProto
                    {
                        Cx = cx,
                        Cy = cy,
                        LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                }

                // 7. 防碰撞與覆蓋：移除該區塊存檔中可能已有的同名 Guid 舊資料
                for (int i = chunkProto.Entities.Count - 1; i >= 0; i--)
                {
                    if (chunkProto.Entities[i].Guid == guid)
                    {
                        chunkProto.Entities.RemoveAt(i);
                    }
                }

                // 8. 寫入最新的實體數據
                chunkProto.Entities.Add(entityProto);

                // 9. 序列化回寫至檔案
                byte[] dataToSave = chunkProto.ToByteArray();
                await File.WriteAllBytesAsync(filePath, dataToSave);
                Debug.Log($"[InfiniteWorldController] 實體 {guid} 成功轉移至地圖 Zone_{targetWorldId} 的區塊 ({cx}, {cy}) 座標 ({targetPos.x}, {targetPos.y})");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[InfiniteWorldController] 寫入目標區塊檔案失敗: {ex.Message}");
                return false;
            }
            finally
            {
                fileLock.Release();
            }
        }

        /// <summary>
        /// 單純的強制存檔 (不銷毀實體，適用於玩家主動點擊「儲存遊戲」)
        /// </summary>
        public async UniTask ForceSaveAllAsync()
        {
            if (_world == null) return;
            var activeChunks = GetActiveChunks();
            if (activeChunks == null || activeChunks.Count == 0) return;

            List<UniTask> saveTasks = new List<UniTask>();
            List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

            foreach (var cPos in keysToSave)
            {
                Chunk chunk = activeChunks[cPos];
                // 只執行純存檔邏輯，不觸發 ECS 銷毀 (卸載)
                saveTasks.Add(SaveChunkStateAsync(chunk));
            }

            await UniTask.WhenAll(saveTasks);
            Debug.Log($"[World] 已成功儲存目前進度 (共 {keysToSave.Count} 個活躍區塊)。");
        }

        /// <summary>
        /// 切換至新的地圖 Zone
        /// (強制儲存並卸載當前世界所有的活躍區塊，然後重新初始化系統)
        /// </summary>
        public async UniTask SwitchZoneAsync(int newWorldId)
        {
            if (_world != null)
            {
                var activeChunks = GetActiveChunks();
                if (activeChunks != null && activeChunks.Count > 0)
                {
                    List<UniTask> unloadTasks = new List<UniTask>();
                    List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

                    // 存檔並強制銷毀當前世界上所有的 ECS 實體
                    foreach (var cPos in keysToSave)
                    {
                        unloadTasks.Add(SaveAndUnloadChunkAsync(activeChunks[cPos]));
                    }
                    await UniTask.WhenAll(unloadTasks);
                }
            }

            // 重新初始化為新的世界編號
            Init(newWorldId, ChunkSize, LoadRadius, TileSize, _saveBasePath);
            _isFirstUpdate = true; // 重置標記，確保下一個 Tick 會立刻載入新世界的九宮格
            Debug.Log($"[World] ===== 已成功切換至 Zone_{newWorldId} =====");
        }

        // =========================================================
        // 離線區塊處理 API (編輯器/背景演算法共用)
        // =========================================================

        /// <summary>
        /// 獲取或讀取指定坐標的離線 ChunkProto，若不存在則建立新的
        /// </summary>
        public ChunkProto GetOrLoadOfflineChunk(Dictionary<CPos, ChunkProto> cache, string baseDir, int mapId, CPos cpos)
        {
            if (cache.TryGetValue(cpos, out ChunkProto proto)) return proto;

            string filePath = Path.Combine(baseDir, $"chunk_{mapId}_{cpos.x}_{cpos.y}.bytes");

            // 讀取也需要套用文件鎖，避免與正在儲存的寫入衝突
            var fileLock = GetFileLock(filePath);
            fileLock.Wait();

            try
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(filePath);
                        proto = ChunkProto.Parser.ParseFrom(data);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[InfiniteWorldController] 讀取離線區塊失敗，將建立新區塊: {e.Message}");
                    }
                }
            }
            finally
            {
                fileLock.Release();
            }

            if (proto == null)
            {
                proto = new ChunkProto
                {
                    Cx = cpos.x,
                    Cy = cpos.y,
                    LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
            }

            cache[cpos] = proto;
            return proto;
        }

        /// <summary>
        /// 批量將離線修改的 ChunkProto 寫入硬碟
        /// </summary>
        public void SaveOfflineChunks(Dictionary<CPos, ChunkProto> cache, string baseDir, int mapId)
        {
            if (cache == null || cache.Count == 0) return;
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

            foreach (var kvp in cache)
            {
                string filePath = Path.Combine(baseDir, $"chunk_{mapId}_{kvp.Key.x}_{kvp.Key.y}.bytes");

                var fileLock = GetFileLock(filePath);
                fileLock.Wait(); // 同步方法使用 Wait() 進行安全寫入鎖定

                try
                {
                    File.WriteAllBytes(filePath, kvp.Value.ToByteArray());
                }
                finally
                {
                    fileLock.Release();
                }
            }
        }

        // =========================================================
        // 內部 I/O 實作 (Protobuf 存取)
        // =========================================================

        private async UniTask<Chunk> LoadChunkFromDiskAsync(CPos cPos)
        {
            string location = $"chunk_{WorldId}_{cPos.x}_{cPos.y}";
            string preferredFileName = $"{location}.bytes";
            string localFilePath = Path.Combine(_saveDirectory, preferredFileName);
            byte[] data = null;

            // 1. 優先讀取本地玩家的存檔 (套用文件鎖防止與儲存衝突)
            if (File.Exists(localFilePath))
            {
                var fileLock = GetFileLock(localFilePath);
                await fileLock.WaitAsync();
                try
                {
                    data = await File.ReadAllBytesAsync(localFilePath);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            // 2. 若無本地存檔，嘗試讀取官方發布的預設地圖檔 (YooAsset)
            else
            {
#if UNITY_EDITOR
                // 編輯器專屬捷徑：避免 YooAsset 尚未更新 Manifest 導致讀不到剛建立的資料
                string editorRawPath = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap", $"Zone_{WorldId}", preferredFileName);
                if (File.Exists(editorRawPath))
                {
                    var fileLock = GetFileLock(editorRawPath);
                    await fileLock.WaitAsync();
                    try
                    {
                        data = await File.ReadAllBytesAsync(editorRawPath);
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                }
                else
#endif
                {
                    try
                    {
                        if (ResMgr.Instance.Check(location))
                        {
                            var asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(location);
                            if (asset != null)
                            {
                                data = asset.bytes;
                            }
                            else
                            {
                                Debug.LogWarning($"[World] YooAsset 加載失敗: 無法找到資源 {location}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[World] YooAsset 中不存在資源 {location}，將載入空白區塊。");
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[World] 略過 YooAsset 加載: {e.Message}");
                    }
                }
            }

            // 3. 反序列化並觸發實體生成
            if (data != null && data.Length > 0)
            {
                try
                {
                    ChunkProto proto = ChunkProto.Parser.ParseFrom(data);
                    Chunk loadedChunk = new Chunk(cPos);
                    loadedChunk.LastTime = proto.LastTime;

                    foreach (var ent in proto.Entities)
                    {
                        Pos3 pos = new Pos3(ent.Pos.X, ent.Pos.Y, ent.Pos.H);
                        loadedChunk.AddEntity(ent.Guid, pos);

                        // 通知外部 ECSMgr 利用這個 Proto 資料重建 Atom，並將區塊的時間戳傳遞下去
                        OnEntitySpawn?.Invoke(ent, loadedChunk.LastTime);
                    }
                    return loadedChunk;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[World] 區塊 {location} 反序列化失敗: {e.Message}");
                }
            }

            return null; // 若回傳 null，底層 World 框架會自動 New 一個空的 Chunk
        }

        /// <summary>
        /// 純粹將區塊資料寫入硬碟 (擷取 ECS 最新狀態)。
        /// </summary>
        private async UniTask SaveChunkStateAsync(Chunk chunk)
        {
            ChunkProto proto = new ChunkProto
            {
                Cx = chunk.Pos.x,
                Cy = chunk.Pos.y,
                LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // 逐一向 ECS 索要這塊 Chunk 中所有實體的最新狀態 (ValueBag)
            foreach (long guid in chunk.Entities)
            {
                if (OnEntitySerialize != null)
                {
                    EntityProto latestState = OnEntitySerialize.Invoke(guid);
                    if (latestState != null)
                    {
                        proto.Entities.Add(latestState);
                    }
                }
            }

            // 使用 Protobuf 的 ToByteArray() 直接轉為二進位 (極速，零 GC)
            byte[] dataToSave = proto.ToByteArray();
            string fileName = $"chunk_{WorldId}_{chunk.Pos.x}_{chunk.Pos.y}.bytes";
            string filePath = Path.Combine(_saveDirectory, fileName);

            // 透過 SemaphoreSlim 獲取特定檔案的路徑排他鎖，徹底解決 sharing violation
            var fileLock = GetFileLock(filePath);
            await fileLock.WaitAsync();

            try
            {
                await File.WriteAllBytesAsync(filePath, dataToSave);
            }
            finally
            {
                fileLock.Release();
            }
        }

        /// <summary>
        /// 存檔並觸發實體銷毀 (適用於區塊遠離玩家、切換地圖、離開遊戲時)。
        /// </summary>
        private async UniTask SaveAndUnloadChunkAsync(Chunk chunk)
        {
            // 1. 先寫入硬碟
            await SaveChunkStateAsync(chunk);

            // 2. 廣播銷毀事件，通知外部 ECSMgr.AtomUnload 釋放記憶體
            foreach (long guid in chunk.Entities)
            {
                OnEntityDestroy?.Invoke(guid);
            }
        }

        /// <summary>
        /// 使用 Reflection 從內部 World 獲取活躍區塊
        /// (避免修改 World.cs 原有封裝的便利方法)
        /// </summary>
        public Dictionary<CPos, Chunk> GetActiveChunks()
        {
            if (_world == null) return null;
            var field = typeof(World).GetField("activeChunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) return (Dictionary<CPos, Chunk>)field.GetValue(_world);
            return null;
        }
    }
}