namespace Core.BT
{
    public class ForceResult : Node
    {
        public NodeState nodeState;
        public ForceResult(Node node) : base()
        {
            children.Clear();
            children.Add(node);
        }
        public override NodeState Evaluate(int scale)
        {
            var result = children[0].Evaluate(scale);
            switch (result)
            {
                case NodeState.SUCCESS:
                    return nodeState;
                case NodeState.FAILURE:
                    return nodeState;
                default: 
                    return result;
            }
        }
    }
}
