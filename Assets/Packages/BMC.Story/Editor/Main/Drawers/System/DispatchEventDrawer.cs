using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class DispatchEventDrawer : StoryActionDrawer
    {
        public override string MenuPath => "System/Dispatch Event (發送腳本事件)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.DispatchEvent;
        public override StoryEvent CreateNewEvent() => new StoryEvent { DispatchEvent = new DispatchEventAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            var action = evt.DispatchEvent;

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Event Type:", GUILayout.Width(80));
            action.EventType = EditorGUILayout.TextField(action.EventType);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Param:", GUILayout.Width(80));
            action.EventParam = EditorGUILayout.TextField(action.EventParam);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck()) changed = true;

            return changed;
        }
    }
}