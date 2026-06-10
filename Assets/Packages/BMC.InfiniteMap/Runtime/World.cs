using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks; // 替換為 UniTask

namespace InfiniteMap
{
    /// <summary>
    /// 無邊際世界管理器 (完全獨立不依賴 MonoBehaviour 或 Singleton)
    /// 採用「單一序列佇列鎖」架構，徹底解決滑鼠移動過快時產生的實體重複生成與內存洩漏問題。
    /// </summary>
    public class World
    {
        public int ChunkSize { get; private set; }
        public int LoadRadius { get; set; } // 載入半徑 (1 = 3x3, 2 = 5x5)

        // 目前活耀(在記憶體中)的區塊
        private Dictionary<CPos, Chunk> activeChunks;
        private CPos lastUpdateCPos = new CPos(int.MaxValue, int.MaxValue);

        // 外部依賴介面 (現在使用 UniTask 徹底避免 GC)
        public Func<CPos, UniTask<Chunk>> OnLoadChunkAsync;
        public Func<Chunk, UniTask> OnSaveChunkAsync;

        // ============================================
        // 佇列與防重入狀態機 (取代不穩定的 CancellationToken)
        // ============================================
        private bool _isUpdatingFocus = false;
        private Pos3? _pendingFocusPos = null;

        public World(int chunkSize = 16, int loadRadius = 1)
        {
            ChunkSize = chunkSize;
            LoadRadius = loadRadius;
            activeChunks = new Dictionary<CPos, Chunk>();
        }

        /// <summary>
        /// 根據焦點(例如玩家/編輯器滑鼠位置)更新區塊 (非同步防重入排隊機制)
        /// </summary>
        public async UniTask UpdateFocusAsync(Pos3 focusPos)
        {
            // 記錄最新的焦點目標（若在加載中，此時滑鼠再次移動，會直接覆蓋此目標）
            _pendingFocusPos = focusPos;

            // 如果已經有加載協程在運行，我們直接排隊等待，不重入執行
            if (_isUpdatingFocus) return;

            _isUpdatingFocus = true;

            try
            {
                // 持續消耗佇列，直到最後一次移動的目標被完美處理完畢
                while (_pendingFocusPos.HasValue)
                {
                    Pos3 nextTarget = _pendingFocusPos.Value;
                    _pendingFocusPos = null; // 清空 pending 標記

                    await DoUpdateFocusAsync(nextTarget);
                }
            }
            finally
            {
                _isUpdatingFocus = false; // 確保一定會釋放鎖定
            }
        }

        /// <summary>
        /// 實際執行區塊加載與卸載的核心邏輯 (保證事務完整執行，絕不中途遺失登記)
        /// </summary>
        private async UniTask DoUpdateFocusAsync(Pos3 focusPos)
        {
            CPos currentCPos = focusPos.ToCPos(ChunkSize);

            // 如果焦點所在的 Chunk 沒變，不需重新計算
            if (currentCPos == lastUpdateCPos) return;
            lastUpdateCPos = currentCPos;

            HashSet<CPos> neededChunks = new HashSet<CPos>();

            // 計算出需要的區塊
            for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
            {
                for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
                {
                    neededChunks.Add(new CPos(currentCPos.x + dx, currentCPos.y + dy));
                }
            }

            // 1. 卸載不需要的區塊
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
                // 安全檢查：從 activeChunks 中移出，並觸發釋放 (OnEntityDestroy)
                if (activeChunks.TryGetValue(cPos, out Chunk chunk))
                {
                    if (OnSaveChunkAsync != null)
                    {
                        await OnSaveChunkAsync(chunk);
                    }
                    activeChunks.Remove(cPos);
                }
            }

            // 2. 載入新進入範圍的區塊
            foreach (var cPos in neededChunks)
            {
                if (!activeChunks.ContainsKey(cPos))
                {
                    Chunk newChunk = null;
                    if (OnLoadChunkAsync != null)
                    {
                        newChunk = await OnLoadChunkAsync(cPos);
                    }

                    // 若無存檔則生成新的空區塊
                    if (newChunk == null)
                    {
                        newChunk = new Chunk(cPos);
                    }

                    // 關鍵步驟：只要成功載入，就必須立刻加入 activeChunks，保證後續能被卸載釋放
                    activeChunks[cPos] = newChunk;
                }
            }
        }

        /// <summary>
        /// 跨區塊的範圍搜尋 (提供給外部的統一介面，零 GC)
        /// </summary>
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