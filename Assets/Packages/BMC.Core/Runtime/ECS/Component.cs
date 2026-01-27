namespace BMC.Core
{
    public class Component
    {
        int Id;
        public int EntityId;
        public Component(int id, int entityId)
        {
            Id = id;
            EntityId = entityId;
        }

        public virtual void Init()
        {

        }

        public virtual void Depose()
        { 
            
        }

        public virtual void Tick(int tick)
        { 
            
        }
    }
}
