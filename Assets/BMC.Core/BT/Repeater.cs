using System;
using System.Collections;
using System.Collections.Generic;
namespace Core.BT
{

    public class Repeater : Node
    {
        public bool allowFailureRepeat;
        private int index;
        public Repeater(List<Node> children) : base(children) { }

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
                        index = 0;
                        children[index].OnEnter();
                        return NodeState.RUNNING;
                    }
                    else
                    {
                        children[index].OnEnter();
                        return NodeState.RUNNING;
                    }
                case NodeState.FAILURE:
                    if(!allowFailureRepeat)
                        return result;
                    children[index].OnExit();
                    index++;
                    if (index == children.Count)
                    {
                        index = 0;
                        children[index].OnEnter();
                        return NodeState.RUNNING;
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
