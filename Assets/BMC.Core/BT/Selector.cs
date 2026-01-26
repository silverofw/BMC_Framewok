using System;
using System.Collections;
using System.Collections.Generic;
namespace Core.BT { 
    public class Selector : Node
    {
        public bool isRand;
        public Random rand;

        private int index;
        public Selector(List<Node> children) : base(children) { }
        public override void OnEnter()
        {
            base.OnEnter();
            if (isRand)
            {
                ShuffleList(children);
            }

            index = 0;
            children[index].OnEnter();
        }
        public override NodeState Evaluate(int scale)
        {
            var result = children[index].Evaluate(scale);
            switch (result)
            {
                case NodeState.FAILURE:
                    children[index].OnExit();
                    index++;
                    if (index == children.Count)
                    {
                        return NodeState.FAILURE;
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

        protected void ShuffleList<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rand.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
