using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditorInternal;

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

        // Unity 內建的可拖拉列表
        private ReorderableList _nodeReorderableList;

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
            if (target != null) window._targetPanel = target;
            window.Show();
        }

        [MenuItem("BMC/Story/Open Story Editor", priority = 0)]
        public static void OpenStoryEditor()
        {
            var window = GetWindow<StoryLineEditorWindow>("Story Editor");
            window.Show();
            window.TryAutoBindAndOpenPrefab();
        }

        private StoryLinePanel FindPanel()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                var p = stage.prefabContentsRoot.GetComponentInChildren<StoryLinePanel>(true);
                if (p != null) return p;
            }

            var panels = UnityEngine.Object.FindObjectsByType<StoryLinePanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in panels)
            {
                if (!EditorUtility.IsPersistent(p.gameObject))
                {
                    return p;
                }
            }
            return null;
        }

        private void TryAutoBindAndOpenPrefab()
        {
            _targetPanel = FindPanel();
            if (_targetPanel != null)
            {
                Repaint();
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab StoryLinePanel");
            string targetPath = null;

            if (guids.Length > 0)
            {
                targetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            else
            {
                guids = AssetDatabase.FindAssets("t:Prefab");
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefabObj != null && prefabObj.GetComponentInChildren<StoryLinePanel>(true) != null)
                    {
                        targetPath = path;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                GameObject targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
                if (targetPrefab != null)
                {
                    PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
                    PrefabStage.prefabStageOpened += OnPrefabStageOpened;
                    AssetDatabase.OpenAsset(targetPrefab);
                    EditorApplication.delayCall += () =>
                    {
                        if (_targetPanel == null)
                        {
                            _targetPanel = FindPanel();
                            Repaint();
                        }
                    };
                    return;
                }
            }

            Debug.LogWarning("[Story Editor] 找不到包含 StoryLinePanel 的 Prefab。");
        }

        private void OnPrefabStageOpened(PrefabStage stage)
        {
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            _targetPanel = FindPanel();
            Repaint();
        }

        private void OnEnable()
        {
            _chapterId = StoryEditorContext.CurrentChapterId;
            SceneView.duringSceneGui += OnSceneGUI;
            InitializeDrawers();

            if (_targetPanel == null)
            {
                _targetPanel = FindPanel();
            }
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
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
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

                var targets = StoryLinePanel.GetTargetNodeIds(node).ToList();
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
            EditorGUILayout.Space(5);

            if (_targetPanel == null)
            {
                EditorGUILayout.HelpBox("無法自動找到 StoryLinePanel。\n請點擊下方按鈕自動尋找並開啟 Prefab。", MessageType.Warning);
                if (GUILayout.Button("Auto Find & Open Prefab", GUILayout.Height(25)))
                {
                    TryAutoBindAndOpenPrefab();
                }
                return;
            }

            DrawChapterSettings();
            DrawFileOperations();
            EditorGUILayout.Space();
            DrawHorizontalLine();

            if (CurrentPackage != null) DrawEditorInterface();
            else ShowWaitingInterface();
        }

        private void DrawChapterSettings()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Chapter ID", EditorStyles.boldLabel, GUILayout.Width(75));

            EditorGUI.BeginChangeCheck();
            _chapterId = EditorGUILayout.IntField(_chapterId, GUILayout.Width(50));
            if (_chapterId < 1) _chapterId = 1;

            if (EditorGUI.EndChangeCheck())
            {
                StoryEditorContext.CurrentChapterId = _chapterId;
                CurrentPackage = null;
                _selectedNodeId = null;
                SceneView.RepaintAll();
            }

            GUILayout.Space(10);

            UnityEngine.Object assetObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(StoryEditorContext.CurrentFilePath);
            GUI.enabled = false;
            EditorGUILayout.ObjectField(assetObj, typeof(UnityEngine.Object), false);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (CurrentPackage != null && CurrentPackage.InitialVariables != null)
            {
                EditorGUILayout.Space(2);
                bool varsChanged = false;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Initial Variables ({CurrentPackage.InitialVariables.Count})", EditorStyles.miniBoldLabel, GUILayout.Width(130));
                GUILayout.FlexibleSpace();
                _newVarName = EditorGUILayout.TextField(_newVarName, GUILayout.Width(120));

                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button("Add Var", GUILayout.Width(65)))
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

                var keys = CurrentPackage.InitialVariables.Keys.ToList();

                foreach (var key in keys)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(10);
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

        private void InitReorderableList()
        {
            if (CurrentPackage == null)
            {
                _nodeReorderableList = null;
                return;
            }

            _nodeReorderableList = new ReorderableList(CurrentPackage.Nodes, typeof(StoryNode), true, false, false, false);

            _nodeReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= CurrentPackage.Nodes.Count) return;
                var node = CurrentPackage.Nodes[index];

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                string displayName = string.IsNullOrEmpty(node.Id) ? "[Empty ID]" : node.Id;

                GUIStyle labelStyle = isActive ? EditorStyles.whiteLabel : EditorStyles.label;
                GUI.Label(rect, displayName, labelStyle);
            };

            _nodeReorderableList.onSelectCallback = (ReorderableList list) =>
            {
                if (list.index >= 0 && list.index < CurrentPackage.Nodes.Count)
                {
                    _selectedNodeId = CurrentPackage.Nodes[list.index].Id;
                    GUI.FocusControl(null);
                    SceneView.RepaintAll();
                }
            };

            _nodeReorderableList.onReorderCallback = (ReorderableList list) =>
            {
                SaveToDiskAndRefresh();
            };

            _nodeReorderableList.headerHeight = 0;
            _nodeReorderableList.footerHeight = 0;
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

            bool isSearching = !string.IsNullOrEmpty(_searchFilter);

            if (isSearching)
            {
                for (int i = 0; i < CurrentPackage.Nodes.Count; i++)
                {
                    var node = CurrentPackage.Nodes[i];
                    if (node.Id.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    EditorGUILayout.BeginHorizontal();

                    if (_selectedNodeId == node.Id) GUI.backgroundColor = Styles.SelectionColor;
                    string displayName = string.IsNullOrEmpty(node.Id) ? "[Empty ID]" : node.Id;
                    if (GUILayout.Button(displayName, EditorStyles.miniButton, GUILayout.Height(24)))
                    {
                        _selectedNodeId = node.Id;
                        GUI.FocusControl(null);
                        SceneView.RepaintAll();
                    }
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                if (_nodeReorderableList == null && CurrentPackage != null)
                {
                    InitReorderableList();
                }

                if (_nodeReorderableList != null)
                {
                    int targetIdx = -1;
                    for (int i = 0; i < CurrentPackage.Nodes.Count; i++)
                    {
                        if (CurrentPackage.Nodes[i].Id == _selectedNodeId)
                        {
                            targetIdx = i;
                            break;
                        }
                    }

                    if (_nodeReorderableList.index != targetIdx)
                        _nodeReorderableList.index = targetIdx;

                    _nodeReorderableList.DoLayoutList();
                }
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
            var orphans = GetOrphanedNodesLocal();

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

        private List<string> GetOrphanedNodesLocal()
        {
            List<string> orphans = new List<string>();
            if (CurrentPackage == null || CurrentPackage.Nodes.Count == 0) return orphans;

            HashSet<string> reachable = new HashSet<string>();
            Queue<string> queue = new Queue<string>();

            var startNode = CurrentPackage.Nodes.FirstOrDefault(n => n.Id == "Start") ?? CurrentPackage.Nodes[0];
            queue.Enqueue(startNode.Id);
            reachable.Add(startNode.Id);

            while (queue.Count > 0)
            {
                string currId = queue.Dequeue();
                var node = CurrentPackage.Nodes.FirstOrDefault(n => n.Id == currId);
                if (node == null) continue;

                foreach (var targetId in StoryLinePanel.GetTargetNodeIds(node))
                {
                    if (!string.IsNullOrEmpty(targetId) && !reachable.Contains(targetId))
                    {
                        reachable.Add(targetId);
                        queue.Enqueue(targetId);
                    }
                }
            }

            foreach (var node in CurrentPackage.Nodes)
            {
                if (!reachable.Contains(node.Id)) orphans.Add(node.Id);
            }
            return orphans;
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

            node.Title = EditorGUILayout.TextField("Title", node.Title)?.Replace("\n", "").Replace("\r", "");
            node.PreviewImagePath = EditorGUILayout.TextField("Preview Image", node.PreviewImagePath)?.Replace("\n", "").Replace("\r", "");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Memo (PS)", GUILayout.Width(EditorGUIUtility.labelWidth - 5));
            GUIStyle multiLineStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            node.Ps = EditorGUILayout.TextArea(node.Ps, multiLineStyle);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck()) isDirty = true;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("Auto Jump Logic", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            node.AutoJumpDelay = EditorGUILayout.DelayedFloatField("Delay (Seconds)", node.AutoJumpDelay);
            if (EditorGUI.EndChangeCheck()) isDirty = true;

            // --- 節點層級的好感度跳轉判定 ---
            GUI.backgroundColor = new Color(1f, 0.9f, 0.95f);
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = Color.white;

            if (DrawGenericRulesBlock(
                "Affection Jump Rules (依序判定，符合即跳轉)",
                node.AutoJumpAffectionRules,
                () => new StoryNode.Types.NodeAffectionJumpRule
                {
                    CharacterId = 0,
                    CompareType = Condition.Types.CompareType.GreaterEqual,
                    TargetValue = 50,
                    TargetNodeId = ""
                },
                (rule) => {
                    bool changed = false;
                    GUILayout.Label("Char ID:", GUILayout.Width(50));
                    EditorGUI.BeginChangeCheck();
                    rule.CharacterId = EditorGUILayout.IntField(rule.CharacterId, GUILayout.Width(35));
                    rule.CompareType = (Condition.Types.CompareType)EditorGUILayout.EnumPopup(rule.CompareType, GUILayout.Width(100));
                    rule.TargetValue = EditorGUILayout.IntField(rule.TargetValue, GUILayout.Width(40));
                    if (EditorGUI.EndChangeCheck()) changed = true;

                    float oldLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 75;
                    DrawTargetIdSelector("-> Jump To:", () => rule.TargetNodeId ?? "", (val) => { rule.TargetNodeId = val; changed = true; }, node);
                    EditorGUIUtility.labelWidth = oldLabelWidth;

                    return changed;
                }))
            {
                isDirty = true;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            // --- 節點層級的全域變數/SaveKey跳轉判定 (支援切換 Scope) ---
            GUI.backgroundColor = new Color(0.9f, 0.95f, 1f);
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = Color.white;

            if (DrawGenericRulesBlock(
                "Variable Jump Rules (字串比對，相同即自動跳轉)",
                node.AutoJumpVariableRules,
                () => new StoryNode.Types.NodeVariableJumpRule
                {
                    Scope = VariableScope.SaveKey,
                    ConditionKey = "MyKey",
                    ConditionValue = "True",
                    TargetNodeId = ""
                },
                (rule) => {
                    bool changed = false;
                    EditorGUI.BeginChangeCheck();

                    // 新增的 VariableScope 選擇器
                    rule.Scope = (VariableScope)EditorGUILayout.EnumPopup(rule.Scope, GUILayout.Width(80));

                    GUILayout.Label("Key:", GUILayout.Width(30));
                    rule.ConditionKey = EditorGUILayout.TextField(rule.ConditionKey, GUILayout.Width(80));

                    GUILayout.Label("==", EditorStyles.boldLabel, GUILayout.Width(20));

                    GUILayout.Label("Value:", GUILayout.Width(40));
                    rule.ConditionValue = EditorGUILayout.TextField(rule.ConditionValue, GUILayout.Width(70));
                    if (EditorGUI.EndChangeCheck()) changed = true;

                    float oldLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 75;
                    DrawTargetIdSelector("-> Jump To:", () => rule.TargetNodeId ?? "", (val) => { rule.TargetNodeId = val; changed = true; }, node);
                    EditorGUIUtility.labelWidth = oldLabelWidth;

                    return changed;
                }))
            {
                isDirty = true;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);

            // 防呆預設跳轉
            DrawTargetIdSelector("Fallback Target (皆不符合時)", () => node.AutoJumpNodeId, (val) => node.AutoJumpNodeId = val, node);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            if (DrawEventList("Events Pipeline (Execute sequentially)", node.OnEnterEvents, node))
            {
                isDirty = true;
            }

            if (isDirty) SaveToDiskAndRefresh();
        }

        /// <summary>
        /// 共用的 UI 輔助方法：用來繪製泛型的規則列表 (例如好感度、變數跳轉)。
        /// </summary>
        public bool DrawGenericRulesBlock<T>(
            string title,
            IList<T> rules,
            Func<T> createNewRule,
            Func<T, bool> drawRuleInner)
        {
            bool changed = false;
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

            for (int r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                EditorGUILayout.BeginHorizontal();

                if (drawRuleInner(rule))
                {
                    changed = true;
                }

                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    rules.RemoveAt(r);
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
                rules.Add(createNewRule());
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            return changed;
        }

        public bool DrawEventList(string headerTitle, IList<StoryEvent> events, StoryNode node)
        {
            bool isDirty = false;

            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(headerTitle))
            {
                GUILayout.Label(headerTitle, EditorStyles.boldLabel);
            }
            GUILayout.FlexibleSpace();

            var groupedDrawers = _sortedDrawers.GroupBy(d => d.MenuPath.Contains('/') ? d.MenuPath.Split('/')[0] : "Other");

            foreach (var group in groupedDrawers)
            {
                if (EditorGUILayout.DropdownButton(new GUIContent($"+ {group.Key}"), FocusType.Keyboard, GUILayout.Width(75), GUILayout.Height(20)))
                {
                    GenericMenu menu = new GenericMenu();
                    foreach (var drawer in group)
                    {
                        var currentDrawer = drawer;

                        string displayPath = currentDrawer.MenuPath.Contains('/')
                            ? currentDrawer.MenuPath.Substring(currentDrawer.MenuPath.IndexOf('/') + 1)
                            : currentDrawer.MenuPath;

                        menu.AddItem(new GUIContent(displayPath), false, () => {
                            events.Add(currentDrawer.CreateNewEvent());
                            SaveToDiskAndRefresh();
                            Repaint();
                        });
                    }
                    menu.ShowAsContext();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

                string title = _drawers.TryGetValue(evt.ActionCase, out var d) ? d.MenuPath : evt.ActionCase.ToString();
                GUILayout.Label($"#{i + 1}  {title}", EditorStyles.miniLabel);

                if (i > 0 && GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (events[i], events[i - 1]) = (events[i - 1], events[i]);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                if (i < events.Count - 1 && GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (events[i], events[i + 1]) = (events[i + 1], events[i]);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    events.RemoveAt(i);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Pre-Delay", GUILayout.Width(65));
                evt.DelaySeconds = EditorGUILayout.DelayedFloatField(evt.DelaySeconds, GUILayout.Width(60));
                GUILayout.Space(15);
                evt.WaitForTrigger = EditorGUILayout.ToggleLeft("Wait For Trigger (手動觸發)", evt.WaitForTrigger);
                EditorGUILayout.EndHorizontal();

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

            return isDirty;
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
                    case Condition.Types.TargetType.SaveVariable: // 兩者都支援純文字輸入 Key
                        cond.VariableId = EditorGUILayout.TextField(cond.VariableId, GUILayout.Width(80));
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
            InitReorderableList();
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