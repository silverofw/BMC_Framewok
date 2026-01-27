namespace Core.BT
{
    public class Inverter : Node
    {
        public Inverter(Node node) : base() {
            children.Clear();
            children.Add(node);
        }
        public override NodeState Evaluate(int scale)
        {
            var result = children[0].Evaluate(scale);
            switch (result)
            {
                case NodeState.SUCCESS:
                    return NodeState.FAILURE;
                case NodeState.FAILURE:
                    return NodeState.SUCCESS;
                default: return result;

            }
        }
    }
}
