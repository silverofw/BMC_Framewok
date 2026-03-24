using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Story.Editor
{
    public class PlayAvgDialogDrawer : StoryActionDrawer
    {
        public override string MenuPath => "UI/AVG Dialog (AVG文字對話)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlayAvgDialog;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlayAvgDialog = new PlayAvgDialogAction() };

        // 用來記錄每個 Frame 的摺疊狀態 (改為依據陣列的 index)
        private Dictionary<int, bool> _frameFoldStates = new Dictionary<int, bool>();

        // 記錄 On End Events 的摺疊狀態
        private Dictionary<int, bool> _onEndFoldStates = new Dictionary<int, bool>();

        private bool IsFrameExpanded(int index)
        {
            if (!_frameFoldStates.TryGetValue(index, out bool expanded))
            {
                expanded = true; // 預設展開
                _frameFoldStates[index] = expanded;
            }
            return expanded;
        }

        private void SetFrameExpanded(int index, bool expanded)
        {
            _frameFoldStates[index] = expanded;
        }

        private bool IsOnEndExpanded(int index)
        {
            if (!_onEndFoldStates.TryGetValue(index, out bool expanded))
            {
                expanded = false; // 預設為收合，縮減為一行
                _onEndFoldStates[index] = expanded;
            }
            return expanded;
        }

        private void SetOnEndExpanded(int index, bool expanded)
        {
            _onEndFoldStates[index] = expanded;
        }

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            var action = evt.PlayAvgDialog;

            EditorGUI.BeginChangeCheck();
            action.BackgroundImage = EditorGUILayout.TextField("Background Image", action.BackgroundImage);
            if (EditorGUI.EndChangeCheck()) changed = true;

            EditorGUILayout.Space(3);

            // 標題旁增加 Expand All / Collapse All 快速按鈕
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Dialog Frames ({action.Frames.Count})", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Expand All", EditorStyles.miniButtonLeft, GUILayout.Width(75)))
            {
                for (int i = 0; i < action.Frames.Count; i++) SetFrameExpanded(i, true);
            }
            if (GUILayout.Button("Collapse All", EditorStyles.miniButtonRight, GUILayout.Width(75)))
            {
                for (int i = 0; i < action.Frames.Count; i++) SetFrameExpanded(i, false);
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < action.Frames.Count; i++)
            {
                if (i > 0)
                {
                    EditorGUILayout.Space(8);
                    Rect rect = EditorGUILayout.GetControlRect(false, 2);
                    EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 1f));
                    EditorGUILayout.Space(8);
                }

                var frame = action.Frames[i];

                GUI.backgroundColor = (i % 2 == 0) ? new Color(0.85f, 0.92f, 1f) : new Color(0.78f, 0.85f, 0.95f);
                EditorGUILayout.BeginVertical("box");
                GUI.backgroundColor = Color.white;

                EditorGUILayout.BeginHorizontal();

                bool isExpanded = IsFrameExpanded(i);
                GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

                Rect foldoutRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(85));
                isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, $"Frame #{i + 1}", true, foldoutStyle);

                GUILayout.Space(2);
                GUILayout.Label("ID:", EditorStyles.boldLabel, GUILayout.Width(20));

                EditorGUI.BeginChangeCheck();
                frame.FrameId = EditorGUILayout.TextField(frame.FrameId, GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck()) changed = true;

                SetFrameExpanded(i, isExpanded);

                GUILayout.FlexibleSpace();

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

                if (isExpanded)
                {
                    EditorGUI.BeginChangeCheck();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    EditorGUILayout.BeginVertical();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Visual", GUILayout.Width(40));
                    frame.VisualType = (DialogFrame.Types.VisualType)EditorGUILayout.EnumPopup(frame.VisualType, GUILayout.Width(75));
                    GUILayout.Space(15);
                    GUILayout.Label("Char ID", GUILayout.Width(50));
                    frame.CharacterId = EditorGUILayout.IntField(frame.CharacterId, GUILayout.Width(45));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    if (frame.VisualType == DialogFrame.Types.VisualType.Sprite)
                    {
                        GUILayout.Label("Pos", GUILayout.Width(30));
                        frame.Position = (CharacterPosition)EditorGUILayout.EnumPopup(frame.Position, GUILayout.Width(70));
                        GUILayout.Space(10);
                        GUILayout.Label("Sprite(留空自動讀取)", GUILayout.Width(125));
                        frame.AssetName = EditorGUILayout.TextField(frame.AssetName, GUILayout.ExpandWidth(true));
                    }
                    else if (frame.VisualType == DialogFrame.Types.VisualType.Video)
                    {
                        GUILayout.Label("Video Path", GUILayout.Width(70));
                        frame.AssetName = EditorGUILayout.TextField(frame.AssetName, GUILayout.ExpandWidth(true));
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Dialog Text", GUILayout.Width(75));
                    GUIStyle multiLineStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    frame.Key = EditorGUILayout.TextArea(frame.Key, multiLineStyle);
                    EditorGUILayout.EndHorizontal();

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

                            GUI.backgroundColor = new Color(0.9f, 1f, 0.9f);
                            EditorGUILayout.BeginVertical("box");
                            GUI.backgroundColor = Color.white;

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label("Text", GUILayout.Width(35));
                            choice.Text = EditorGUILayout.TextArea(choice.Text, multiLineStyle);

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
                            choice.Type = (DialogChoice.Types.ChoiceType)EditorGUILayout.EnumPopup(choice.Type, GUILayout.Width(110));
                            EditorGUILayout.EndHorizontal();

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
                                GUI.backgroundColor = new Color(1f, 0.95f, 0.85f);
                                EditorGUILayout.BeginVertical("box");
                                GUI.backgroundColor = Color.white;

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

                            EditorGUILayout.Space(3);
                            GUI.backgroundColor = new Color(0.9f, 0.95f, 0.9f);
                            EditorGUILayout.BeginVertical("helpbox");
                            GUI.backgroundColor = Color.white;

                            if (window.DrawConditionList("Visible Conditions (達標才顯示選項，不設定即為常駐)", choice.VisibleConditions)) changed = true;
                            if (window.DrawConditionList("Lock Conditions (達標才可點擊，否則反灰)", choice.LockConditions)) changed = true;

                            if (choice.LockConditions.Count > 0)
                            {
                                EditorGUILayout.BeginHorizontal();
                                GUILayout.Label("Lock Msg:", GUILayout.Width(65));
                                choice.LockMessage = EditorGUILayout.TextField(choice.LockMessage);
                                EditorGUILayout.EndHorizontal();
                            }
                            EditorGUILayout.EndVertical();

                            EditorGUILayout.Space(5);
                            GUI.backgroundColor = new Color(0.95f, 0.9f, 1f);
                            EditorGUILayout.BeginVertical("helpbox");
                            GUI.backgroundColor = Color.white;

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
                    else if (frame.FrameType == DialogFrame.Types.FrameType.WithAffectionJump ||
                             frame.FrameType == DialogFrame.Types.FrameType.WithVariableJump) // 結合好感度與變數跳轉的UI
                    {
                        bool isVariableJump = (frame.FrameType == DialogFrame.Types.FrameType.WithVariableJump);
                        Color bgColor = isVariableJump ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.9f, 0.95f);

                        GUI.backgroundColor = bgColor;
                        EditorGUILayout.BeginVertical("box");
                        GUI.backgroundColor = Color.white;

                        if (isVariableJump)
                        {
                            if (window.DrawGenericRulesBlock(
                                "Variable Jump Rules (字串比對，相同即自動跳轉)",
                                frame.VariableJumpRules,
                                () => new DialogFrame.Types.VariableJumpRule
                                {
                                    Scope = VariableScope.SaveKey,
                                    ConditionKey = "MyKey",
                                    ConditionValue = "True",
                                    TargetFrameId = ""
                                },
                                (rule) => {
                                    bool ruleChanged = false;
                                    EditorGUI.BeginChangeCheck();

                                    // 新增的 VariableScope 選擇器
                                    rule.Scope = (VariableScope)EditorGUILayout.EnumPopup(rule.Scope, GUILayout.Width(80));

                                    GUILayout.Label("Key:", GUILayout.Width(30));
                                    rule.ConditionKey = EditorGUILayout.TextField(rule.ConditionKey, GUILayout.Width(80));

                                    GUILayout.Label("==", EditorStyles.boldLabel, GUILayout.Width(20));

                                    GUILayout.Label("Value:", GUILayout.Width(40));
                                    rule.ConditionValue = EditorGUILayout.TextField(rule.ConditionValue, GUILayout.Width(70));

                                    GUILayout.Label("-> Jump To:", GUILayout.Width(70));
                                    rule.TargetFrameId = EditorGUILayout.TextField(rule.TargetFrameId, GUILayout.ExpandWidth(true));
                                    if (EditorGUI.EndChangeCheck()) ruleChanged = true;

                                    return ruleChanged;
                                }))
                            {
                                changed = true;
                            }
                        }
                        else
                        {
                            if (window.DrawGenericRulesBlock(
                                "Affection Jump Rules (依序判定，符合即自動跳轉)",
                                frame.AffectionJumpRules,
                                () => new DialogFrame.Types.AffectionJumpRule
                                {
                                    CharacterId = 0,
                                    CompareType = Condition.Types.CompareType.GreaterEqual,
                                    TargetValue = 50,
                                    TargetFrameId = ""
                                },
                                (rule) => {
                                    bool ruleChanged = false;
                                    GUILayout.Label("Char ID:", GUILayout.Width(50));
                                    EditorGUI.BeginChangeCheck();
                                    rule.CharacterId = EditorGUILayout.IntField(rule.CharacterId, GUILayout.Width(35));
                                    rule.CompareType = (Condition.Types.CompareType)EditorGUILayout.EnumPopup(rule.CompareType, GUILayout.Width(100));
                                    rule.TargetValue = EditorGUILayout.IntField(rule.TargetValue, GUILayout.Width(40));

                                    GUILayout.Label("-> Jump To:", GUILayout.Width(70));
                                    rule.TargetFrameId = EditorGUILayout.TextField(rule.TargetFrameId, GUILayout.ExpandWidth(true));
                                    if (EditorGUI.EndChangeCheck()) ruleChanged = true;

                                    return ruleChanged;
                                }))
                            {
                                changed = true;
                            }
                        }

                        EditorGUILayout.Space(5);

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Fallback Target ID (皆不符合時):", GUILayout.Width(180));
                        EditorGUI.BeginChangeCheck();
                        frame.FallbackFrameId = EditorGUILayout.TextField(frame.FallbackFrameId, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) changed = true;
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.EndVertical();
                    }

                    if (EditorGUI.EndChangeCheck()) changed = true;

                    EditorGUILayout.Space(5);
                    GUI.backgroundColor = new Color(1f, 0.98f, 0.85f);
                    EditorGUILayout.BeginVertical("helpbox");
                    GUI.backgroundColor = Color.white;

                    bool onEndExpanded = IsOnEndExpanded(i);
                    Rect onEndFoldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    onEndExpanded = EditorGUI.Foldout(onEndFoldRect, onEndExpanded, $"On End Events (本句結束時) - 共 {frame.OnEndEvents.Count} 個事件", true, new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold });
                    SetOnEndExpanded(i, onEndExpanded);

                    if (onEndExpanded)
                    {
                        if (window.DrawEventList("", frame.OnEndEvents, node))
                        {
                            changed = true;
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }

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