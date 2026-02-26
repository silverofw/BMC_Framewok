using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class GamePuzzleDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Game/Puzzle (解謎遊戲)";

        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.GamePuzzle;

        public override StoryEvent CreateNewEvent()
        {
            return new StoryEvent
            {
                GamePuzzle = new GamePuzzleAction
                {
                    PrefabName = "",
                    SuccessNodeId = "",
                    FailNodeId = ""
                }
            };
        }

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            var action = evt.GamePuzzle;

            EditorGUI.BeginChangeCheck();

            // 繪製 Prefab 名稱輸入框
            action.PrefabName = EditorGUILayout.TextField("Prefab Name", action.PrefabName);

            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }


            // 使用 StoryLineEditorWindow 提供的 Helper 來繪製節點選擇器
            // 成功跳轉節點
            window.DrawTargetIdSelector(
                "Success Node",
                () => action.SuccessNodeId,
                (val) => { action.SuccessNodeId = val; },
                node
            );

            // 失敗跳轉節點 (可選)
            window.DrawTargetIdSelector(
                "Fail Node",
                () => action.FailNodeId,
                (val) => { action.FailNodeId = val; },
                node
            );

            EditorGUILayout.Space(4);

            // 加入 HelpBox 提示企劃：勝利與失敗節點為非必填
            EditorGUILayout.HelpBox("※ 勝利與失敗節點為非必填。\n若皆未填寫，解謎結束後將會自動執行此節點上方的 Auto Jump 邏輯。", MessageType.Info);

            EditorGUILayout.Space(2);
            return changed;
        }
    }
}