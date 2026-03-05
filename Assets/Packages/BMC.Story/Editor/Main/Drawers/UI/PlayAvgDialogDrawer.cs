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
                // --- 新增：在每個 Frame 之間加入明顯的分隔線 ---
                if (i > 0)
                {
                    EditorGUILayout.Space(8);
                    Rect rect = EditorGUILayout.GetControlRect(false, 2);
                    EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 1f)); // 畫一條深灰色的分隔線
                    EditorGUILayout.Space(8);
                }

                var frame = action.Frames[i];

                // 1. 區分對話單句 (Frame) 的背景色：奇偶數採用不同深淺的淡藍色
                GUI.backgroundColor = (i % 2 == 0) ? new Color(0.85f, 0.92f, 1f) : new Color(0.78f, 0.85f, 0.95f);
                EditorGUILayout.BeginVertical("box");
                GUI.backgroundColor = Color.white; // 恢復預設，避免內部欄位被染色

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

                // --- 新增：內部縮排，讓標題與內容有明顯的主從層次 ---
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15); // 左側縮進 15 pixel
                EditorGUILayout.BeginVertical();

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

                // 2. 文本類型與選項設定
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

                        // 2. 區分選項 (Choice) 的背景色：淡綠色
                        GUI.backgroundColor = new Color(0.9f, 1f, 0.9f);
                        EditorGUILayout.BeginVertical("box");
                        GUI.backgroundColor = Color.white; // 恢復預設

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

                        // 選擇 Type
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Action", GUILayout.Width(45));
                        choice.Type = (DialogChoice.Types.ChoiceType)EditorGUILayout.EnumPopup(choice.Type, GUILayout.Width(110));
                        EditorGUILayout.EndHorizontal();

                        // 根據 Type 展開細節
                        if (choice.Type == DialogChoice.Types.ChoiceType.JumpFrame)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(45);
                            GUILayout.Label("Target ID:", GUILayout.Width(65));
                            choice.TargetFrameId = EditorGUILayout.TextField(choice.TargetFrameId);
                            EditorGUILayout.EndHorizontal();
                        }
                        else if (choice.Type == DialogChoice.Types.ChoiceType.MaxVariableJump)
                        {
                            // 3. 區分變數判定規則的背景色：淡橘色
                            GUI.backgroundColor = new Color(1f, 0.95f, 0.85f);
                            EditorGUILayout.BeginVertical("box");
                            GUI.backgroundColor = Color.white; // 恢復預設

                            EditorGUILayout.LabelField("Variable Rules (Jump to highest)", EditorStyles.miniBoldLabel);

                            for (int k = 0; k < choice.VariableRules.Count; k++)
                            {
                                var rule = choice.VariableRules[k];
                                EditorGUILayout.BeginHorizontal();
                                rule.VariableId = EditorGUILayout.TextField(rule.VariableId, GUILayout.Width(100));
                                GUILayout.Label("->", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(20));
                                rule.TargetFrameId = EditorGUILayout.TextField(rule.TargetFrameId);

                                GUI.backgroundColor = StoryLineEditorWindow.Styles.ErrorColor;
                                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                                {
                                    choice.VariableRules.RemoveAt(k);
                                    changed = true;
                                    GUI.backgroundColor = Color.white;
                                    EditorGUILayout.EndHorizontal();
                                    break;
                                }
                                GUI.backgroundColor = Color.white;
                                EditorGUILayout.EndHorizontal();
                            }

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("+ Add Rule", EditorStyles.miniButton, GUILayout.Width(80)))
                            {
                                choice.VariableRules.Add(new DialogChoice.Types.VariableJumpRule { VariableId = "Var_XXX", TargetFrameId = "" });
                                changed = true;
                            }
                            EditorGUILayout.EndHorizontal();

                            EditorGUILayout.EndVertical();
                        }

                        // 4. 區分選項附加事件列表的背景色：淡紫色
                        EditorGUILayout.Space(5);
                        GUI.backgroundColor = new Color(0.95f, 0.9f, 1f);
                        EditorGUILayout.BeginVertical("helpbox");
                        GUI.backgroundColor = Color.white; // 恢復預設

                        if (window.DrawEventList("On Select Events (選擇此項時觸發)", choice.OnSelectEvents, node))
                        {
                            changed = true;
                        }
                        EditorGUILayout.EndVertical();

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
                    window.DrawTargetIdSelector("Target Node ID", () => frame.TargetNodeId, (val) => frame.TargetNodeId = val, node);
                    EditorGUILayout.EndVertical();
                }

                if (EditorGUI.EndChangeCheck()) changed = true;

                // --- 結束內部縮排 ---
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            // --- 底部操作按鈕 ---
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Frame"))
            {
                string newKey = action.Frames.Count > 0 ? action.Frames.Last().Key + "_NEW" : "LOC_DIALOG_...";
                int lastCharId = action.Frames.Count > 0 ? action.Frames.Last().CharacterId : 0;
                var lastPos = action.Frames.Count > 0 ? action.Frames.Last().Position : CharacterPosition.Center;
                string newId = System.Guid.NewGuid().ToString("N").Substring(0, 8);

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