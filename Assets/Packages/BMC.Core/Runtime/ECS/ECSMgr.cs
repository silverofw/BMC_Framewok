using System;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Core
{
    public class ECSMgr : Singleton<ECSMgr>
    {
        public int NextId => nextId;

        int nextId = 1;
        private Dictionary<int, Entity> entitys { get; set; }
        private Dictionary<Type, ECSSystem> systems;

        // 【核心優化】將原本的 List 改為 Dictionary，內層 Key 為 EntityId
        private Dictionary<Type, Dictionary<int, Component>> components;

        // guid 是上層(遊戲邏輯)賦予 Entity 的外部識別碼，框架本身不會自動產生或驗證，
        // 純粹是一個 guid->EntityId 的登記表，由上層在建立/刪除 Entity 時自行呼叫維護。
        private Dictionary<long, int> guidToId;

        public ECSMgr()
        {
            Clear();
        }

        public void Clear()
        {
            systems = new();
            if (components != null)
            {
                foreach (var dict in components.Values)
                {
                    foreach(var com in dict.Values)
                    {
                        com.Depose();
                    }
                    dict.Clear();
                }
            }
            entitys = new();
            components = new();
            guidToId = new();

            nextId = 1;
        }

        public void RegisterGuid(long guid, int entityId) => guidToId[guid] = entityId;

        public void UnregisterGuid(long guid) => guidToId.Remove(guid);

        public T GetByGuid<T>(long guid) where T : Entity
        {
            return guidToId.TryGetValue(guid, out var id) ? Get<T>(id) : null;
        }

        public void Tick(int scale)
        {
            foreach (var sys in systems.Values)
            {
                sys.Tick(scale);
            }
        }

        public void DeleteEntity(int id)
        {
            // 【效能大幅優化】不需要再對所有元件進行雙層 foreach 掃描了
            // 對於每種元件類型，只需做一次 O(1) 的 Dictionary.Remove
            foreach (var dict in components.Values) 
            {
                if (dict.TryGetValue(id, out var com))
                {
                    com.Depose();
                    dict.Remove(id);
                }
            }
            entitys.Remove(id);
        }

        public void AddSystem<T>(T sys) where T : ECSSystem
        {
            Type systemType = sys.GetType();

            if (systems.ContainsKey(systemType))
            {
                return;
            }
            systems[systemType] = sys;
        }

        public T CreateEntity<T>() where T : Entity
        {
            var entity = (T)Activator.CreateInstance(typeof(T), nextId);
            entitys.Add(nextId, entity);
            nextId++;
            return entity;
        }

        public T Get<T>(int id) where T : Entity
        {
            if (entitys.TryGetValue(id, out var entity))
            { 
                return entity as T;
            }
            return null;
        }

        public T AddComponent<T>(T com) where T : Component
        {
            if (!components.TryGetValue(com.GetType(), out var dict))
            {
                dict = new Dictionary<int, Component>();
                components.Add(com.GetType(), dict);
            }
            
            // 【優化】使用 EntityId 作為 Key，保證同一個 Entity 同種元件只有一個，並極大化加速查詢
            dict[com.EntityId] = com;
            com.Init();
            return com;
        }

        public void AddComponents(List<Component> coms)
        {
            foreach (var com in coms)
            {
                AddComponent(com);
            }
        }

        public void RemoveComponent<T>(T com) where T : Component
        {
            // 【效能優化】從 O(N) 的 List.Remove 變成了 O(1) 的 Dictionary 刪除
            if (components.TryGetValue(com.GetType(), out var dict))
            {
                if (dict.Remove(com.EntityId))
                {
                    com.Depose();
                }
            }
        }

        // 【變更注意】回傳型別從 List 改為 IEnumerable。
        // 如果這裡強迫轉回 List()，每幀都會產生大量 GC 記憶體垃圾 (Allocation)。
        // 外部 System 呼叫此方法時，建議直接用 foreach (var com in GetComponentList<T>()) 來遍歷即可。
        public IEnumerable<Component> GetComponentList<T>() where T : Component
        {
            if (components.TryGetValue(typeof(T), out var dict))
            {
                return dict.Values;
            }

            return Enumerable.Empty<Component>();
        }

        public T GetComponent<T>(int entityId) where T : Component
        {
            // 【效能大幅優化】原本是做 foreach 掃描整個 List (效能極差)
            // 現在改為 O(1) 的 Dictionary 雜湊取值
            if (components.TryGetValue(typeof(T), out var dict))
            {
                if (dict.TryGetValue(entityId, out var com))
                {
                    return com as T;
                }
            }
            return null;
        }
    }
}