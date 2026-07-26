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
        // 提供給 InfiniteWorldController 直接存取，取代原本每次呼叫的反射
        public Dictionary<CPos, Chunk> ActiveChunks => activeChunks;
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
            // 【修復】原本這裡一旦焦點中途改變就直接 return，但 lastUpdateCPos 在方法一開始就已經
            // 寫入了，導致「這個位置處理過了」的標記跟「這個位置的 chunk 真的都載入完成了」脫鉤——
            // 如果角色連續移動導致這裡被中斷、還有些 chunk 沒真的塞進 activeChunks，
            // 等角色停下來不再移動時，因為 lastUpdateCPos 已經等於 currentCPos，就再也不會重新
            // 嘗試補齊那些漏掉的 chunk，畫面上該處的地板/物件就永久是空的，只能離開重進才會恢復。
            // 現在改成：只有整批 neededChunks 都確認在 activeChunks 裡、完全沒被中斷時，
            // 才把 lastUpdateCPos 更新成 currentCPos；被中斷的話維持原值，之後角色只要再度停在
            // 同一個位置，就會重新觸發、補載入還沒完成的 chunk。
            bool completed = true;
            bool hasLoadedAny = false;
            foreach (var cPos in neededChunks)
            {
                if (_isDestroyed) return;

                if (_pendingFocusPos.HasValue)
                {
                    completed = false;
                    break;
                }

                if (!activeChunks.ContainsKey(cPos))
                {
                    if (hasLoadedAny && ChunkLoadIntervalMs > 0)
                    {
                        await UniTask.Delay(ChunkLoadIntervalMs);

                        if (_isDestroyed) return;
                        if (_pendingFocusPos.HasValue)
                        {
                            completed = false;
                            break;
                        }
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

            if (completed)
            {
                lastUpdateCPos = currentCPos;
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