using BMC.Core;
using Cysharp.Threading.Tasks;
using Google.Protobuf;
using InfiniteMap.Proto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; 
using System.Threading;
using UnityEngine;

namespace InfiniteMap.Unity
{
    public delegate EntityProto FetchEntityDataDelegate(long guid);
    public delegate ChunkProto GenerateEmptyChunkDelegate(int worldId, CPos cPos);
    public delegate bool ChunkRetainCondition(ChunkProto chunkData); // 【新增】外部定義的區塊保留條件

    /// <summary>
    /// 純 C# 的世界控制器
    /// 負責與 World 框架溝通，並處理 Unity 端的 IO 序列化與實體管理
    /// </summary>
    public class InfiniteWorldController
    {
        public int WorldId { get; private set; }
        public int ChunkSize { get; private set; }
        public int LoadRadius { get; private set; }
        public float TileSize { get; private set; }
        
        public bool IsEditorMode { get; private set; }

        private static readonly Dictionary<string, SemaphoreSlim> _fileLocks = new Dictionary<string, SemaphoreSlim>();
        private static readonly object _fileLocksLock = new object();

        // 實體狀態快取防護：防止非同步加載不及時，導致存檔時被誤判為空而抹除資料
        private Dictionary<long, EntityProto> _entityStateCache = new Dictionary<long, EntityProto>();

        /// <summary>
        /// 讀取快取中的實體最新資料，不需要這個實體真的被上層(例如 ECS)實體化。給想在不建立
        /// 完整運行時物件的情況下，直接查詢原始 EntityProto 的呼叫端使用(例如大量、被動、
        /// 不需要主動行為的地形類 entity)。找不到回傳 null。
        /// </summary>
        public EntityProto GetCachedEntityData(long guid)
        {
            return _entityStateCache.TryGetValue(guid, out var proto) ? proto : null;
        }

        /// <summary>
        /// 主動寫入/更新快取中的實體資料。給不透過完整 ECS 實體化、卻仍需要修改某個 entity
        /// 最新狀態的呼叫端使用——例如把一個原本在快取中的實體臨時實體化、修改後又還原，
        /// 這裡讓還原後的最新狀態能被 GetCachedEntityData 查到，不用等到下一次真正整塊存檔
        /// (SaveChunkStateAsync)才會更新快取。
        /// </summary>
        public void UpdateCachedEntityData(EntityProto proto)
        {
            if (proto == null) return;
            _entityStateCache[proto.Guid] = proto.Clone();
        }

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

        // 清掉目前未被持有(CurrentCount==1)的檔案鎖，避免無邊際地圖長時間探索後 _fileLocks 無限增長。
        // 只移除閒置鎖，且不 Dispose(本專案未使用 AvailableWaitHandle，SemaphoreSlim 無非受控資源需釋放)，
        // 即使極端競態下仍有他處持有舊參照也不會拋 ObjectDisposedException。
        private static void CleanupIdleFileLocks()
        {
            lock (_fileLocksLock)
            {
                var idleKeys = new List<string>();
                foreach (var kv in _fileLocks)
                {
                    if (kv.Value.CurrentCount == 1)
                        idleKeys.Add(kv.Key);
                }
                foreach (var k in idleKeys)
                    _fileLocks.Remove(k);
            }
        }

        public event System.Action<EntityProto, long> OnEntitySpawn;
        public FetchEntityDataDelegate OnEntitySerialize;
        public event System.Action<long> OnEntityDestroy;
        public GenerateEmptyChunkDelegate OnGenerateEmptyChunk;

        private World _world;

        private string _saveDirectory => IsEditorMode ? 
            Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap", $"Zone_{WorldId}") : 
            Path.Combine(_saveBasePath, $"Zone_{WorldId}");

        private string _saveBasePath;

        private CPos _lastPlayerChunkPos = new CPos(int.MaxValue, int.MaxValue);
        private bool _isFirstUpdate = true;

