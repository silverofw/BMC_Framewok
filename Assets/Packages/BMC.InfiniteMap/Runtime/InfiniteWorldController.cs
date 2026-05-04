using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks; // 替換為 UniTask
using InfiniteMap;
using BMC.Core; // 引入 ResMgr 等核心功能
using InfiniteMap.Proto; // 引入 Protobuf 生成的資料結構
using Google.Protobuf;   // 引入 Protobuf 核心擴展 (用於 ToByteArray)

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
        // 框架事件 (IoC 反轉控制) - 供 AtomECSMgr 綁定
        // =========================================================

        /// <summary> 當區塊載入，需要生成實體時觸發 (傳出 Proto 以及該區塊的最後存檔時間) </summary>
        public event System.Action<EntityProto, long> OnEntitySpawn;

        /// <summary> 當區塊準備存檔時觸發 (傳入 GUID，請外部回傳最新的 Proto 屬性狀態) </summary>
        public event FetchEntityDataDelegate OnEntitySerialize;

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

            // 效能優化：首次執行或玩家移動超過半格才更新區塊
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
            if (_world == null) return;
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
            if (_world == null || oldPos == newPos) return;

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
        // 系統管理 API (存檔、切換 Zone)
        // =========================================================

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
        // 內部 I/O 實作 (Protobuf 存取)
        // =========================================================

        private async UniTask<Chunk> LoadChunkFromDiskAsync(CPos cPos)
        {
            string location = $"chunk_{WorldId}_{cPos.x}_{cPos.y}.bytes";
            string localFilePath = Path.Combine(_saveDirectory, location);
            byte[] data = null;

            // 1. 優先讀取本地玩家的存檔
            if (File.Exists(localFilePath))
            {
                data = await File.ReadAllBytesAsync(localFilePath);
            }
            // 2. 若無本地存檔，嘗試讀取官方發布的預設地圖檔 (YooAsset)
            else
            {
                try
                {
                    if (ResMgr.Instance.Check(location))
                    {
                        string rawPath = await ResMgr.Instance.LoadRawFilePathAsync(location);
                        if (!string.IsNullOrEmpty(rawPath) && File.Exists(rawPath))
                        {
                            data = await File.ReadAllBytesAsync(rawPath);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[World] 略過 YooAsset 加載: {e.Message}");
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

            await File.WriteAllBytesAsync(filePath, dataToSave);
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