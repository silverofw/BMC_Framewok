using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class UpdateStatDrawer : StoryActionDrawer
    {
        public override string MenuPath => "System/Update Stat (更新角色屬性)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.UpdateStat;
        public override StoryEvent CreateNewEvent() => new StoryEvent { UpdateStat = new UpdateCharacterStatAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.UpdateStat.CharacterId = EditorGUILayout.IntField("Character ID", evt.UpdateStat.CharacterId);
            evt.UpdateStat.StatType = (StatType)EditorGUILayout.EnumPopup("Stat Type", evt.UpdateStat.StatType);
            evt.UpdateStat.Value = EditorGUILayout.IntField("Add Value", evt.UpdateStat.Value);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}