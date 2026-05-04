using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks; // 替換為 UniTask

namespace InfiniteMap
{
    /// <summary>
    /// 無邊際世界管理器 (完全獨立不依賴 MonoBehaviour 或 Singleton)
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

        public World(int chunkSize = 16, int loadRadius = 1)
        {
            ChunkSize = chunkSize;
            LoadRadius = loadRadius;
            activeChunks = new Dictionary<CPos, Chunk>();
        }

        /// <summary>
        /// 根據焦點(例如玩家位置)更新區塊
        /// </summary>
        public async UniTask UpdateFocusAsync(Pos3 focusPos)
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
                Chunk chunk = activeChunks[cPos];
                if (OnSaveChunkAsync != null)
                {
                    await OnSaveChunkAsync(chunk);
                }
                activeChunks.Remove(cPos);
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
                        // 這裡可以呼叫地形生成器
                    }

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
                    // 若有涵蓋高度的需求，可在此加一層 Z 軸迴圈
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