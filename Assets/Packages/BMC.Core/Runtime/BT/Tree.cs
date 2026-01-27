namespace Core.BT
{
    public abstract class Tree 
    {
        protected Node _root = null;

        public void Init()
        {
            _root.OnEnter();
        }

        public NodeState Tick(int scale)
        {
            if (_root != null) 
            {
                return _root.Evaluate(scale);
            }
            return NodeState.FAILURE;
        }
    }
}
