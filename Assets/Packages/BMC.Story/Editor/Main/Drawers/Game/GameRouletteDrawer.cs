using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class GameRouletteDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Game/Russian Roulette (俄羅斯輪盤)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.GameRussianRoulette;
        public override StoryEvent CreateNewEvent() => new StoryEvent { GameRussianRoulette = new GameRussianRouletteAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            evt.GameRussianRoulette.PlayerHp = EditorGUILayout.IntField("Player HP", evt.GameRussianRoulette.PlayerHp);
            evt.GameRussianRoulette.OpponentHp = EditorGUILayout.IntField("Enemy HP", evt.GameRussianRoulette.OpponentHp);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck()) changed = true;

            window.DrawTargetIdSelector("Win ->", () => evt.GameRussianRoulette.WinNodeId, (val) => evt.GameRussianRoulette.WinNodeId = val);
            window.DrawTargetIdSelector("Lose ->", () => evt.GameRussianRoulette.LoseNodeId, (val) => evt.GameRussianRoulette.LoseNodeId = val);

            return changed;
        }
    }
}