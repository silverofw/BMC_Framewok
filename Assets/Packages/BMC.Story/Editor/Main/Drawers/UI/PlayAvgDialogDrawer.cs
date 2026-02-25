using UnityEditor;
using UnityEngine;
using System.Linq;

namespace BMC.Story.Editor
{
    public class PlayAvgDialogDrawer : StoryActionDrawer
    {
        public override string MenuPath => "UI/AVG Dialog (AVG文字對話)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlayAvgDialog;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlayAvgDialog = new PlayAvgDialogAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            var action = evt.PlayAvgDialog;

            EditorGUI.BeginChangeCheck();
            action.BackgroundImage = EditorGUILayout.TextField("Background Image", action.BackgroundImage);
            if (EditorGUI.EndChangeCheck()) changed = true;

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField($"Dialog Frames ({action.Frames.Count})", EditorStyles.miniBoldLabel);

            for (int i = 0; i < action.Frames.Count; i++)
            {
                var frame = action.Frames[i];
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Frame #{i + 1}", EditorStyles.boldLabel, GUILayout.Width(75));

                if (i > 0 && GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(25)))
                {
                    (action.Frames[i], action.Frames[i - 1]) = (action.Frames[i - 1], action.Frames[i]);
                    changed = true;
                }
                if (i < action.Frames.Count - 1 && GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(25)))
                {
                    (action.Frames[i], action.Frames[i + 1]) = (action.Frames[i + 1], action.Frames[i]);
                    changed = true;
                }

                GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
                if (GUILayout.Button("C", EditorStyles.miniButtonMid, GUILayout.Width(25)))
                {
                    var newFrame = new DialogFrame
                    {
                        CharacterId = frame.CharacterId,
                        Position = frame.Position,
                        CharacterSpriteName = frame.CharacterSpriteName,
                        Key = frame.Key + "_NEXT"
                    };
                    action.Frames.Insert(i + 1, newFrame);
                    changed = true;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                // 這裡存取了主視窗定義的 ErrorColor 樣式
                GUI.backgroundColor = StoryLineEditorWindow.Styles.ErrorColor;
                if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(25)))
                {
                    action.Frames.RemoveAt(i);
                    changed = true;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.BeginHorizontal();

                // === 修正區域 ===
                // 將 Label 與 Field 拆開畫，避免 EditorGUIUtility.labelWidth 吃掉輸入框的空間
                GUILayout.Label("Char ID", GUILayout.Width(50));
                frame.CharacterId = EditorGUILayout.IntField(frame.CharacterId, GUILayout.Width(60));

                GUILayout.Space(10); // 加上一點間距

                GUILayout.Label("Pos", GUILayout.Width(30));
                frame.Position = (CharacterPosition)EditorGUILayout.EnumPopup(frame.Position, GUILayout.Width(80));
                // === 修正區域結束 ===

                EditorGUILayout.EndHorizontal();

                frame.CharacterSpriteName = EditorGUILayout.TextField("Sprite Name", frame.CharacterSpriteName);

                EditorGUILayout.LabelField("Dialog Text Key:");
                frame.Key = EditorGUILayout.TextArea(frame.Key, GUILayout.Height(45));

                if (EditorGUI.EndChangeCheck()) changed = true;

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Frame"))
            {
                string newKey = action.Frames.Count > 0 ? action.Frames.Last().Key + "_NEW" : "LOC_DIALOG_...";
                int lastCharId = action.Frames.Count > 0 ? action.Frames.Last().CharacterId : 0;
                var lastPos = action.Frames.Count > 0 ? action.Frames.Last().Position : CharacterPosition.Center;

                action.Frames.Add(new DialogFrame { Key = newKey, CharacterId = lastCharId, Position = lastPos });
                changed = true;
            }

            // 這裡存取了主視窗定義的 WarningColor 樣式
            GUI.backgroundColor = StoryLineEditorWindow.Styles.WarningColor;
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("Clear Frames", "Are you sure you want to clear all dialog frames?", "Yes", "Cancel"))
                {
                    action.Frames.Clear();
                    changed = true;
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            return changed;
        }
    }
}