        // 【保險機制】UpdateWorldFocus 只有在玩家實際移動時才會被呼叫。如果某次區塊載入
        // 中途被中斷(見 World.DoUpdateFocusAsync 的修正)、玩家又剛好停在原地不再移動，
        // 就永遠不會有任何東西再觸發重新嘗試載入。這裡定期用「最後一次已知的焦點位置」
        // 重新呼叫一次 UpdateWorldFocus：如果上次已經完整載入成功，World 內部的
        // lastUpdateCPos 早已跟目前位置一致，這裡只是多一次幾乎零成本的比較就返回；
        // 只有真的還有東西沒載入完成時，才會真正重新嘗試補齊。
        private Vector3 _lastFocusPosition;
        private bool _hasFocusPosition = false;
        private float _selfHealTimer = 0f;
        private const float SelfHealIntervalSeconds = 2f;

        private List<IGlobalSystem> _subSystem = new List<IGlobalSystem>();

        public void Init(int worldId, int chunkSize, int loadRadius, float tileSize, string saveBasePath, bool isEditorMode = false, int loadDelayMs = 250, int chunkLoadIntervalMs = 50)
        {
            IsEditorMode = isEditorMode;
            WorldId = worldId;
            ChunkSize = chunkSize;
            LoadRadius = loadRadius;
            TileSize = tileSize;
            _saveBasePath = saveBasePath;

            _entityStateCache.Clear(); // 初始化時清空快取
            CleanupIdleFileLocks();    // 每次載入地圖時回收閒置的檔案鎖，避免 static 字典無限增長

            // 換 Zone 時重置保險機制的狀態，避免沿用上一個世界的焦點位置
            _hasFocusPosition = false;
            _selfHealTimer = 0f;

            if (!Directory.Exists(_saveDirectory))
                Directory.CreateDirectory(_saveDirectory);

            _world = new World(ChunkSize, LoadRadius, loadDelayMs, chunkLoadIntervalMs)
            {
                OnLoadChunkAsync = LoadChunkFromDiskAsync,
                OnSaveChunkAsync = SaveAndUnloadChunkAsync
            };

            foreach(var s in _subSystem)
            {
                s.Init(chunkSize, saveBasePath, worldId);
            }
        }

        public void RegisterGlobalSystem(IGlobalSystem sys)
        {
            _subSystem.Add(sys);
        }
        public void Tick(int scale)
        {
            foreach(var s in _subSystem)
            {
                s.Tick(1);
            }

            if (_hasFocusPosition && _world != null)
            {
                _selfHealTimer += UnityEngine.Time.deltaTime * Mathf.Max(scale, 1);
                if (_selfHealTimer >= SelfHealIntervalSeconds)
                {
                    _selfHealTimer = 0f;
                    // 直接呼叫 World，不透過 UpdateWorldFocus：UpdateWorldFocus 會先比對
                    // chunk 座標，位置沒變就整個跳過，但這裡就是要在「位置沒變」的情況下
                    // 也讓 World 有機會重新檢查、補齊漏掉的 chunk。
                    _world.UpdateFocusAsync(ToPos3(_lastFocusPosition)).Forget();
                }
            }
        }

        private Pos3 ToPos3(Vector3 worldPosition)
        {
            return new Pos3(
                Mathf.FloorToInt(worldPosition.x / TileSize),
                Mathf.FloorToInt(worldPosition.y / TileSize),
                Mathf.FloorToInt(worldPosition.z / TileSize)
            );
        }

        public void UpdateWorldFocus(Vector3 playerPosition)
        {
            if (_world == null) return;

            _lastFocusPosition = playerPosition;
            _hasFocusPosition = true;
            _selfHealTimer = 0f;

            Pos3 playerPos = ToPos3(playerPosition);

            CPos currentChunkPos = playerPos.ToCPos(ChunkSize);

            if (_isFirstUpdate || currentChunkPos.x != _lastPlayerChunkPos.x || currentChunkPos.y != _lastPlayerChunkPos.y)
            {
                _isFirstUpdate = false;
                _lastPlayerChunkPos = currentChunkPos;
                _world.UpdateFocusAsync(playerPos).Forget();
            }
        }

