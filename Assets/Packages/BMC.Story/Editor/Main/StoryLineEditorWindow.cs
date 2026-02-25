using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

namespace BMC.Story.Editor
{
    public class StoryLineEditorWindow : EditorWindow
    {
        // 核心狀態
        private int _chapterId = 1;
        private StoryLinePanel _targetPanel;

        public StoryPackage CurrentPackage { get; private set; }
        public int ChapterId => _chapterId;
        private string _selectedNodeId;

        // UI 狀態
        private Vector2 _nodeListScrollPos;
        private Vector2 _nodeDetailScrollPos;
        private string _searchFilter = "";
        private string _newVarName = "";

        // 反射用的 Drawer 字典
        private Dictionary<StoryEvent.ActionOneofCase, StoryActionDrawer> _drawers = new Dictionary<StoryEvent.ActionOneofCase, StoryActionDrawer>();
        private List<StoryActionDrawer> _sortedDrawers = new List<StoryActionDrawer>();

        public static class Styles
        {
            public static GUIStyle SceneLabel = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.yellow },
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            public static GUIStyle HeaderLabel = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            public static Color SelectionColor = Color.cyan;
            public static Color WarningColor = new Color(1f, 0.8f, 0.6f);
            public static Color ErrorColor = new Color(1f, 0.4f, 0.4f);
            public static Color SuccessColor = new Color(0.6f, 1f, 0.6f);
            public static Color BackupColor = new Color(0.6f, 0.8f, 1f);
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
            InitializeDrawers();
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

