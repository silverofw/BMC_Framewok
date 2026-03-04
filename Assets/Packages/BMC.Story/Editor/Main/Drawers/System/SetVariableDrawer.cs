using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class SetVariableDrawer : StoryActionDrawer
    {
        public override string MenuPath => "System/Set Variable (設定全域變數)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.SetVariable;
        public override StoryEvent CreateNewEvent() => new StoryEvent { SetVariable = new SetVariableAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.SetVariable.VariableId = EditorGUILayout.TextField("Variable ID", evt.SetVariable.VariableId);
            evt.SetVariable.Value = EditorGUILayout.IntField("Value", evt.SetVariable.Value);
            evt.SetVariable.IsAdditive = EditorGUILayout.Toggle("Additive (+)", evt.SetVariable.IsAdditive);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}