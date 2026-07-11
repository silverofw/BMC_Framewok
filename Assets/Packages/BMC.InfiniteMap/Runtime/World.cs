using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace InfiniteMap
{
    /// <summary>
    /// 無邊際世界管理器 (完全獨立不依賴 MonoBehaviour 或 Singleton)
    /// 採用「單一序列佇列鎖」與「防抖延遲」架構，極致優化效能並解決邊界反覆橫跳問題。
    /// 加入了「平滑加載間隔」，將多區塊的生成壓力分散到不同幀。
    /// </summary>
    public class World
    {
        public int ChunkSize { get; private set; }
        public int LoadRadius { get; set; } 
        public int LoadDelayMs { get; set; } 
        public int ChunkLoadIntervalMs { get; set; }

        private Dictionary<CPos, Chunk> activeChunks;
        private CPos lastUpdateCPos = new CPos(int.MaxValue, int.MaxValue);

        public Func<CPos, UniTask<Chunk>> OnLoadChunkAsync;
        public Func<Chunk, UniTask> OnSaveChunkAsync;

        private bool _isUpdatingFocus = false;
        private Pos3? _pendingFocusPos = null;
        
        // 【新增】防殭屍載入標記：避免世界銷毀後，舊的非同步任務還在背景偷塞區塊
        private bool _isDestroyed = false;

        public World(int chunkSize = 16, int loadRadius = 1, int loadDelayMs = 250, int chunkLoadIntervalMs = 100)
        {
            ChunkSize = chunkSize;
            LoadRadius = loadRadius;
            LoadDelayMs = loadDelayMs;
            ChunkLoadIntervalMs = chunkLoadIntervalMs;
            activeChunks = new Dictionary<CPos, Chunk>();
        }

        public async UniTask UpdateFocusAsync(Pos3 focusPos)
        {
            if (_isDestroyed) return; // 系統已關閉，拒絕更新

            _pendingFocusPos = focusPos;

            if (_isUpdatingFocus) return;

            _isUpdatingFocus = true;

            try
            {
                if (LoadDelayMs > 0)
                {
                    await UniTask.Delay(LoadDelayMs);
                }

                while (_pendingFocusPos.HasValue)
                {
                    if (_isDestroyed) return;

                    Pos3 nextTarget = _pendingFocusPos.Value;
                    _pendingFocusPos = null;

                    try
                    {
                        await DoUpdateFocusAsync(nextTarget);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[World] 更新地圖焦點時發生嚴重錯誤: {ex}");
                    }

                    if (_pendingFocusPos.HasValue && LoadDelayMs > 0)
                    {
                        await UniTask.Delay(LoadDelayMs);
                    }
                }
            }
            finally
            {
                _isUpdatingFocus = false;
            }
        }

        private async UniTask DoUpdateFocusAsync(Pos3 focusPos)
        {
            if (_isDestroyed) return;

            CPos currentCPos = focusPos.ToCPos(ChunkSize);

            // 如果目標沒變，就不用重新跑
            if (currentCPos == lastUpdateCPos) return;
            lastUpdateCPos = currentCPos;

            HashSet<CPos> neededChunks = new HashSet<CPos>();

            for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
            {
                for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
                {
                    neededChunks.Add(new CPos(currentCPos.x + dx, currentCPos.y + dy));
                }
            }

            // 1. 卸載不需要的區塊 (卸載不能被中斷，必須確保資料存檔落地)
            List<CPos> toUnload = new List<CPos>();
            foreach (var kvp in activeChunks)
            {
                if (!neededChunks.Contains(kvp.Key))
                {
                    toUnload.Add(kvp.Key);
                }
            }

            foreach (var cPos in toUnload)
            {
                if (activeChunks.TryGetValue(cPos, out Chunk chunk))
                {
                    activeChunks.Remove(cPos);

                    if (OnSaveChunkAsync != null)
                    {
                        try
                        {
                            await OnSaveChunkAsync(chunk);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"[World] 卸載區塊 {cPos} 時發生錯誤: {ex}");
                        }
                    }
                }
            }

            // 2. 加載需要的區塊
            bool hasLoadedAny = false;
            foreach (var cPos in neededChunks)
            {
                if (_isDestroyed) return;

                // 【關鍵修復】防呆中斷機制：如果加載途中焦點改變（玩家跑走了），直接放棄過時的加載
                // 讓外層迴圈能立刻接手新焦點，避免「剛加載完下一秒就被卸載」導致存檔被清空的災難。
                if (_pendingFocusPos.HasValue) return;

                if (!activeChunks.ContainsKey(cPos))
                {
                    if (hasLoadedAny && ChunkLoadIntervalMs > 0)
                    {
                        await UniTask.Delay(ChunkLoadIntervalMs);
                        
                        // 延遲醒來後再次檢查是否已經過時或被銷毀
                        if (_isDestroyed || _pendingFocusPos.HasValue) return;
                    }

                    Chunk newChunk = null;
                    if (OnLoadChunkAsync != null)
                    {
                        try
                        {
                            newChunk = await OnLoadChunkAsync(cPos);
                        }
                        catch (Exception ex)
                        {
                            UnityEngine.Debug.LogError($"[World] 載入區塊 {cPos} 時發生錯誤: {ex}");
                        }
                    }

                    if (newChunk == null)
                    {
                        newChunk = new Chunk(cPos);
                    }

                    // 確保世界還活著才塞進活躍字典裡
                    if (!_isDestroyed)
                    {
                        activeChunks[cPos] = newChunk;
                    }
                    hasLoadedAny = true; 
                }
            }
        }
        
        public async UniTask DestroyAndSaveAllAsync()
        {
            _isDestroyed = true; // 【關鍵修復】標記為銷毀，斬斷所有還在背景跑的加載任務

            if (activeChunks.Count == 0) return;

            _pendingFocusPos = null;
            lastUpdateCPos = new CPos(int.MaxValue, int.MaxValue);

            List<UniTask> saveTasks = new List<UniTask>();
            List<CPos> keysToSave = new List<CPos>(activeChunks.Keys);

            foreach (var cPos in keysToSave)
            {
                if (activeChunks.TryGetValue(cPos, out Chunk chunk))
                {
                    activeChunks.Remove(cPos);

                    if (OnSaveChunkAsync != null)
                    {
                        await OnSaveChunkAsync(chunk);
                        //saveTasks.Add(OnSaveChunkAsync(chunk));
                    }
                }
            }

            //await UniTask.WhenAll(saveTasks);
        }

        public void QueryArea(Pos3 center, int radius, List<long> results)
        {
            results.Clear();

            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int y = center.y - radius; y <= center.y + radius; y++)
                {
                    Pos3 searchPos = new Pos3(x, y, center.h);
                    CPos cPos = searchPos.ToCPos(ChunkSize);

                    if (activeChunks.TryGetValue(cPos, out Chunk chunk))
                    {
                        chunk.GetEntitiesAt(searchPos, results);
                    }
                }
            }
        }
    }
}