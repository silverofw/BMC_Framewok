using BMC.Core;
using System.Collections.Generic;

namespace BMC.Story
{
    public enum StoryEventID
    {
        None = 0,
        Play,
        Pause,
        Restart,
        FastEnd,
        PlayNode = 1000,
        NodeEventTrigger = 1001,
    }
    public class StoryPlayer : Singleton<StoryPlayer>
    {
        public StoryPackage _currentPackage { get; private set; }
        public StoryNode StartNode => _nodeMap.ContainsKey("Start") ? _nodeMap["Start"] : null;
        public StoryNode CrtNode { get; private set; }

        /// <summary>
        /// Only for StoryEventID
        /// </summary>
        public EventHandler handler = new EventHandler();
        private Dictionary<string, StoryNode> _nodeMap = new Dictionary<string, StoryNode>();
        private StoryNode _preNode;
        private StoryNode _startNode => _currentPackage.Nodes[0];

        public void LoadStory(byte[] bytes)
        {
            _nodeMap.Clear();
            _currentPackage = StoryPackage.Parser.ParseFrom(bytes);

            foreach (var node in _currentPackage.Nodes)
            {
                if (!_nodeMap.ContainsKey(node.Id)) _nodeMap.Add(node.Id, node);
            }
        }

        public void StartStory()
        {
            PlayNode(_startNode.Id);
        }

        public void Play()
        {
            handler.Send((int)StoryEventID.Play, CrtNode, _preNode);
        }

        public void Pause()
        {
            handler.Send((int)StoryEventID.Pause, CrtNode, _preNode);
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
                    if (item.WaitForTrigger)
                        continue;
                    handler.Send((int)StoryEventID.NodeEventTrigger, item, CrtNode, _preNode);
                }
            }
        }

        public bool IsCrtNode(StoryNode node)
        {
            return CrtNode != null && CrtNode.Id == node.Id;
        }
    }
}