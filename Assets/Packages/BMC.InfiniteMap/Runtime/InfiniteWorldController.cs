using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using InfiniteMap;
using BMC.Core; // 引入 ResMgr 所在的命名空間
using InfiniteMap.Proto; // 引入 Protobuf 生成的資料結構
using Google.Protobuf;   // 引入 Protobuf 核心擴展 (用於 ToByteArray)

namespace InfiniteMap.Unity
{
    // 定義向外部索取資料的委派
    public delegate EntityProto FetchEntityDataDelegate(long guid);

    /// <summary>
    /// 純 C# 的世界控制器 (不再繼承 MonoBehaviour)
    /// 負責與 World 框架溝通，並處理 Unity 端的 IO 序列化
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
        // 框架事件 (IoC 反轉控制) - 請透過外部管理器程式碼綁定
        // =========================================================

        /// <summary> 當區塊載入，需要生成實體時觸發 (傳出 Proto，由外部負責實例化) </summary>
        // 【修改 1】將委派多加一個 long 參數，用來傳遞區塊最後存檔的時間
        public event System.Action<EntityProto, long> OnEntitySpawn;

        /// <summary> 當區塊準備存檔時觸發 (傳入 GUID，請外部回傳最新的 Proto 屬性狀態) </summary>
        public event FetchEntityDataDelegate OnEntitySerialize;

        /// <summary> 當區塊卸載完成時觸發 (傳出 GUID，請外部銷毀對應的 ECS/GameObject) </summary>
        public event System.Action<long> OnEntityDestroy;

        // 純 C# 的世界核心
        private World _world;
        // 存檔路徑 (用於存檔/覆寫時的本地端路徑)
        private string _saveDirectory;
        private string _saveBasePath; // 紀錄根目錄，供切換 Zone 時使用
        // 紀錄玩家上一次的位置，避免每幀無意義的計算
        private Vector3 _lastPlayerPos;
        // 紀錄是否為首次更新
        private bool _isFirstUpdate = true;

        /// <summary>
        /// 初始化控制器
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

