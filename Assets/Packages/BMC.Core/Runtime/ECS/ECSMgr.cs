using System;
using System.Collections.Generic;

namespace BMC.Core
{
    public class ECSMgr : Singleton<ECSMgr>
    {
        public int NextId => nextId;

        int nextId = 1;
        private Dictionary<int, Entity> entitys { get; set; }
        private Dictionary<Type, ECSSystem> systems;
        private Dictionary<Type, List<Component>> components;
        public ECSMgr()
        {
            Clear();
        }

        public void Clear()
        {
            systems = new();
            if (components != null)
            {
                foreach (var list in components.Values)
                {
                    foreach(var com in list) 
                    {
                        com.Depose();
                    }
                    list.Clear();
                }
            }
            entitys = new();
            components = new();

            nextId = 1;
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
            //Core.eventHandler.Send((int)CoreEvent.LOG, $"[DeleteEntity] {id}");
            foreach (var list in components.Values) 
            {
                var deleteList = new List<Component>();
                foreach(var com in list) 
                {
                    if (com.EntityId == id)
                    {
                        deleteList.Add(com);
                    }
                }

                foreach (var com in deleteList)
                {
                    RemoveComponent(com);
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

        public T CreateEntity<T>()where T : Entity
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
            if (components.TryGetValue(com.GetType(), out var list))
            {
                list.Add(com);
            }
            else
            {
                components.Add(com.GetType(), new List<Component>() { com });
            }
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
            com.Depose();
            components[com.GetType()].Remove(com);
        }

        public List<Component> GetComponentList<T>()where T:Component
        {
            if (components.TryGetValue(typeof(T), out var list))
            {
                return list;
            }

            return new List<Component>();
        }

        public T GetComponent<T>(int entityId) where T : Component
        {
            var list = GetComponentList<T>();
            if (list == null)
                return null;
            foreach (var com in list)
            {
                if (com.EntityId == entityId)
                {
                    return com as T;
                }
            }
            return null;
        }
    }
}
