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

        private int _loadRadius;
        /// <summary>
        /// 載入半徑。**執行期改這個值是允許的**(例如攝影機拉遠要多載一圈)。
        ///
        /// 【改值時一定要重置 lastUpdateCPos】DoUpdateFocusAsync 開頭有
        /// 「currentCPos == lastUpdateCPos 就整個跳過」的早退，只改半徑不重置的話，
        /// 要等玩家實際跨過一次 chunk 邊界才會生效 —— 站著按縮放鍵完全沒反應。
        /// </summary>
        public int LoadRadius
        {
            get => _loadRadius;
            set
            {
                if (_loadRadius == value) return;
                _loadRadius = value;
                lastUpdateCPos = new CPos(int.MaxValue, int.MaxValue);
            }
        }
        public int LoadDelayMs { get; set; } 
        // 同時用來分幀「載入」跟「卸載」兩個方向，避免同一批要處理的 chunk 一次擠在同一幀
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

        // 【修正】UpdateFocusAsync 是 .Forget() 背景執行、沒有人持有它的 UniTask，
        // DestroyAndSaveAllAsync 之前完全不知道有沒有一個背景串流工作正在半路修改 activeChunks。
        // 如果玩家移動(或 Controller.Tick 的自我修復計時器)剛好在換區的瞬間也觸發了一次
        // UpdateFocusAsync，這個背景工作可能正卡在「卸載不需要的區塊→await 存檔」的迴圈中間，
        // 跟 DestroyAndSaveAllAsync 同時讀寫 activeChunks：DestroyAndSaveAllAsync 讀到的
        // activeChunks 快照可能已經被背景工作搶先移除了某個 chunk(該 chunk 因此不會被
        // DestroyAndSaveAllAsync 存到)，而背景工作自己那份「還在 await 存檔」的工作又完全不受
        // DestroyAndSaveAllAsync 的返回值保護——DestroyAndSaveAllAsync 一返回，呼叫端就接著做
        // 場景重載/清空所有實體，這個背景工作若還沒真正寫完檔案就可能讀到「实体已被清除」的
        // 半殘狀態，導致該 chunk 存檔漏掉正在裡面的實體(例如玩家自己)。
        // 用一個 completion signal 讓 DestroyAndSaveAllAsync 能夠等到「目前這一輪背景串流工作
        // 完全結束」之後才開始自己的存檔，兩邊就不會再交錯讀寫同一份 activeChunks。
        private UniTaskCompletionSource _focusIdleSignal = null;

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
            _focusIdleSignal = new UniTaskCompletionSource();

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
                _focusIdleSignal?.TrySetResult();
                _focusIdleSignal = null;
            }
        }

        /// <summary>
        /// 等待「目前這一輪背景區塊串流工作」(UpdateFocusAsync 的 .Forget() 背景執行)完全結束。
        /// 沒有背景工作在跑時立刻返回。DestroyAndSaveAllAsync 用這個方法確保自己開始存檔前，
        /// 不會有另一個背景工作同時在改 activeChunks，見 _focusIdleSignal 的說明。
        /// </summary>
        private async UniTask WaitForFocusIdleAsync()
        {
            if (_isUpdatingFocus && _focusIdleSignal != null)
            {
                await _focusIdleSignal.Task;
            }
        }

        private async UniTask DoUpdateFocusAsync(Pos3 focusPos)
        {
            if (_isDestroyed) return;

            CPos currentCPos = focusPos.ToCPos(ChunkSize);

            // 如果目標沒變，就不用重新跑
            if (currentCPos == lastUpdateCPos)
            {
                UnityEngine.Debug.Log($"[DIAG][World] focusPos={focusPos} -> currentCPos={currentCPos} 跟 lastUpdateCPos 相同，跳過。");
                return;
            }

            HashSet<CPos> neededChunks = new HashSet<CPos>();

            for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
            {
                for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
                {
                    neededChunks.Add(new CPos(currentCPos.x + dx, currentCPos.y + dy));
                }
            }

            UnityEngine.Debug.Log($"[DIAG][World] focusPos={focusPos} -> currentCPos={currentCPos} (原 lastUpdateCPos={lastUpdateCPos})，LoadRadius={LoadRadius}，neededChunks=[{string.Join(",", neededChunks)}]");

            // 1. 卸載不需要的區塊 (卸載不能被中斷，必須確保資料存檔落地)
            List<CPos> toUnload = new List<CPos>();
            foreach (var kvp in activeChunks)
            {
                if (!neededChunks.Contains(kvp.Key))
                {
                    toUnload.Add(kvp.Key);
                }
            }

            // 【平滑卸載】比照下面載入迴圈的 ChunkLoadIntervalMs 分幀寫法：從 activeChunks
            // 移除維持同步不變(避免有 entity 在卸載中途被加進一個已經決定要消失的 chunk)，
            // 但實際存檔(可能包含不小的 CPU 開銷)之間插入間隔，避免玩家一次跨越多個 chunk
            // 邊界時，好幾個 chunk 的存檔尖峰疊在同一批處理裡。
            bool hasUnloadedAny = false;
            foreach (var cPos in toUnload)
            {
                if (activeChunks.TryGetValue(cPos, out Chunk chunk))
                {
                    activeChunks.Remove(cPos);

                    if (hasUnloadedAny && ChunkLoadIntervalMs > 0)
                    {
                        await UniTask.Delay(ChunkLoadIntervalMs);
                    }

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
                    hasUnloadedAny = true;
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

            // 【修正】等目前這一輪背景區塊串流工作(若有)完全結束，才開始下面自己的存檔快照。
            // 見 _focusIdleSignal 的說明：沒有這一步的話，玩家換區前一刻若剛好也觸發了一次
            // UpdateFocusAsync(移動或 Controller.Tick 的自我修復計時器都會觸發)，這個背景工作
            // 跟這裡會同時讀寫 activeChunks，可能導致某個 chunk(甚至包含玩家自己所在的那個)
            // 兩邊都沒真正存到、或這裡返回後背景工作才姍姍來遲地存檔，那時場景可能已經開始
            // 重載、實體已被清空，寫進去的就是缺漏玩家的殘缺資料。
            await WaitForFocusIdleAsync();

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