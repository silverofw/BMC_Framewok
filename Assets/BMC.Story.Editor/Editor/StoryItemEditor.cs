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

            // Header Status
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
                EditorGUILayout.TextField("Video Path", _cachedNode.VideoPath);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Choices Count: {_cachedNode.Choices.Count}", EditorStyles.miniBoldLabel);

                for (int i = 0; i < _cachedNode.Choices.Count; i++)
                {
                    var choice = _cachedNode.Choices[i];
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField($"Option {i + 1}", EditorStyles.miniLabel);
                    EditorGUILayout.TextField("Text", choice.Text);
                    EditorGUILayout.TextField("Target ID", choice.TargetNodeId);
                    EditorGUILayout.EndVertical();
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