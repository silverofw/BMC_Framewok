namespace BMC.Core
{
    public class Entity
    {
        public int Id { get; }

        public Entity(int id)
        {
            Id = id;
        }

        public T GetComponent<T>() where T : Component
        {
            return null;
        }

        public T AddComponent<T>(T com) where T : Component
        {
            return ECSMgr.Instance.AddComponent<T>(com);
        }

        public void RemoveComponent<T>(T com) where T : Component
        {
            ECSMgr.Instance.RemoveComponent<T>(com);
        }
    }
}
