using UnityEngine;
using UnityEditor;
using System.IO;

namespace BMC.Story.Editor
{
    [CustomEditor(typeof(StoryLineItem))]
    public class StoryItemEditor : UnityEditor.Editor
    {
        private StoryNode _cachedNode;
        private bool _isLoaded = false;

        private void OnEnable()
        {
            _isLoaded = false;
            LoadFromDisk();
        }

        public override void OnInspectorGUI()
        {
            StoryLineItem item = (StoryLineItem)target;

            GUIStyle headerStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            if (string.IsNullOrEmpty(item.NodeID))
            {
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label("NO NODE ID ASSIGNED", headerStyle, GUILayout.Height(30));
            }
            else
            {
                GUI.backgroundColor = new Color(0.4f, 1f, 0.6f);
                GUILayout.Label($"SELECTED NODE: {item.NodeID}", headerStyle, GUILayout.Height(30));
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space();

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("--- File Data Preview (Read Only) ---", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(StoryEditorContext.CurrentFilePath))
            {
                EditorGUILayout.HelpBox("未設定檔案路徑", MessageType.Warning);
                return;
            }

            if (!_isLoaded) LoadFromDisk();

            if (_cachedNode != null)
            {
                GUI.enabled = false;
                EditorGUILayout.TextField("File Node ID", _cachedNode.Id);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"OnEnter Events: {_cachedNode.OnEnterEvents.Count}", EditorStyles.miniBoldLabel);

                for (int i = 0; i < _cachedNode.OnEnterEvents.Count; i++)
                {
                    var evt = _cachedNode.OnEnterEvents[i];
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField($"Event {i + 1}: {evt.ActionCase}", EditorStyles.miniLabel);

                    if (evt.ActionCase == StoryEvent.ActionOneofCase.ShowChoices)
                    {
                        foreach (var c in evt.ShowChoices.Choices)
                        {
                            EditorGUILayout.LabelField($" -> {c.Text} (To: {c.TargetNodeId})");
                        }
                    }
                    EditorGUILayout.EndVertical();
                }

                if (!string.IsNullOrEmpty(_cachedNode.AutoJumpNodeId))
                {
                    EditorGUILayout.LabelField($"Auto Jump -> {_cachedNode.AutoJumpNodeId}");
                }
                GUI.enabled = true;
            }
            else
            {
                if (File.Exists(StoryEditorContext.CurrentFilePath))
                    EditorGUILayout.HelpBox($"檔案中找不到 ID '{item.NodeID}'。", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("尚未建立對應的章節檔案。", MessageType.Info);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Data")) LoadFromDisk();
        }

        private void LoadFromDisk()
        {
            StoryLineItem item = (StoryLineItem)target;
            if (string.IsNullOrEmpty(item.NodeID)) return;
            _cachedNode = StoryEditorContext.LoadNode(item.NodeID);
            _isLoaded = true;
        }
    }
}