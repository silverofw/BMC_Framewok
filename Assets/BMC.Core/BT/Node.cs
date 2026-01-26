using System.Collections;
using System.Collections.Generic;
namespace Core.BT
{
    public enum NodeState { 
        RUNNING,
        SUCCESS,
        FAILURE
    }
    public class Node
    {
        public string name;

        protected NodeState state;
        public Node parent;
        protected List<Node> children = new List<Node>();

        public Node()
        {
            parent = null;
        }
        public Node(List<Node> children)
        {
            foreach (Node child in children)
                _Attach(child);
        }

        private void _Attach(Node node)
        {
            node.parent = this;
            children.Add(node);
        }

        public virtual void OnEnter() { }

        public virtual NodeState Evaluate(int scale) => NodeState.FAILURE;

        public virtual void OnExit() { }
    }
}
