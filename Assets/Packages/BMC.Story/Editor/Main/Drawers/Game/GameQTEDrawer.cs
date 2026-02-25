using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class GameQTEDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Game/QTE (快速反應事件)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.GameQte;
        public override StoryEvent CreateNewEvent() => new StoryEvent { GameQte = new GameQTEAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.GameQte.Type = (GameQTEAction.Types.QTEType)EditorGUILayout.EnumPopup("Type", evt.GameQte.Type);
            evt.GameQte.DurationSeconds = EditorGUILayout.FloatField("Time Limit", evt.GameQte.DurationSeconds);
            if (EditorGUI.EndChangeCheck()) changed = true;

            window.DrawTargetIdSelector("Success ->", () => evt.GameQte.SuccessNodeId, (val) => evt.GameQte.SuccessNodeId = val);
            window.DrawTargetIdSelector("Fail ->", () => evt.GameQte.FailNodeId, (val) => evt.GameQte.FailNodeId = val);

            return changed;
        }
    }
}