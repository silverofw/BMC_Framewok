using UnityEngine;
using UnityEditor;

namespace BMC.Story.Editor
{
    [CustomEditor(typeof(StoryLinePanel))]
    public class StoryLinePanelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // 繪製預設的 Inspector (顯示 public 變數等)
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);

            // --- 顯示開啟視窗的按鈕 ---
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f); // 淡藍色
            if (GUILayout.Button("Open Story Editor Window", GUILayout.Height(35)))
            {
                // 呼叫我們剛剛在 Window 中新增的方法，並把目前的 Panel 傳進去
                StoryEditorWindow.ShowWindow((StoryLinePanel)target);
            }
            GUI.backgroundColor = Color.white;
        }
    }
}