            // 注意：底層 World 框架觸發的必定是「卸載」，所以綁定卸載版的方法
            _world.OnSaveChunkAsync = SaveAndUnloadChunkAsync;
        }

        /// <summary>
        /// 外部驅動的 Tick 邏輯
        /// </summary>
        public void Tick(Vector3 playerPosition)
        {
            if (_world == null) return;

            // 效能優化：首次執行或玩家移動超過一定距離才更新區塊
            if (_isFirstUpdate || Vector3.Distance(playerPosition, _lastPlayerPos) > TileSize * 0.5f)
            {
                _isFirstUpdate = false;
                _lastPlayerPos = playerPosition;

                // 將 Unity Vector3 轉換為底層框架的 Pos3
                Pos3 playerPos = new Pos3(
                    Mathf.FloorToInt(playerPosition.x / TileSize),
                    Mathf.FloorToInt(playerPosition.z / TileSize),
                    Mathf.FloorToInt(playerPosition.y / TileSize)
                );

                // 觸發區塊更新 (Fire and Forget)
                _ = _world.UpdateFocusAsync(playerPos);
            }
        }

        // =========================================================
        // 系統管理 API (存檔、切換 Zone)
        // =========================================================

        /// <summary>
        /// 單純的強制存檔 (不銷毀實體，適用於玩家主動點擊「儲存遊戲」)
        /// </summary>
        public async Task ForceSaveAllAsync()
        {
            if (_world == null) return;
            var activeChunks = GetActiveChunks();
            if (activeChunks == null || activeChunks.Count == 0) return;

            List<Task> saveTasks = new List<Task>();
            List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

            foreach (var cPos in keysToSave)
            {
                Chunk chunk = activeChunks[cPos];
                // 這裡只呼叫純存檔方法，不觸發 ECS 銷毀
                saveTasks.Add(SaveChunkStateAsync(chunk));
            }

            await Task.WhenAll(saveTasks);
            Debug.Log($"[World] 已成功儲存目前進度 (共 {keysToSave.Count} 個活躍區塊)。");
        }

        /// <summary>
        /// 切換至新的地圖 Zone。
        /// 將會強制儲存並卸載當前世界所有的活躍區塊，然後重新初始化系統。
        /// </summary>
        public async Task SwitchZoneAsync(int newWorldId)
        {
            if (_world != null)
            {
                var activeChunks = GetActiveChunks();
                if (activeChunks != null && activeChunks.Count > 0)
                {
                    List<Task> unloadTasks = new List<Task>();
                    List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

                    // 存檔並強制銷毀當前世界上所有的 ECS 實體
                    foreach (var cPos in keysToSave)
                    {
                        unloadTasks.Add(SaveAndUnloadChunkAsync(activeChunks[cPos]));
                    }
                    await Task.WhenAll(unloadTasks);
                }
            }

            // 重新初始化為新的世界編號
            Init(newWorldId, ChunkSize, LoadRadius, TileSize, _saveBasePath);
            _isFirstUpdate = true; // 重置標記，確保下一個 Tick 會立刻載入新世界的九宮格
            Debug.Log($"[World] ===== 已成功切換至 Zone_{newWorldId} =====");
        }

        // =========================================================
        // 運行時動態操作介面 (Runtime API)
        // =========================================================

        public void AddRuntimeEntity(long guid, Pos3 pos)
        {
            if (_world == null) return;
            CPos cPos = pos.ToCPos(ChunkSize);
            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.AddEntity(guid, pos);
            }
        }

        public void RemoveRuntimeEntity(long guid, Pos3 pos)
        {
            if (_world == null) return;
            CPos cPos = pos.ToCPos(ChunkSize);
            var activeChunks = GetActiveChunks();
            if (activeChunks != null && activeChunks.TryGetValue(cPos, out Chunk chunk))
            {
                chunk.RemoveEntity(guid, pos);
            }
        }

        public void MoveRuntimeEntity(long guid, Pos3 oldPos, Pos3 newPos)
        {
            if (_world == null || oldPos == newPos) return;
            CPos oldCPos = oldPos.ToCPos(ChunkSize);
            CPos newCPos = newPos.ToCPos(ChunkSize);
            var activeChunks = GetActiveChunks();
            if (activeChunks == null) return;

            if (activeChunks.TryGetValue(oldCPos, out Chunk oldChunk))
                oldChunk.RemoveEntity(guid, oldPos);

            if (activeChunks.TryGetValue(newCPos, out Chunk newChunk))
                newChunk.AddEntity(guid, newPos);
        }

        // =========================================================
        // 內部 I/O 實作
        // =========================================================

        private async Task<Chunk> LoadChunkFromDiskAsync(CPos cPos)
        {
            string location = $"chunk_{WorldId}_{cPos.x}_{cPos.y}.bytes";
            string localFilePath = Path.Combine(_saveDirectory, location);
            byte[] data = null;

            if (File.Exists(localFilePath))
            {
                data = await File.ReadAllBytesAsync(localFilePath);
            }
            else
            {
                try
                {
                    if (ResMgr.Instance.Check(location))
                    {
                        string rawPath = await ResMgr.Instance.LoadRawFilePathAsync(location);
                        if (!string.IsNullOrEmpty(rawPath) && File.Exists(rawPath))
                            data = await File.ReadAllBytesAsync(rawPath);
                    }
                }
                catch (System.Exception e) { Debug.LogWarning($"[World] 略過 YooAsset 加載: {e.Message}"); }
            }

            if (data != null && data.Length > 0)
            {
                try
                {
                    ChunkProto proto = ChunkProto.Parser.ParseFrom(data);
                    Chunk loadedChunk = new Chunk(cPos);
                    loadedChunk.LastTime = proto.LastTime;

                    // 【修改 2】在 LoadChunkFromDiskAsync 的 foreach 迴圈中，把 proto.LastTime 傳出去
                    foreach (var ent in proto.Entities)
                    {
                        Pos3 pos = new Pos3(ent.Pos.X, ent.Pos.Y, ent.Pos.H);
                        loadedChunk.AddEntity(ent.Guid, pos);

                        // 將區塊的最後存檔時間 (proto.LastTime) 一併拋出給 ECS
                        OnEntitySpawn?.Invoke(ent, proto.LastTime);
                    }
                    return loadedChunk;
                }
                catch (System.Exception e) { Debug.LogError($"[World] 區塊 {location} 反序列化失敗: {e.Message}"); }
            }
            return null;
        }

        /// <summary>
        /// 純粹將區塊資料寫入硬碟 (不觸發實體銷毀)。
        /// </summary>
        private async Task SaveChunkStateAsync(Chunk chunk)
        {
            ChunkProto proto = new ChunkProto { Cx = chunk.Pos.x, Cy = chunk.Pos.y, LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

            foreach (long guid in chunk.Entities)
            {
                if (OnEntitySerialize != null)
                {
                    EntityProto latestState = OnEntitySerialize.Invoke(guid);
                    if (latestState != null) proto.Entities.Add(latestState);
                }
            }

            byte[] dataToSave = proto.ToByteArray();
            string fileName = $"chunk_{WorldId}_{chunk.Pos.x}_{chunk.Pos.y}.bytes";
            string filePath = Path.Combine(_saveDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, dataToSave);
        }

        /// <summary>
        /// 存檔並觸發實體銷毀 (適用於區塊遠離卸載、切換地圖、離開遊戲)。
        /// </summary>
        private async Task SaveAndUnloadChunkAsync(Chunk chunk)
        {
            // 1. 先寫入硬碟
            await SaveChunkStateAsync(chunk);

            // 2. 廣播銷毀事件，通知外部 ECS 釋放記憶體
            foreach (long guid in chunk.Entities)
            {
                OnEntityDestroy?.Invoke(guid);
            }
        }

        public Dictionary<CPos, Chunk> GetActiveChunks()
        {
            if (_world == null) return null;
            var field = typeof(World).GetField("activeChunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) return (Dictionary<CPos, Chunk>)field.GetValue(_world);
            return null;
        }
    }
}