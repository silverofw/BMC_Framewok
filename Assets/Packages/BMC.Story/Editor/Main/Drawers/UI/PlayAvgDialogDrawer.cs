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

                // --- 標題與操作按鈕 ---
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
                    // 使用 Protobuf 內建的 Clone 進行深拷貝，並賦予新的獨立 ID 與 Key
                    var newFrame = frame.Clone();
                    newFrame.Key += "_NEXT";
                    newFrame.FrameId = System.Guid.NewGuid().ToString("N").Substring(0, 8);

                    action.Frames.Insert(i + 1, newFrame);
                    changed = true;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

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

                // --- 主要內容設定 ---
                EditorGUI.BeginChangeCheck();

                // 1. 識別與站位設定
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Frame ID", GUILayout.Width(60));
                frame.FrameId = EditorGUILayout.TextField(frame.FrameId, GUILayout.Width(80));
                GUILayout.Space(10);
                GUILayout.Label("Char ID", GUILayout.Width(50));
                frame.CharacterId = EditorGUILayout.IntField(frame.CharacterId, GUILayout.Width(60));
                GUILayout.Space(10);
                GUILayout.Label("Pos", GUILayout.Width(30));
                frame.Position = (CharacterPosition)EditorGUILayout.EnumPopup(frame.Position, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                frame.CharacterSpriteName = EditorGUILayout.TextField("Sprite Name", frame.CharacterSpriteName);

                EditorGUILayout.LabelField("Dialog Text Key:");
                frame.Key = EditorGUILayout.TextArea(frame.Key, GUILayout.Height(45));

                // 2. 文本類型與選項設定 (新增功能區)
                EditorGUILayout.Space(3);
                frame.FrameType = (DialogFrame.Types.FrameType)EditorGUILayout.EnumPopup("Frame Type", frame.FrameType);

                if (frame.FrameType == DialogFrame.Types.FrameType.WithChoices)
                {
                    EditorGUILayout.BeginVertical("helpbox");
                    EditorGUILayout.LabelField("Choices", EditorStyles.miniBoldLabel);
                    EditorGUI.indentLevel++;

                    for (int j = 0; j < frame.Choices.Count; j++)
                    {
                        var choice = frame.Choices[j];
                        EditorGUILayout.BeginVertical("box");

                        EditorGUILayout.BeginHorizontal();
                        choice.Text = EditorGUILayout.TextField("Text", choice.Text);

                        GUI.backgroundColor = StoryLineEditorWindow.Styles.ErrorColor;
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                        {
                            frame.Choices.RemoveAt(j);
                            changed = true;
                            GUI.backgroundColor = Color.white;
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.EndVertical();
                            break;
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Action", GUILayout.Width(45));
                        choice.Type = (DialogChoice.Types.ChoiceType)EditorGUILayout.EnumPopup(choice.Type, GUILayout.Width(150));

                        if (choice.Type == DialogChoice.Types.ChoiceType.JumpFrame)
                        {
                            GUILayout.Space(10);
                            GUILayout.Label("Target ID:", GUILayout.Width(60));
                            choice.TargetFrameId = EditorGUILayout.TextField(choice.TargetFrameId);
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.EndVertical();
                    }

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(15 * EditorGUI.indentLevel);
                    if (GUILayout.Button("+ Add Choice", EditorStyles.miniButton, GUILayout.Width(100)))
                    {
                        frame.Choices.Add(new DialogChoice
                        {
                            Text = "New Choice",
                            Type = DialogChoice.Types.ChoiceType.JumpFrame
                        });
                        changed = true;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                }
                else if (frame.FrameType == DialogFrame.Types.FrameType.WithJumpNode)
                {
                    EditorGUILayout.BeginVertical("helpbox");
                    frame.TargetNodeId = EditorGUILayout.TextField("Target Node ID", frame.TargetNodeId);
                    EditorGUILayout.EndVertical();
                }

                if (EditorGUI.EndChangeCheck()) changed = true;

                EditorGUILayout.EndVertical();
            }

            // --- 底部操作按鈕 ---
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Frame"))
            {
                string newKey = action.Frames.Count > 0 ? action.Frames.Last().Key + "_NEW" : "LOC_DIALOG_...";
                int lastCharId = action.Frames.Count > 0 ? action.Frames.Last().CharacterId : 0;
                var lastPos = action.Frames.Count > 0 ? action.Frames.Last().Position : CharacterPosition.Center;
                string newId = System.Guid.NewGuid().ToString("N").Substring(0, 8); // 自動生成新的 ID

                action.Frames.Add(new DialogFrame
                {
                    Key = newKey,
                    CharacterId = lastCharId,
                    Position = lastPos,
                    FrameId = newId,
                    FrameType = DialogFrame.Types.FrameType.Normal
                });
                changed = true;
            }

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