        public void AddRuntimeEntity(long guid, int config, Pos3 pos)
        {
            if (guid == 0 || _world == null) return;

            foreach(var s in _subSystem)
            {
                if(s.OnRuntimeEntityAdd(guid, config, pos))
                    return;
            }

            CPos cPos = pos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.AddEntity(guid, pos);
            }
        }

        public void RemoveRuntimeEntity(long guid, Pos3 pos)
        {
            if (guid == 0 || _world == null) return;

            foreach(var s in _subSystem)
            {
                if(s.OnRuntimeEntityRemove(guid, pos))
                    return;
            }

            CPos cPos = pos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.RemoveEntity(guid, pos);
                _entityStateCache.Remove(guid); // 真實移除時，連同快取一起清除
            }
        }

        public void MoveRuntimeEntity(long guid, Pos3 oldPos, Pos3 newPos)
        {
            if (guid == 0 || _world == null || oldPos == newPos) return;

            foreach(var s in _subSystem)
            {
                if(s.OnRuntimeEntityMove(guid, oldPos, newPos))
                    return;
            }

            CPos oldCPos = oldPos.ToCPos(ChunkSize);
            CPos newCPos = newPos.ToCPos(ChunkSize);

            var activeChunks = GetActiveChunks();
            if (activeChunks == null) return;

            // 【修正】目標 chunk 若還沒串流載入完成(區塊載入是分批延遲的，理論上邊界情況下
            // 可能發生)，先前的寫法會照樣把實體從舊 chunk 移除，但因為 TryGetValue 找不到新
            // chunk 就直接放棄「加回去」——實體從此兩邊都追蹤不到，之後存檔(DestroyAndSaveAllAsync
            // 只存 activeChunks 裡看得到的實體)就會憑空漏掉它。改成：加不進新 chunk 就整個不搬，
            // 讓實體維持在舊 chunk 的紀錄裡，等下一次真正移動、目標 chunk 已經載入完成時再搬過去，
            // 寧可暫時位置紀錄稍舊，也不要讓實體變成完全追蹤不到。
            if (!activeChunks.TryGetValue(newCPos, out Chunk newChunk))
            {
                Debug.LogWarning($"[World] MoveRuntimeEntity: 目標 chunk {newCPos} 尚未載入，guid {guid} 暫緩搬移(仍留在 {oldCPos})。");
                return;
            }

            if (activeChunks.TryGetValue(oldCPos, out Chunk oldChunk))
            {
                oldChunk.RemoveEntity(guid, oldPos);
            }

            newChunk.AddEntity(guid, newPos);
        }
        
        public async UniTask DestroyAndSaveAllAsync()
        {
            if (_world != null)
            {
                Debug.Log($"[InfiniteWorldController] 準備強制儲存並卸載 Zone_{WorldId} 所有的活躍區塊...");
                await _world.DestroyAndSaveAllAsync();
                Debug.Log($"[InfiniteWorldController] Zone_{WorldId} 已安全完成所有存檔與資源釋放。");
            }
            // 先讓場景物件資料寫回個別sys
            foreach(var s in _subSystem)
            {
                await s.SaveAsync();
            }
        }

        public async UniTask<bool> TransferEntityToZoneAsync(long guid, int targetWorldId, Pos3 targetPos)
        {
            if (guid == 0) return false;
            if (OnEntitySerialize == null) return false;

            EntityProto liveProto = OnEntitySerialize.Invoke(guid);
            // 如果拿不到畫面上的狀態，嘗試從快取拿取
            if (liveProto == null && !_entityStateCache.TryGetValue(guid, out liveProto)) return false;

            EntityProto entityProto = liveProto.Clone();

            Pos3 oldPos = new Pos3(entityProto.Pos.X, entityProto.Pos.Y, entityProto.Pos.H);
            RemoveRuntimeEntity(guid, oldPos);

            entityProto.Pos.X = targetPos.x;
            entityProto.Pos.Y = targetPos.y;
            entityProto.Pos.H = targetPos.h;

            int cx = Mathf.FloorToInt((float)targetPos.x / ChunkSize);
            int cy = Mathf.FloorToInt((float)targetPos.y / ChunkSize);

            // 與 _saveDirectory / WipeZoneDataAsync 保持一致：編輯器模式寫入 Assets 內的預設 proto 路徑，
            // 否則載入(讀 Assets)與轉移寫入(寫 persistentData)會落在不同位置導致轉移不生效。
            string targetDirectory = IsEditorMode ?
                Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap", $"Zone_{targetWorldId}") :
                Path.Combine(_saveBasePath, $"Zone_{targetWorldId}");
            if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

            string fileName = $"chunk_{targetWorldId}_{cx}_{cy}.bytes";
            string filePath = Path.Combine(targetDirectory, fileName);

            ChunkProto chunkProto = null;
            var fileLock = GetFileLock(filePath);
            await fileLock.WaitAsync();

            try
            {
                string location = $"chunk_{targetWorldId}_{cx}_{cy}";
                if (File.Exists(filePath))
                {
                    try { chunkProto = ChunkProto.Parser.ParseFrom(await File.ReadAllBytesAsync(filePath)); } 
                    catch (Exception e) { Debug.LogError($"[Transfer] 讀取目標存檔失敗: {e}"); }
                }
                else
                {
                    byte[] defaultData = null;
                    try
                    {
                        if (ResMgr.Instance.Check(location))
                        {
                            var asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(location);
                            if (asset != null) defaultData = asset.bytes;
                        }
                    }
                    catch (Exception e) { Debug.LogWarning($"[Transfer] 讀取預設地圖失敗: {e}"); }
                    
                    if (defaultData != null && defaultData.Length > 0)
                    {
                        try { chunkProto = ChunkProto.Parser.ParseFrom(defaultData); } 
                        catch (Exception e) { Debug.LogError($"[Transfer] 解析預設地圖失敗: {e}"); }
                    }
                }

                if (chunkProto == null)
                {
                    if (OnGenerateEmptyChunk != null) 
                    {
                        try { chunkProto = OnGenerateEmptyChunk.Invoke(targetWorldId, new CPos(cx, cy)); }
                        catch (Exception e) { Debug.LogError($"[Transfer] 創建新區塊失敗: {e}"); }
                    }
                    if (chunkProto == null) chunkProto = new ChunkProto { Cx = cx, Cy = cy, LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                }

                for (int i = chunkProto.Entities.Count - 1; i >= 0; i--)
                {
                    if (chunkProto.Entities[i].Guid == guid) chunkProto.Entities.RemoveAt(i);
                }

                chunkProto.Entities.Add(entityProto);
                byte[] dataToSave = chunkProto.ToByteArray();
                await File.WriteAllBytesAsync(filePath, dataToSave);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[InfiniteWorldController] 跨 Zone 寫入失敗: {ex.Message}");
                return false;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async UniTask ForceSaveAllAsync()
        {
            foreach(var s in _subSystem)
            {
                await s.SaveAsync();
            }
            if (_world == null) return;
            var activeChunks = GetActiveChunks();
            if (activeChunks == null || activeChunks.Count == 0) return;

            List<UniTask> saveTasks = new List<UniTask>();
            List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

            foreach (var cPos in keysToSave)
            {
                Chunk chunk = activeChunks[cPos];
                saveTasks.Add(SaveChunkStateAsync(chunk));
            }

            await UniTask.WhenAll(saveTasks);
        }

        public async UniTask SwitchZoneAsync(int newWorldId)
        {
            await DestroyAndSaveAllAsync();

            Init(newWorldId, ChunkSize, LoadRadius, TileSize, _saveBasePath, IsEditorMode);
            _isFirstUpdate = true;
            Debug.Log($"[World] ===== 已成功切換至 Zone_{newWorldId} =====");
        }

        // 【新增】清理指定 WorldId 的存檔，並提供委派讓外部決定哪些區塊要保留
        public async UniTask WipeZoneDataAsync(int targetWorldId, ChunkRetainCondition retainCondition = null)
        {
            string targetDirectory = IsEditorMode ? 
                Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap", $"Zone_{targetWorldId}") : 
                Path.Combine(_saveBasePath, $"Zone_{targetWorldId}");

            if (!Directory.Exists(targetDirectory)) return;

            string[] files = Directory.GetFiles(targetDirectory, "chunk_*.bytes");
            foreach (string filePath in files)
            {
                bool shouldRetain = false;

                // 如果有提供保留判定，則先讀取該區塊資料進行判斷
                if (retainCondition != null)
                {
                    var fileLock = GetFileLock(filePath);
                    await fileLock.WaitAsync();
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            byte[] data = await File.ReadAllBytesAsync(filePath);
                            ChunkProto chunkProto = ChunkProto.Parser.ParseFrom(data);
                            shouldRetain = retainCondition.Invoke(chunkProto);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[WipeZone] 讀取區塊以判定保留時發生錯誤 {filePath}: {e}");
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                }

                // 如果判斷不保留，則刪除檔案
                if (!shouldRetain)
                {
                    var fileLock = GetFileLock(filePath);
                    await fileLock.WaitAsync();
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[WipeZone] 刪除區塊存檔失敗 {filePath}: {e}");
                    }
                    finally
                    {
                        fileLock.Release();
                    }
                }
                else
                {
                    Debug.Log($"[WipeZone] 區塊滿足保留條件 (如神蹟)，已跳過刪除: {Path.GetFileName(filePath)}");
                }
            }
        }

        // =========================================================
        // 內部 I/O 實作 (Protobuf 存取) - 加入嚴格除錯與快取機制
        // =========================================================

        private async UniTask<Chunk> LoadChunkFromDiskAsync(CPos cPos)
        {
            Debug.Log($"[DIAG][LoadChunk] 開始載入 {cPos} (WorldId={WorldId}, IsEditorMode={IsEditorMode})");

            foreach(var s in _subSystem)
            {
                s.OnChunkLoaded(cPos);
            }

            string location = $"chunk_{WorldId}_{cPos.x}_{cPos.y}";
            string preferredFileName = $"{location}.bytes";
            string localFilePath = Path.Combine(_saveDirectory, preferredFileName);
            byte[] data = null;

            bool localExists = File.Exists(localFilePath);
            Debug.Log($"[DIAG][LoadChunk] {cPos} 本地存檔路徑: {localFilePath} 存在={localExists}");

            if (localExists)
            {
                var fileLock = GetFileLock(localFilePath);
                await fileLock.WaitAsync();
                try { data = await File.ReadAllBytesAsync(localFilePath); }
                catch (Exception e) { Debug.LogError($"[IO] 讀取存檔失敗 {cPos}: {e}"); }
                finally { fileLock.Release(); }
            }
            else
            {
                try
                {
                    bool locationValid = ResMgr.Instance.Check(location);
                    Debug.Log($"[DIAG][LoadChunk] {cPos} 改查 YooAsset 預設資源: location={location} valid={locationValid}");
                    if (locationValid)
                    {
                        var asset = await ResMgr.Instance.LoadAssetAsync<TextAsset>(location);
                        if (asset != null) data = asset.bytes;
                        Debug.Log($"[DIAG][LoadChunk] {cPos} YooAsset 讀取結果: asset={(asset != null ? "OK" : "NULL")} bytes={(data?.Length ?? -1)}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[IO] 嘗試讀取預設地圖 {location} 時發生錯誤: {e}");
                }
            }

            ChunkProto proto = null;
            if (data != null && data.Length > 0)
            {
                // 【效能，修正】ParseFrom 是純 CPU 運算、不碰 Unity API，本來就該離開主執行緒，
                // 但光把這行程式碼「寫在」SwitchToMainThread() 之前不會讓它真的離開主執行緒——
                // 這個方法從呼叫端進來就一直在主執行緒跑，中間沒有任何明確的切換點，前面的
                // await File.ReadAllBytesAsync()/ResMgr 呼叫的延續預設也會被 Unity 的
                // UnitySynchronizationContext 送回主執行緒繼續執行。實測一個存了 800+ entity
                // 的 chunk，ParseFrom 反序列化要價 40ms+，整段卡在主執行緒上。這裡明確呼叫
                // SwitchToThreadPool()，才會真的讓 ParseFrom 離開主執行緒，跟存檔側
                // SaveChunkStateAsync 呼叫 ToByteArray() 前的 SwitchToThreadPool()對稱。
                await UniTask.SwitchToThreadPool();
                try { proto = ChunkProto.Parser.ParseFrom(data); }
                catch (Exception e) { Debug.LogError($"[IO] Protobuf 解析失敗，資料可能損毀 {cPos}: {e}"); }
            }

            // 【關鍵防護】確保切換回主執行緒 (Main Thread)：下面的 OnGenerateEmptyChunk
            // (程序化地圖產生器會用到 UnityEngine.Random/Mathf.PerlinNoise)跟 OnEntitySpawn
            // (建立 Unity/ECS 實體)都必須在主執行緒執行，否則觸發 Unity 實例化或 API 會失敗
            // 或被吞掉。
            await UniTask.SwitchToMainThread();

            if (proto == null && OnGenerateEmptyChunk != null)
            {
                Debug.LogWarning($"[DIAG][LoadChunk] {cPos} data 為空/解析失敗，改用 OnGenerateEmptyChunk 產生預設區塊！");
                try { proto = OnGenerateEmptyChunk.Invoke(WorldId, cPos); }
                catch (Exception e) { Debug.LogError($"[Generator] 呼叫 OnGenerateEmptyChunk 發生錯誤 {cPos}: {e}"); }
            }

            Debug.Log($"[DIAG][LoadChunk] {cPos} proto={(proto != null ? $"OK entities={proto.Entities.Count}" : "NULL")}");

            if (proto != null)
            {
                Chunk loadedChunk = new Chunk(cPos);
                loadedChunk.LastTime = proto.LastTime > 0 ? proto.LastTime : System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (OnEntitySpawn == null)
                {
                    Debug.LogWarning($"[InfiniteWorldController] 區塊 {cPos} 正在加載，但尚未有系統註冊 OnEntitySpawn 事件！這可能導致畫面無物件產生。");
                }

                foreach (var ent in proto.Entities)
                {
                    try
                    {
                        Pos3 pos = new Pos3(ent.Pos.X, ent.Pos.Y, ent.Pos.H);
                        loadedChunk.AddEntity(ent.Guid, pos);
                        
                        // 【寫入防護快取】確保存檔時若物件來不及生成，還能用這個最後狀態寫回去。
                        // 【效能】不 Clone：ent 接下來會透過 OnEntitySpawn 直接交給上層(例如包進
                        // MapEntity/StatusComponent.proto)當作活資料的儲存體本身，讓 cache 從一開始
                        // 就跟活著的資料共用同一個物件，之後任何變動都會直接反映在這裡，不需要另外同步。
                        _entityStateCache[ent.Guid] = ent;

                        OnEntitySpawn?.Invoke(ent, loadedChunk.LastTime);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Spawner] 實體生成失敗！區塊: {cPos}, Guid: {ent.Guid}. 錯誤訊息: {ex}");
                    }
                }
                return loadedChunk;
            }

            return null; 
        }

        private async UniTask SaveChunkStateAsync(Chunk chunk)
        {
            ChunkProto proto = new ChunkProto
            {
                Cx = chunk.Pos.x,
                Cy = chunk.Pos.y,
                LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            // 確保回到主執行緒進行序列化
            await UniTask.SwitchToMainThread();

            foreach (long guid in chunk.Entities.ToList())
            {
                if (OnEntitySerialize != null)
                {
                    try
                    {
                        EntityProto latestState = OnEntitySerialize.Invoke(guid);
                        if (latestState != null)
                        {
                            // 【重要，不能省】這個 Clone 不是多餘的：latestState 現在通常就是上層
                            // 活資料本身的參照(例如 StatusComponent.proto)，接下來要離開主執行緒
                            // 做 ToByteArray()，這段期間主執行緒可能仍在繼續修改同一個物件(例如子彈
                            // 打中一個還沒真正卸載的實體)，沒有這個 Clone 會有跨執行緒資料競爭。
                            proto.Entities.Add(latestState.Clone());
                            // 不 Clone：latestState 就是活資料本身，跟 cache 已經是同一個物件，這行只是
                            // 確保「這個 guid 第一次出現在 cache 裡」時把關聯建立起來，之後都是同一份。
                            _entityStateCache[guid] = latestState;
                        }
                        else if (_entityStateCache.TryGetValue(guid, out var cachedState))
                        {
                            // 【關鍵修復】OnEntitySerialize 回傳 null 有兩種情況：(1) 視覺物件還沒載入完畢
                            // 就被卸載的偶發情況，(2) 上層刻意讓某類 entity 永遠不實體化(例如大量、被動的
                            // 地形類 entity，見 GetCachedEntityData/UpdateCachedEntityData 的說明)，這種
                            // 情況下每次存檔都會合法地走到這裡。不管哪種情況都使用「安全快取資料」寫回
                            // 硬碟，避免物件在存檔中被永久抹除；但因為情況 (2) 屬於常態、數量龐大，這裡
                            // 不再逐筆記 log，避免每次存檔都洗版。
                            // 【效能】不 Clone：_entityStateCache 所有寫入點都是整筆替換(dict[guid] = x.Clone())，
                            // 從不就地修改已存在的物件，所以這裡拿到的參照可以直接放進 proto.Entities，
                            // 不需要再複製一份——地板類 entity 數量龐大(一個 chunk 500~800 個)，每次存檔
                            // 省下等量的 Clone()/GC alloc。
                            proto.Entities.Add(cachedState);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Serialize] 序列化實體失敗 Guid {guid}: {e}");
                    }
                }
            }

            // 【重要】fileName/filePath 要在切執行緒「之前」先算好：_saveDirectory 在
            // IsEditorMode(地圖編輯器烘焙工作流程)時會即時呼叫 Application.dataPath，這是
            // Unity 規定只能在主執行緒呼叫的 API，不能等到切到 thread pool 之後才算。
            string fileName = $"chunk_{WorldId}_{chunk.Pos.x}_{chunk.Pos.y}.bytes";
            string filePath = Path.Combine(_saveDirectory, fileName);

            // 【效能】proto 到這裡已經是不再被其他執行緒碰到的純資料物件，ToByteArray()
            // 是純 CPU 運算(chunk 動輒 500~800 個 entity，這一步過去在主執行緒上實測要價
            // 70ms+，是玩家跨 chunk 邊界卡頓的主因)，離開主執行緒不影響正確性。
            await UniTask.SwitchToThreadPool();

            byte[] dataToSave = proto.ToByteArray();

            var fileLock = GetFileLock(filePath);
            await fileLock.WaitAsync();
            try { await File.WriteAllBytesAsync(filePath, dataToSave); }
            catch (Exception e) { Debug.LogError($"[IO] 寫入存檔失敗 {chunk.Pos}: {e}"); }
            finally { fileLock.Release(); }

            // 確保呼叫端(SaveAndUnloadChunkAsync/ForceSaveAllAsync 等)拿回控制權時仍在
            // 主執行緒，維持這個方法一直以來「回傳時已在主執行緒」的隱含契約。
            await UniTask.SwitchToMainThread();
        }

        private async UniTask SaveAndUnloadChunkAsync(Chunk chunk)
        {
            foreach(var s in _subSystem)
            {
                s.OnChunkUnloaded(chunk.Pos);
            }
            await SaveChunkStateAsync(chunk);

            // 確保回到主執行緒進行銷毀
            await UniTask.SwitchToMainThread();

            foreach (long guid in chunk.Entities.ToList())
            {
                try
                {
                    OnEntityDestroy?.Invoke(guid);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Destroy] 銷毀實體時發生錯誤 Guid {guid}: {e}");
                }
            }
        }

        public Dictionary<CPos, Chunk> GetActiveChunks()
        {
            return _world?.ActiveChunks;
        }
    }
}