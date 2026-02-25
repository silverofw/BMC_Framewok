using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class GameDiceDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Game/Dice Roll (擲骰檢定)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.GameDice;
        public override StoryEvent CreateNewEvent() => new StoryEvent { GameDice = new GameDiceRollAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            evt.GameDice.DiceCount = EditorGUILayout.IntField("Count", evt.GameDice.DiceCount);
            evt.GameDice.DiceFaces = EditorGUILayout.IntField("Faces", evt.GameDice.DiceFaces);
            EditorGUILayout.EndHorizontal();
            evt.GameDice.TargetValue = EditorGUILayout.IntField("Target >=", evt.GameDice.TargetValue);
            if (EditorGUI.EndChangeCheck()) changed = true;

            // 使用主視窗的公開 Helper 方法繪製節點連接器
            window.DrawTargetIdSelector("Success ->", () => evt.GameDice.SuccessNodeId, (val) => evt.GameDice.SuccessNodeId = val);
            window.DrawTargetIdSelector("Fail ->", () => evt.GameDice.FailNodeId, (val) => evt.GameDice.FailNodeId = val);

            return changed;
        }
    }
}