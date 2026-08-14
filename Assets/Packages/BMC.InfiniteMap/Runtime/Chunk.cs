using System.Collections.Generic;

namespace InfiniteMap
{
    /// <summary>
    /// 代表記憶體中一個正在運作的區塊
    /// </summary>
    public class Chunk
    {
        public CPos Pos { get; private set; }

        // 這個區塊最後卸載的時間，用於時間追趕 (Catch-up)
        public long LastTime { get; set; }

        // 空間索引：快速查出某個座標上有哪些實體
        // 允許同一個格子有多個實體 (例如：地板、掉落物、玩家)
        private Dictionary<Pos3, List<long>> spatialIndex;

        // 實體總表 (簡化示範，實務上可替換為您的 Entity/Atom 類別)
        public HashSet<long> Entities { get; private set; }

        public Chunk(CPos pos)
        {
            Pos = pos;
            spatialIndex = new Dictionary<Pos3, List<long>>();
            Entities = new HashSet<long>();
        }

        public void AddEntity(long guid, Pos3 pos)
        {
            Entities.Add(guid);

            if (!spatialIndex.TryGetValue(pos, out var list))
            {
                list = new List<long>(2); // 預設小容量減少記憶體浪費
                spatialIndex[pos] = list;
            }
            list.Add(guid);
        }

        public void RemoveEntity(long guid, Pos3 pos)
        {
            Entities.Remove(guid);

            if (spatialIndex.TryGetValue(pos, out var list))
            {
                list.Remove(guid);
                if (list.Count == 0)
                {
                    spatialIndex.Remove(pos);
                }
            }
        }

        /// <summary>
        /// 取得某座標上的所有實體 (零 GC 配置)
        /// </summary>
        public void GetEntitiesAt(Pos3 pos, List<long> results)
        {
            if (spatialIndex.TryGetValue(pos, out var list))
            {
                results.AddRange(list);
            }
        }
    }
}