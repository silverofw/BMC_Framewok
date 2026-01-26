using System.Collections.Generic;
namespace Core.BT
{
    public class Sequence : Node
    {
        private int index;

        public Sequence(List<Node> children) : base(children) { }

        public override void OnEnter()
        {
            base.OnEnter();
            index = 0;
            children[index].OnEnter();
        }

        public override NodeState Evaluate(int scale)
        {
            var result = children[index].Evaluate(scale);
            switch (result)
            {
                case NodeState.SUCCESS:
                    children[index].OnExit();
                    index++;
                    if (index == children.Count)
                    {
                        return NodeState.SUCCESS;
                    }
                    else
                    {
                        children[index].OnEnter();
                        return NodeState.RUNNING;
                    }
                default:
                    return result;
            }
        }
    }
}
