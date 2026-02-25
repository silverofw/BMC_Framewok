using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class ShowChoicesDrawer : StoryActionDrawer
    {
        public override string MenuPath => "UI/Show Choices (顯示選項分歧)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.ShowChoices;
        public override StoryEvent CreateNewEvent()
        {
            var act = new ShowChoicesAction();
            act.Choices.Add(new Choice { Text = "Option 1" });
            return new StoryEvent { ShowChoices = act };
        }

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            var action = evt.ShowChoices;
            EditorGUILayout.LabelField($"Options ({action.Choices.Count})", EditorStyles.miniBoldLabel);

            for (int i = 0; i < action.Choices.Count; i++)
            {
                var choice = action.Choices[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(25));

                EditorGUI.BeginChangeCheck();
                choice.Text = EditorGUILayout.TextField(choice.Text);
                if (EditorGUI.EndChangeCheck()) changed = true;

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    action.Choices.RemoveAt(i);
                    changed = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                window.DrawTargetIdSelector("Target", () => choice.TargetNodeId, (val) => choice.TargetNodeId = val, node);
                EditorGUILayout.Space(3);

                if (window.DrawConditionList("Visible Conditions (顯示條件)", choice.VisibleConditions)) changed = true;
                if (window.DrawConditionList("Lock Conditions (解鎖條件)", choice.LockConditions)) changed = true;

                if (choice.LockConditions.Count > 0)
                {
                    EditorGUI.BeginChangeCheck();
                    choice.LockMessage = EditorGUILayout.TextField("Lock Message (未解鎖提示)", choice.LockMessage);
                    if (EditorGUI.EndChangeCheck()) changed = true;
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Add Choice"))
            {
                string nextId = StoryEditorContext.GenerateUniqueNextID(window.CurrentPackage, node);
                action.Choices.Add(new Choice { Text = "New Option", TargetNodeId = nextId });
                changed = true;
            }

            return changed;
        }
    }
}