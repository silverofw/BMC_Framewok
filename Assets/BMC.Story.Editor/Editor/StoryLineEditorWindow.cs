using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Story.Editor
{
    public class StoryLineEditorWindow : EditorWindow
    {
        private int _chapterId = 1;
        private StoryLinePanel _targetPanel;
        private StoryPackage _currentPackage;
        private string _selectedNodeId;
        private Vector2 _nodeListScrollPos;
        private Vector2 _nodeDetailScrollPos;
        private string _searchFilter = "";

        private static class Styles
        {
            public static GUIStyle SceneLabel;
            public static Color SelectionColor = Color.cyan;
            public static Color WarningColor = new Color(1f, 0.8f, 0.6f);
            public static Color ErrorColor = new Color(1f, 0.4f, 0.4f);
            public static Color SuccessColor = Color.green;
            public static Color BackupColor = new Color(0.6f, 0.8f, 1f);
            public static Color EventHeaderColor = new Color(0.25f, 0.25f, 0.25f);

            static Styles()
            {
                SceneLabel = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.yellow }, fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            }
        }

        public static void ShowWindow(StoryLinePanel target)
        {
            var window = GetWindow<StoryLineEditorWindow>("Story Editor");
            window._targetPanel = target;
            window.Show();
        }

        private void OnEnable()
        {
            _chapterId = StoryEditorContext.CurrentChapterId;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        private void OnDestroy()
        {
            StoryEditorContext.ClearAllBackups(StoryEditorContext.CurrentFilePath);
            if (_targetPanel != null)
            {
                _targetPanel.ClearOldLayout();
                EditorUtility.SetDirty(_targetPanel.gameObject);
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_currentPackage == null || _targetPanel == null) return;

            var items = _targetPanel.GetComponentsInChildren<StoryLineItem>();
            var nodeMap = new Dictionary<string, Transform>();
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.NodeID) && !nodeMap.ContainsKey(item.NodeID))
                {
                    nodeMap.Add(item.NodeID, item.transform);
                }
            }

            foreach (var node in _currentPackage.Nodes)
            {
                if (!nodeMap.ContainsKey(node.Id)) continue;
                Vector3 startPos = nodeMap[node.Id].position;

                // 根據是否有跳轉邏輯來決定顏色
                var targets = StoryEditorContext.GetTargetNodeIds(node).ToList();
                Handles.color = (node.Id == "Start") ? Color.green : (targets.Count == 0 ? Color.red : Color.white);
                if (node.Id == _selectedNodeId) Handles.color = Color.cyan;

                Handles.DrawWireDisc(startPos, Vector3.up, 0.5f);

                if (node.Id == _selectedNodeId)
                {
                    Handles.Label(startPos + Vector3.up * 1.0f, $"<color=cyan>Editing:</color> {node.Id}", Styles.SceneLabel);
                }

                foreach (var targetId in targets)
                {
                    if (!string.IsNullOrEmpty(targetId) && nodeMap.ContainsKey(targetId))
                    {
                        Vector3 endPos = nodeMap[targetId].position;
                        if (node.Id == _selectedNodeId)
                        {
                            Handles.color = Color.cyan;
                            Handles.DrawBezier(startPos, endPos, startPos + Vector3.up * 2, endPos + Vector3.up * 2, Color.cyan, null, 3f);
                        }
                        else
                        {
                            Handles.color = new Color(1, 1, 1, 0.3f);
                            Handles.DrawLine(startPos, endPos);
                        }
                    }
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Story System Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawTargetPanelBinding();
            if (_targetPanel == null) return;

            DrawHorizontalLine();
            DrawChapterSettings();
            DrawFileOperations();

            EditorGUILayout.Space();
            DrawHorizontalLine();

            if (_currentPackage != null)
            {
                DrawEditorInterface();
            }
            else
            {
                string path = StoryEditorContext.CurrentFilePath;
                if (File.Exists(path)) EditorGUILayout.HelpBox("File exists. Click 'Load & Edit' to start.", MessageType.Info);
                else EditorGUILayout.LabelField("Waiting for file creation...", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawTargetPanelBinding()
        {
            EditorGUI.BeginChangeCheck();
            _targetPanel = (StoryLinePanel)EditorGUILayout.ObjectField("Target Panel", _targetPanel, typeof(StoryLinePanel), true);
            if (EditorGUI.EndChangeCheck()) { }
            if (_targetPanel == null) EditorGUILayout.HelpBox("Please assign StoryLinePanel first.", MessageType.Info);
        }

        private void DrawChapterSettings()
        {
            EditorGUILayout.LabelField("Chapter Settings", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _chapterId = EditorGUILayout.IntField("Chapter ID", _chapterId);
            if (_chapterId < 1) _chapterId = 1;
            if (EditorGUI.EndChangeCheck())
            {
                StoryEditorContext.CurrentChapterId = _chapterId;
                _currentPackage = null;
                _selectedNodeId = null;
                SceneView.RepaintAll();
            }
            EditorGUILayout.SelectableLabel(StoryEditorContext.CurrentFilePath, EditorStyles.textField, GUILayout.Height(18));
        }

        private void DrawFileOperations()
        {
            bool fileExists = File.Exists(StoryEditorContext.CurrentFilePath);
            EditorGUILayout.BeginHorizontal();
            if (fileExists)
            {
                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button("Load & Edit", GUILayout.Height(30))) LoadAndGenerate();

                GUI.backgroundColor = Styles.BackupColor;
                if (GUILayout.Button("History", GUILayout.Height(30), GUILayout.Width(70))) ShowBackupMenu();
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Styles.WarningColor;
                if (GUILayout.Button("Create & Load", GUILayout.Height(30))) CreateAndLoad();
                GUI.backgroundColor = Color.white;
            }

            if (GUILayout.Button("Clear Scene", GUILayout.Height(30))) _targetPanel.ClearOldLayout();
            EditorGUILayout.EndHorizontal();
        }

        private void ShowBackupMenu()
        {
            string path = StoryEditorContext.CurrentFilePath;
            var backups = StoryEditorContext.GetAvailableBackups(path);
            GenericMenu menu = new GenericMenu();

            if (backups.Count == 0) menu.AddDisabledItem(new GUIContent("No backups found"));
            else
            {
                foreach (var backup in backups)
                {
                    string fileName = Path.GetFileName(backup);
                    menu.AddItem(new GUIContent($"Restore: {fileName}"), false, () => {
                        if (StoryEditorContext.RestoreBackup(backup, path)) LoadAndGenerate();
                    });
                }
            }
            menu.ShowAsContext();
        }

        private void DrawEditorInterface()
        {
            EditorGUILayout.LabelField($"Node Editor (Nodes: {_currentPackage.Nodes.Count})", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            DrawNodeListSidebar();
            DrawNodeDetailArea();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNodeListSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220));

            // Search Bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // Create Button
            if (GUILayout.Button("+ Create Node", GUILayout.Height(25)))
            {
                var newNode = StoryEditorContext.CreateNewNode(_currentPackage);
                _selectedNodeId = newNode.Id;
                SaveToDiskAndRefresh();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(5);
            _nodeListScrollPos = EditorGUILayout.BeginScrollView(_nodeListScrollPos, "box");

            foreach (var node in _currentPackage.Nodes)
            {
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (node.Id.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                if (_selectedNodeId == node.Id) GUI.backgroundColor = Styles.SelectionColor;
                if (GUILayout.Button(node.Id, EditorStyles.miniButton, GUILayout.Height(20)))
                {
                    _selectedNodeId = node.Id;
                    GUI.FocusControl(null);
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            GUI.backgroundColor = Styles.WarningColor;
            if (GUILayout.Button("Check Orphans", GUILayout.Height(25))) CheckAndCleanOrphans();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }

        private void DrawNodeDetailArea()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            _nodeDetailScrollPos = EditorGUILayout.BeginScrollView(_nodeDetailScrollPos);

            if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                var selectedNode = _currentPackage.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
                if (selectedNode != null)
                {
                    DrawNodeDetails(selectedNode);
                }
                else
                {
                    EditorGUILayout.LabelField("Selected node not found.");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a node to edit.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawNodeDetails(StoryNode node)
        {
            bool isDirty = false;

            // 1. Header
            DrawNodeHeader(node, ref isDirty);
            if (_selectedNodeId == null) return; // Node deleted

            // 2. Auto Jump
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Auto Jump", EditorStyles.boldLabel);
            node.AutoJumpDelay = EditorGUILayout.FloatField("Delay (sec)", node.AutoJumpDelay);
            string oldAuto = node.AutoJumpNodeId;
            string newAuto = EditorGUILayout.TextField("Target Node ID", node.AutoJumpNodeId);
            if (oldAuto != newAuto)
            {
                node.AutoJumpNodeId = newAuto;
                isDirty = true;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("On Enter Events", EditorStyles.boldLabel);

            // 3. Events List
            for (int i = 0; i < node.OnEnterEvents.Count; i++)
            {
                var evt = node.OnEnterEvents[i];
                EditorGUILayout.BeginVertical("helpbox");

                // Event Header
                GUI.backgroundColor = Styles.EventHeaderColor;
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label($"#{i + 1} {evt.ActionCase}", EditorStyles.whiteLabel);

                // Move Up/Down
                if (i > 0 && GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    var temp = node.OnEnterEvents[i];
                    node.OnEnterEvents[i] = node.OnEnterEvents[i - 1];
                    node.OnEnterEvents[i - 1] = temp;
                    isDirty = true;
                }
                if (i < node.OnEnterEvents.Count - 1 && GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    var temp = node.OnEnterEvents[i];
                    node.OnEnterEvents[i] = node.OnEnterEvents[i + 1];
                    node.OnEnterEvents[i + 1] = temp;
                    isDirty = true;
                }

                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    node.OnEnterEvents.RemoveAt(i);
                    isDirty = true;
                    // Break to avoid index error, will repaint next frame
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                // Common Properties
                EditorGUI.BeginChangeCheck();
                evt.DelaySeconds = EditorGUILayout.FloatField("Delay", evt.DelaySeconds);
                if (EditorGUI.EndChangeCheck()) isDirty = true;

                // Specific Logic
                bool eventContentChanged = DrawEventContent(node, evt);
                if (eventContentChanged) isDirty = true;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            // 4. Add Event Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Video")) { node.OnEnterEvents.Add(new StoryEvent { PlayVideo = new PlayVideoAction() }); isDirty = true; }
            if (GUILayout.Button("+ BGM")) { node.OnEnterEvents.Add(new StoryEvent { PlayBgm = new PlayBackgroundMusicAction() }); isDirty = true; }
            if (GUILayout.Button("+ SFX")) { node.OnEnterEvents.Add(new StoryEvent { PlaySfx = new PlaySoundEffectAction() }); isDirty = true; }
            if (GUILayout.Button("+ Voice")) { node.OnEnterEvents.Add(new StoryEvent { PlayVoice = new PlayVoiceAction() }); isDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Stat")) { node.OnEnterEvents.Add(new StoryEvent { UpdateStat = new UpdateCharacterStatAction() }); isDirty = true; }
            if (GUILayout.Button("+ Var")) { node.OnEnterEvents.Add(new StoryEvent { SetVariable = new SetVariableAction() }); isDirty = true; }
            if (GUILayout.Button("+ Choices"))
            {
                var act = new ShowChoicesAction();
                act.Choices.Add(new Choice { Text = "Option 1" });
                node.OnEnterEvents.Add(new StoryEvent { ShowChoices = act });
                isDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Dice")) { node.OnEnterEvents.Add(new StoryEvent { GameDice = new GameDiceRollAction() }); isDirty = true; }
            if (GUILayout.Button("+ Roulette")) { node.OnEnterEvents.Add(new StoryEvent { GameRussianRoulette = new GameRussianRouletteAction() }); isDirty = true; }
            if (GUILayout.Button("+ QTE")) { node.OnEnterEvents.Add(new StoryEvent { GameQte = new GameQTEAction() }); isDirty = true; }
            EditorGUILayout.EndHorizontal();

            if (isDirty) SaveToDiskAndRefresh();
        }

        private bool DrawEventContent(StoryNode parentNode, StoryEvent evt)
        {
            // 特殊處理 ShowChoices，因為其內部邏輯較複雜，且已有自己的修改偵測
            if (evt.ActionCase == StoryEvent.ActionOneofCase.ShowChoices)
            {
                return DrawChoicesEditor(parentNode, evt.ShowChoices);
            }

            bool changed = false;

            // 包裹 ChangeCheck 以偵測數值欄位的變更
            EditorGUI.BeginChangeCheck();

            switch (evt.ActionCase)
            {
                case StoryEvent.ActionOneofCase.PlayVideo:
                    evt.PlayVideo.VideoPath = EditorGUILayout.TextField("Video Path", evt.PlayVideo.VideoPath);
                    evt.PlayVideo.Volume = EditorGUILayout.Slider("Volume", evt.PlayVideo.Volume, 0, 1);
                    evt.PlayVideo.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlayVideo.IsLoop);
                    break;

                case StoryEvent.ActionOneofCase.PlayBgm:
                    evt.PlayBgm.AudioPath = EditorGUILayout.TextField("BGM Path", evt.PlayBgm.AudioPath);
                    evt.PlayBgm.Volume = EditorGUILayout.Slider("Volume", evt.PlayBgm.Volume, 0, 1);
                    evt.PlayBgm.FadeInDuration = EditorGUILayout.FloatField("Fade In (sec)", evt.PlayBgm.FadeInDuration);
                    evt.PlayBgm.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlayBgm.IsLoop);
                    break;

                case StoryEvent.ActionOneofCase.PlaySfx:
                    evt.PlaySfx.AudioPath = EditorGUILayout.TextField("SFX Path", evt.PlaySfx.AudioPath);
                    evt.PlaySfx.Volume = EditorGUILayout.Slider("Volume", evt.PlaySfx.Volume, 0, 1);
                    evt.PlaySfx.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlaySfx.IsLoop);
                    break;

                case StoryEvent.ActionOneofCase.PlayVoice:
                    evt.PlayVoice.AudioPath = EditorGUILayout.TextField("Voice Path", evt.PlayVoice.AudioPath);
                    evt.PlayVoice.Volume = EditorGUILayout.Slider("Volume", evt.PlayVoice.Volume, 0, 1);
                    break;

                case StoryEvent.ActionOneofCase.UpdateStat:
                    evt.UpdateStat.CharacterId = EditorGUILayout.TextField("Character ID", evt.UpdateStat.CharacterId);
                    evt.UpdateStat.StatType = (UpdateCharacterStatAction.Types.StatType)EditorGUILayout.EnumPopup("Stat Type", evt.UpdateStat.StatType);
                    evt.UpdateStat.Value = EditorGUILayout.IntField("Add Value", evt.UpdateStat.Value);
                    break;

                case StoryEvent.ActionOneofCase.SetVariable:
                    evt.SetVariable.VariableId = EditorGUILayout.TextField("Variable ID", evt.SetVariable.VariableId);
                    evt.SetVariable.Value = EditorGUILayout.IntField("Value", evt.SetVariable.Value);
                    evt.SetVariable.IsAdditive = EditorGUILayout.Toggle("Additive (+)", evt.SetVariable.IsAdditive);
                    break;

                case StoryEvent.ActionOneofCase.GameDice:
                    evt.GameDice.DiceCount = EditorGUILayout.IntField("Dice Count", evt.GameDice.DiceCount);
                    evt.GameDice.DiceFaces = EditorGUILayout.IntField("Faces", evt.GameDice.DiceFaces);
                    evt.GameDice.TargetValue = EditorGUILayout.IntField("Target Value", evt.GameDice.TargetValue);
                    DrawTargetIdSelector("Success Node", () => evt.GameDice.SuccessNodeId, (val) => evt.GameDice.SuccessNodeId = val);
                    DrawTargetIdSelector("Fail Node", () => evt.GameDice.FailNodeId, (val) => evt.GameDice.FailNodeId = val);
                    break;

                case StoryEvent.ActionOneofCase.GameRussianRoulette:
                    evt.GameRussianRoulette.PlayerHp = EditorGUILayout.IntField("Player HP", evt.GameRussianRoulette.PlayerHp);
                    evt.GameRussianRoulette.OpponentHp = EditorGUILayout.IntField("Opponent HP", evt.GameRussianRoulette.OpponentHp);
                    DrawTargetIdSelector("Win Node", () => evt.GameRussianRoulette.WinNodeId, (val) => evt.GameRussianRoulette.WinNodeId = val);
                    DrawTargetIdSelector("Lose Node", () => evt.GameRussianRoulette.LoseNodeId, (val) => evt.GameRussianRoulette.LoseNodeId = val);
                    break;

                case StoryEvent.ActionOneofCase.GameQte:
                    evt.GameQte.Type = (GameQTEAction.Types.QTEType)EditorGUILayout.EnumPopup("Type", evt.GameQte.Type);
                    evt.GameQte.DurationSeconds = EditorGUILayout.FloatField("Duration", evt.GameQte.DurationSeconds);
                    DrawTargetIdSelector("Success Node", () => evt.GameQte.SuccessNodeId, (val) => evt.GameQte.SuccessNodeId = val);
                    DrawTargetIdSelector("Fail Node", () => evt.GameQte.FailNodeId, (val) => evt.GameQte.FailNodeId = val);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }

            return changed;
        }

        private bool DrawChoicesEditor(StoryNode parentNode, ShowChoicesAction action)
        {
            bool changed = false;
            EditorGUILayout.LabelField($"Options Count: {action.Choices.Count}", EditorStyles.miniBoldLabel);

            for (int i = 0; i < action.Choices.Count; i++)
            {
                var choice = action.Choices[i];
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Opt {i + 1}", GUILayout.Width(40));

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

                DrawTargetIdSelector("Target", () => choice.TargetNodeId, (val) => choice.TargetNodeId = val, parentNode);

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Choice"))
            {
                string nextId = StoryEditorContext.GenerateUniqueNextID(_currentPackage, parentNode);
                action.Choices.Add(new Choice { Text = "New Option", TargetNodeId = nextId });
                changed = true;
            }

            return changed;
        }

        private void DrawTargetIdSelector(string label, System.Func<string> getter, System.Action<string> setter, StoryNode parentForGen = null)
        {
            EditorGUILayout.BeginHorizontal();
            string current = getter();
            string newVal = EditorGUILayout.DelayedTextField(label, current);

            if (newVal != current)
            {
                setter(newVal);
            }

            // Check if valid
            if (!string.IsNullOrEmpty(newVal) && _currentPackage.Nodes.Any(n => n.Id == newVal))
            {
                if (GUILayout.Button("Go", GUILayout.Width(30)))
                {
                    _selectedNodeId = newVal;
                    GUI.FocusControl(null);
                }
            }
            else if (!string.IsNullOrEmpty(newVal))
            {
                GUI.backgroundColor = Styles.SelectionColor;
                if (GUILayout.Button("New", GUILayout.Width(40)))
                {
                    StoryEditorContext.CreateSpecificNode(_currentPackage, newVal);
                    SaveToDiskAndRefresh();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (parentForGen != null && string.IsNullOrEmpty(newVal))
            {
                if (GUILayout.Button("Gen", GUILayout.Width(40)))
                {
                    string nextId = StoryEditorContext.GenerateUniqueNextID(_currentPackage, parentForGen);
                    setter(nextId);
                    StoryEditorContext.CreateSpecificNode(_currentPackage, nextId);
                    SaveToDiskAndRefresh();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNodeHeader(StoryNode node, ref bool isDirty)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Node: {node.Id}", EditorStyles.boldLabel);
            GUI.backgroundColor = Styles.ErrorColor;
            if (GUILayout.Button("Delete", GUILayout.Width(60)))
            {
                if (EditorUtility.DisplayDialog("Delete", $"Delete {node.Id}?", "Yes", "No"))
                {
                    StoryEditorContext.DeleteNodeAndCleanReferences(_currentPackage, node.Id);
                    _selectedNodeId = null;
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            string newId = EditorGUILayout.DelayedTextField("Rename ID", node.Id);
            if (newId != node.Id && !string.IsNullOrEmpty(newId))
            {
                if (_currentPackage.Nodes.Any(n => n.Id == newId)) EditorUtility.DisplayDialog("Error", "ID Exists", "OK");
                else
                {
                    StoryEditorContext.RenameNode(_currentPackage, node.Id, newId);
                    _selectedNodeId = newId;
                    isDirty = true;
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void CheckAndCleanOrphans()
        {
            var orphans = StoryEditorContext.GetOrphanedNodes(_currentPackage);
            if (orphans.Count == 0) { EditorUtility.DisplayDialog("Check", "No orphans.", "OK"); return; }
            if (EditorUtility.DisplayDialog("Orphans", $"Found {orphans.Count} orphans. Delete?", "Yes", "No"))
            {
                StoryEditorContext.DeleteNodes(_currentPackage, orphans);
                if (orphans.Contains(_selectedNodeId)) _selectedNodeId = null;
                SaveToDiskAndRefresh();
            }
        }

        private void SaveToDiskAndRefresh()
        {
            if (_currentPackage == null) return;
            StoryEditorContext.SavePackage(_currentPackage, StoryEditorContext.CurrentFilePath);
            if (_currentPackage.Nodes.Count > 0) _targetPanel.RefreshStoryLayout(_currentPackage.Nodes[0], _currentPackage);
            else _targetPanel.ClearOldLayout();
            EditorUtility.SetDirty(_targetPanel.gameObject);
            SceneView.RepaintAll();
        }

        private void CreateAndLoad()
        {
            var newPkg = StoryEditorContext.CreateDefaultPackage(_chapterId.ToString());
            StoryEditorContext.SavePackage(newPkg, StoryEditorContext.CurrentFilePath);
            AssetDatabase.Refresh();
            LoadAndGenerate();
        }

        private void LoadAndGenerate()
        {
            if (_targetPanel == null) return;
            _currentPackage = StoryEditorContext.LoadPackage(StoryEditorContext.CurrentFilePath);
            if (_currentPackage != null && _currentPackage.Nodes.Count > 0)
            {
                _targetPanel.RefreshStoryLayout(_currentPackage.Nodes[0], _currentPackage);
                EditorUtility.SetDirty(_targetPanel.gameObject);
            }
        }

        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        }
    }
}