        private void InitializeDrawers()
        {
            _drawers.Clear();
            _sortedDrawers.Clear();

            var drawerTypes = TypeCache.GetTypesDerivedFrom<StoryActionDrawer>();
            foreach (var type in drawerTypes)
            {
                if (type.IsAbstract) continue;
                var drawer = (StoryActionDrawer)Activator.CreateInstance(type);
                _drawers[drawer.ActionCase] = drawer;
                _sortedDrawers.Add(drawer);
            }
            _sortedDrawers = _sortedDrawers.OrderBy(d => d.MenuPath).ToList();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (CurrentPackage == null || _targetPanel == null) return;
            var items = _targetPanel.GetComponentsInChildren<StoryLineItem>();
            if (items == null || items.Length == 0) return;

            var nodeMap = new Dictionary<string, Transform>();
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.NodeID) && !nodeMap.ContainsKey(item.NodeID))
                    nodeMap.Add(item.NodeID, item.transform);
            }

            foreach (var node in CurrentPackage.Nodes)
            {
                if (!nodeMap.ContainsKey(node.Id)) continue;
                Vector3 startPos = nodeMap[node.Id].position;
                var targets = StoryEditorContext.GetTargetNodeIds(node).ToList();
                bool isSelected = (node.Id == _selectedNodeId);

                if (node.Id == "Start") Handles.color = Color.green;
                else if (targets.Count == 0) Handles.color = Color.red;
                else Handles.color = Color.white;

                if (isSelected) Handles.color = Styles.SelectionColor;
                Handles.DrawWireDisc(startPos, Vector3.up, 0.5f);
                if (isSelected) Handles.Label(startPos + Vector3.up * 1.0f, $"<color=cyan>Editing:</color> {node.Id}", Styles.SceneLabel);

                foreach (var targetId in targets)
                {
                    if (!string.IsNullOrEmpty(targetId) && nodeMap.ContainsKey(targetId))
                    {
                        Vector3 endPos = nodeMap[targetId].position;
                        if (isSelected)
                        {
                            Handles.color = Styles.SelectionColor;
                            Handles.DrawBezier(startPos, endPos, startPos + Vector3.up * 2, endPos + Vector3.up * 2, Styles.SelectionColor, null, 3f);
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
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Story System Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            DrawTargetPanelBinding();
            if (_targetPanel == null) return;

            DrawHorizontalLine();
            DrawChapterSettings();
            DrawFileOperations();
            EditorGUILayout.Space();
            DrawHorizontalLine();

            if (CurrentPackage != null) DrawEditorInterface();
            else ShowWaitingInterface();
        }

        private void DrawTargetPanelBinding()
        {
            EditorGUI.BeginChangeCheck();
            _targetPanel = (StoryLinePanel)EditorGUILayout.ObjectField("UI Panel (Scene)", _targetPanel, typeof(StoryLinePanel), true);
            if (EditorGUI.EndChangeCheck()) { }
            if (_targetPanel == null) EditorGUILayout.HelpBox("請先將場景中的 StoryLinePanel 拖曳至此。", MessageType.Warning);
        }

        private void DrawChapterSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Chapter Config", Styles.HeaderLabel);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            _chapterId = EditorGUILayout.IntField("Chapter ID", _chapterId);
            if (_chapterId < 1) _chapterId = 1;

            if (EditorGUI.EndChangeCheck())
            {
                StoryEditorContext.CurrentChapterId = _chapterId;
                CurrentPackage = null;
                _selectedNodeId = null;
                SceneView.RepaintAll();
            }

            GUI.enabled = false;
            EditorGUILayout.TextField(StoryEditorContext.CurrentFilePath);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (CurrentPackage != null && CurrentPackage.InitialVariables != null)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField($"Initial Variables ({CurrentPackage.InitialVariables.Count})", EditorStyles.miniBoldLabel);

                bool varsChanged = false;
                var keys = CurrentPackage.InitialVariables.Keys.ToList();

                foreach (var key in keys)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(key, GUILayout.Width(110));

                    EditorGUI.BeginChangeCheck();
                    int val = EditorGUILayout.IntField(CurrentPackage.InitialVariables[key]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        CurrentPackage.InitialVariables[key] = val;
                        varsChanged = true;
                    }

                    GUI.backgroundColor = Styles.ErrorColor;
                    if (GUILayout.Button("X", GUILayout.Width(25)))
                    {
                        CurrentPackage.InitialVariables.Remove(key);
                        varsChanged = true;
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                _newVarName = EditorGUILayout.TextField(_newVarName);
                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button("Add Var", GUILayout.Width(70)))
                {
                    if (!string.IsNullOrEmpty(_newVarName) && !CurrentPackage.InitialVariables.ContainsKey(_newVarName))
                    {
                        CurrentPackage.InitialVariables.Add(_newVarName, 0);
                        _newVarName = "";
                        varsChanged = true;
                        GUI.FocusControl(null);
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (varsChanged) SaveToDiskAndRefresh();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFileOperations()
        {
            bool fileExists = File.Exists(StoryEditorContext.CurrentFilePath);
            EditorGUILayout.BeginHorizontal();

            if (fileExists)
            {
                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button(" Load & Edit", GUILayout.Height(30))) LoadAndGenerate();
                GUI.backgroundColor = Styles.BackupColor;
                if (GUILayout.Button(" History", GUILayout.Height(30), GUILayout.Width(70))) ShowBackupMenu();
            }
            else
            {
                GUI.backgroundColor = Styles.WarningColor;
                if (GUILayout.Button("Create New Package", GUILayout.Height(30))) CreateAndLoad();
            }

            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Clear Scene View", GUILayout.Height(30), GUILayout.Width(120))) _targetPanel.ClearOldLayout();
            EditorGUILayout.EndHorizontal();
        }

        private void ShowWaitingInterface()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status: Idle", EditorStyles.centeredGreyMiniLabel);
            if (File.Exists(StoryEditorContext.CurrentFilePath)) EditorGUILayout.HelpBox($"File found: Chapter {_chapterId}\nClick 'Load & Edit' to begin.", MessageType.Info);
            else EditorGUILayout.LabelField("File not found. Create a new one to start.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawEditorInterface()
        {
            EditorGUILayout.LabelField($"Package Content (Nodes: {CurrentPackage.Nodes.Count})", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawNodeListSidebar();
            DrawNodeDetailArea();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNodeListSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New Node", GUILayout.Height(25)))
            {
                var newNode = StoryEditorContext.CreateNewNode(CurrentPackage);
                _selectedNodeId = newNode.Id;
                SaveToDiskAndRefresh();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            _nodeListScrollPos = EditorGUILayout.BeginScrollView(_nodeListScrollPos, "box", GUILayout.ExpandHeight(true));

            foreach (var node in CurrentPackage.Nodes)
            {
                if (!string.IsNullOrEmpty(_searchFilter) && node.Id.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (_selectedNodeId == node.Id) GUI.backgroundColor = Styles.SelectionColor;
                string displayName = string.IsNullOrEmpty(node.Id) ? "[Empty ID]" : node.Id;
                if (GUILayout.Button(displayName, EditorStyles.miniButton, GUILayout.Height(24)))
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
            if (GUILayout.Button("Cleanup Orphans", GUILayout.Height(25))) CheckAndCleanOrphans();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        private void CheckAndCleanOrphans()
        {
            var orphans = StoryEditorContext.GetOrphanedNodes(CurrentPackage);
            if (orphans.Count == 0)
            {
                EditorUtility.DisplayDialog("Check Complete", "No orphan nodes found.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("Orphans Found", $"Found {orphans.Count} unreachable nodes.\nDo you want to delete them?", "Delete All", "Cancel"))
            {
                StoryEditorContext.DeleteNodes(CurrentPackage, orphans);
                if (orphans.Contains(_selectedNodeId)) _selectedNodeId = null;
                SaveToDiskAndRefresh();
            }
        }

        private void DrawNodeDetailArea()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            _nodeDetailScrollPos = EditorGUILayout.BeginScrollView(_nodeDetailScrollPos);

            if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                var selectedNode = CurrentPackage.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
                if (selectedNode != null) DrawNodeDetails(selectedNode);
                else EditorGUILayout.HelpBox("Error: Selected node does not exist.", MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField("Select a node from the left list to edit properties.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawNodeDetails(StoryNode node)
        {
            bool isDirty = false;
            DrawNodeHeader(node, ref isDirty);
            if (_selectedNodeId == null) return;

            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("Node Properties", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            node.Title = EditorGUILayout.TextField("Title", node.Title);
            node.PreviewImagePath = EditorGUILayout.TextField("Preview Image", node.PreviewImagePath);
            EditorGUILayout.LabelField("Memo (PS):");
            node.Ps = EditorGUILayout.TextArea(node.Ps, GUILayout.Height(35));
            if (EditorGUI.EndChangeCheck()) isDirty = true;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("Auto Jump Logic", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            node.AutoJumpDelay = EditorGUILayout.DelayedFloatField("Delay (Seconds)", node.AutoJumpDelay);
            if (EditorGUI.EndChangeCheck()) isDirty = true;

            DrawTargetIdSelector("Jump Target", () => node.AutoJumpNodeId, (val) => node.AutoJumpNodeId = val, node);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Events Pipeline (Execute sequentially)", EditorStyles.boldLabel);

            for (int i = 0; i < node.OnEnterEvents.Count; i++)
            {
                var evt = node.OnEnterEvents[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

                string title = _drawers.TryGetValue(evt.ActionCase, out var d) ? d.MenuPath : evt.ActionCase.ToString();
                GUILayout.Label($"#{i + 1}  {title}", EditorStyles.miniLabel);

                if (i > 0 && GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (node.OnEnterEvents[i], node.OnEnterEvents[i - 1]) = (node.OnEnterEvents[i - 1], node.OnEnterEvents[i]);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                if (i < node.OnEnterEvents.Count - 1 && GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (node.OnEnterEvents[i], node.OnEnterEvents[i + 1]) = (node.OnEnterEvents[i + 1], node.OnEnterEvents[i]);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    node.OnEnterEvents.RemoveAt(i);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                evt.DelaySeconds = EditorGUILayout.DelayedFloatField("Pre-Delay", evt.DelaySeconds);
                if (EditorGUI.EndChangeCheck()) isDirty = true;

                if (_drawers.TryGetValue(evt.ActionCase, out var drawer))
                {
                    if (drawer.Draw(node, evt, this)) isDirty = true;
                }
                else
                {
                    EditorGUILayout.HelpBox($"無法解析的事件類型: {evt.ActionCase}", MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Event:", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            var groupedDrawers = _sortedDrawers.GroupBy(d => {
                int slashIdx = d.MenuPath.IndexOf('/');
                return slashIdx > 0 ? d.MenuPath.Substring(0, slashIdx) : "General";
            });

            foreach (var group in groupedDrawers)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(group.Key, EditorStyles.miniBoldLabel, GUILayout.Width(50));

                foreach (var drawer in group)
                {
                    string btnName = drawer.MenuPath.Substring(drawer.MenuPath.IndexOf('/') + 1);
                    if (GUILayout.Button(btnName, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    {
                        node.OnEnterEvents.Add(drawer.CreateNewEvent());
                        SaveToDiskAndRefresh();
                        GUIUtility.ExitGUI();
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            if (isDirty) SaveToDiskAndRefresh();
        }

        private void DrawNodeHeader(StoryNode node, ref bool isDirty)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Node ID: {node.Id}", EditorStyles.boldLabel);
            GUI.backgroundColor = Styles.ErrorColor;
            if (GUILayout.Button("Delete Node", GUILayout.Width(90)))
            {
                if (EditorUtility.DisplayDialog("Confirm Delete", $"Are you sure you want to delete node '{node.Id}'?", "Delete", "Cancel"))
                {
                    StoryEditorContext.DeleteNodeAndCleanReferences(CurrentPackage, node.Id);
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
                if (CurrentPackage.Nodes.Any(n => n.Id == newId)) EditorUtility.DisplayDialog("Rename Failed", $"ID '{newId}' already exists.", "OK");
                else
                {
                    StoryEditorContext.RenameNode(CurrentPackage, node.Id, newId);
                    _selectedNodeId = newId;
                    isDirty = true;
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                }
            }
        }

        public void DrawTargetIdSelector(string label, Func<string> getter, Action<string> setter, StoryNode parentForGen = null)
        {
            EditorGUILayout.BeginHorizontal();
            string current = getter();

            EditorGUI.BeginChangeCheck();
            string newVal = EditorGUILayout.DelayedTextField(label, current);
            if (EditorGUI.EndChangeCheck())
            {
                setter(newVal);
                SaveToDiskAndRefresh();
            }

            bool exists = !string.IsNullOrEmpty(newVal) && CurrentPackage.Nodes.Any(n => n.Id == newVal);
            bool isEmpty = string.IsNullOrEmpty(newVal);

            if (exists)
            {
                if (GUILayout.Button("Go", GUILayout.Width(30)))
                {
                    _selectedNodeId = newVal;
                    GUI.FocusControl(null);
                    SceneView.RepaintAll();
                }
            }
            else if (!isEmpty)
            {
                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button("New", GUILayout.Width(40)))
                {
                    StoryEditorContext.CreateSpecificNode(CurrentPackage, newVal);
                    SaveToDiskAndRefresh();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (parentForGen != null && isEmpty)
            {
                if (GUILayout.Button("Gen", GUILayout.Width(40)))
                {
                    string nextId = StoryEditorContext.GenerateUniqueNextID(CurrentPackage, parentForGen);
                    setter(nextId);
                    StoryEditorContext.CreateSpecificNode(CurrentPackage, nextId);
                    SaveToDiskAndRefresh();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        public bool DrawConditionList(string label, IList<Condition> conditions)
        {
            bool changed = false;
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(25)))
            {
                conditions.Add(new Condition { TargetType = Condition.Types.TargetType.GlobalVariable, CompareType = Condition.Types.CompareType.GreaterEqual, VariableId = "" });
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < conditions.Count; i++)
            {
                var cond = conditions[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();

                cond.TargetType = (Condition.Types.TargetType)EditorGUILayout.EnumPopup(cond.TargetType, GUILayout.Width(90));

                switch (cond.TargetType)
                {
                    case Condition.Types.TargetType.GlobalVariable:
                        DrawConditionVariableSelector(cond);
                        break;
                    case Condition.Types.TargetType.CharacterStat:
                        cond.CharacterId = EditorGUILayout.IntField(cond.CharacterId, GUILayout.Width(50));
                        cond.StatType = (StatType)EditorGUILayout.EnumPopup(cond.StatType, GUILayout.Width(65));
                        break;
                    case Condition.Types.TargetType.CharacterAffection:
                        cond.CharacterId = EditorGUILayout.IntField(cond.CharacterId, GUILayout.Width(45));
                        GUILayout.Label("->", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(15));
                        cond.TargetCharacterId = EditorGUILayout.IntField(cond.TargetCharacterId, GUILayout.Width(45));
                        break;
                }

                cond.CompareType = (Condition.Types.CompareType)EditorGUILayout.EnumPopup(cond.CompareType, GUILayout.Width(75));
                cond.TargetValue = EditorGUILayout.IntField(cond.TargetValue, GUILayout.Width(45));

                if (EditorGUI.EndChangeCheck()) changed = true;

                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    conditions.RemoveAt(i);
                    changed = true;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            return changed;
        }

        private void DrawConditionVariableSelector(Condition cond)
        {
            List<string> options = new List<string> { "Custom..." };
            string[] defaultVars = { "Player_Money", "Global_Morality" };
            options.AddRange(defaultVars);

            if (CurrentPackage != null && CurrentPackage.InitialVariables != null)
            {
                foreach (var key in CurrentPackage.InitialVariables.Keys)
                {
                    if (!options.Contains(key)) options.Add(key);
                }
            }

            if (cond.VariableId == null) cond.VariableId = "";

            int currentIndex = options.IndexOf(cond.VariableId);
            bool isCustom = currentIndex == -1 || currentIndex == 0;
            if (currentIndex == -1) currentIndex = 0;

            int newIndex = EditorGUILayout.Popup(currentIndex, options.ToArray(), GUILayout.Width(80));
            if (newIndex != currentIndex)
            {
                cond.VariableId = (newIndex == 0) ? "" : options[newIndex];
                isCustom = (newIndex == 0);
            }

            if (isCustom)
            {
                cond.VariableId = EditorGUILayout.TextField(cond.VariableId, GUILayout.Width(70));
            }
        }

        public void SaveToDiskAndRefresh()
        {
            if (CurrentPackage == null) return;
            StoryEditorContext.SavePackage(CurrentPackage, StoryEditorContext.CurrentFilePath);
            if (_targetPanel != null)
            {
                if (CurrentPackage.Nodes.Count > 0) _targetPanel.RefreshStoryLayout(CurrentPackage.Nodes[0], CurrentPackage);
                else _targetPanel.ClearOldLayout();
                EditorUtility.SetDirty(_targetPanel.gameObject);
            }
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
            CurrentPackage = StoryEditorContext.LoadPackage(StoryEditorContext.CurrentFilePath);
            if (CurrentPackage != null && CurrentPackage.Nodes.Count > 0)
            {
                _targetPanel.RefreshStoryLayout(CurrentPackage.Nodes[0], CurrentPackage);
                EditorUtility.SetDirty(_targetPanel.gameObject);
                Repaint();
            }
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

        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
        }
    }
}