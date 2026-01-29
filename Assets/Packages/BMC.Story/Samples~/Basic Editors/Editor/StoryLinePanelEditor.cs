using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement; // 新增引用

namespace BMC.Story.Editor
{
    [CustomEditor(typeof(StoryLinePanel))]
    public class StoryLinePanelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

            // --- 新增：檢查是否在 Prefab Mode ---
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            bool isPrefabMode = prefabStage != null;

            if (isPrefabMode)
            {
                // 如果在 Prefab Mode，顯示開啟按鈕
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f); // 淡藍色
                if (GUILayout.Button("Open Story Line Editor Window", GUILayout.Height(35)))
                {
                    // [更名] 使用 StoryLineEditorWindow
                    StoryLineEditorWindow.ShowWindow((StoryLinePanel)target);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                // 如果不在 Prefab Mode，顯示警告與禁用的按鈕
                EditorGUILayout.HelpBox("Editor features are only available in Prefab Isolation Mode to prevent scene clutter.", MessageType.Warning);

                GUI.enabled = false;
                GUILayout.Button("Open (Enter Prefab Mode first)", GUILayout.Height(35));
                GUI.enabled = true;
            }
        }
    }
}