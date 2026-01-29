using BMC.Core;
using System.Collections.Generic;

namespace BMC.Story
{
    public enum StoryEventID
    {
        PlayNode = 1000,
        NodeEventTrigger = 1001,
    }
    public class StoryPlayer : Singleton<StoryPlayer>
    {
        public StoryPackage _currentPackage { get; private set; }
        public StoryNode StartNode => _nodeMap.ContainsKey("Start") ? _nodeMap["Start"] : null;
        public StoryNode CrtNode { get; private set; }

        private EventHandler handler = new EventHandler();
        private Dictionary<string, StoryNode> _nodeMap = new Dictionary<string, StoryNode>();
        private StoryNode _preNode;

        public void LoadStory(byte[] bytes)
        {
            _nodeMap.Clear();
            handler = new EventHandler();
            _currentPackage = StoryPackage.Parser.ParseFrom(bytes);

            foreach (var node in _currentPackage.Nodes)
            {
                if (!_nodeMap.ContainsKey(node.Id)) _nodeMap.Add(node.Id, node);
            }
        }

        public void StartStory()
        {
            PlayNode("Start");
        }

        public void PlayNode(string nodeId)
        {
            if (!_nodeMap.ContainsKey(nodeId)) return;

            // 記錄前一個節點 ID (如果有的話)
            _preNode = (CrtNode != null) ? CrtNode : null;
            CrtNode = _nodeMap[nodeId];

            Log.Info($"[StoryPlayer][{CrtNode.Id}] AutoJumpNodeId: {CrtNode.AutoJumpNodeId}, AutoJumpDelay: {CrtNode.AutoJumpDelay}");
            handler.Send((int)StoryEventID.PlayNode, CrtNode, _preNode);
            if (CrtNode.OnEnterEvents != null)
            {
                foreach (var item in CrtNode.OnEnterEvents)
                {
                    handler.Send((int)StoryEventID.NodeEventTrigger, item, CrtNode, _preNode);
                }
            }
        }

        public void Register(System.Action<StoryNode, StoryNode> callback)
        {
            handler.Register((int)StoryEventID.PlayNode, callback);
        }
        public void UnRegister(System.Action<StoryNode, StoryNode> callback)
        {
            handler.UnRegister((int)StoryEventID.PlayNode, callback);
        }

        public void Register(System.Action<StoryEvent, StoryNode, StoryNode> callback)
        {
            handler.Register((int)StoryEventID.NodeEventTrigger, callback);
        }
        public void UnRegister(System.Action<StoryEvent, StoryNode, StoryNode> callback)
        {
            handler.UnRegister((int)StoryEventID.NodeEventTrigger, callback);
        }

        public bool IsCrtNode(StoryNode node)
        {
            return CrtNode != null && CrtNode == node;
        }
    }
}