using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Story.Editor
{
    public class StoryEditorWindow : EditorWindow
    {
        private int _chapterId = 1;
        private StoryLinePanel _targetPanel;
        private StoryPackage _currentPackage;
        private string _selectedNodeId;
        private Vector2 _nodeListScrollPos;
        private string _searchFilter = "";

        private static class Styles
        {
            public static GUIStyle SceneLabel;
            public static Color SelectionColor = Color.cyan;
            public static Color WarningColor = new Color(1f, 0.8f, 0.6f);
            public static Color ErrorColor = new Color(1f, 0.4f, 0.4f);
            public static Color SuccessColor = Color.green;
            public static Color BackupColor = new Color(0.6f, 0.8f, 1f);

            static Styles()
            {
                SceneLabel = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.yellow }, fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            }
        }

        public static void ShowWindow(StoryLinePanel target)
        {
            var window = GetWindow<StoryEditorWindow>("Story Editor");
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

            Debug.Log("[StoryEditorWindow] 視窗已關閉，已清除場景 Layout 與暫存備份。");
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

                Handles.color = (node.Id == "Start") ? Color.green : (node.Choices.Count == 0 ? Color.red : Color.white);
                if (node.Id == _selectedNodeId) Handles.color = Color.cyan;

                Handles.DrawWireDisc(startPos, Vector3.up, 0.5f);

                if (node.Id == _selectedNodeId)
                {
                    Handles.Label(startPos + Vector3.up * 1.0f, $"<color=cyan>Editing:</color> {node.Id}", Styles.SceneLabel);
                }

                foreach (var choice in node.Choices)
                {
                    if (!string.IsNullOrEmpty(choice.TargetNodeId) && nodeMap.ContainsKey(choice.TargetNodeId))
                    {
                        Vector3 endPos = nodeMap[choice.TargetNodeId].position;
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
                if (File.Exists(path)) EditorGUILayout.HelpBox("檔案存在。請點擊 'Load & Edit' 開始編輯。", MessageType.Info);
                else EditorGUILayout.LabelField("Waiting for file creation...", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawTargetPanelBinding()
        {
            EditorGUI.BeginChangeCheck();
            _targetPanel = (StoryLinePanel)EditorGUILayout.ObjectField("Target Panel", _targetPanel, typeof(StoryLinePanel), true);
            if (EditorGUI.EndChangeCheck()) { }
            if (_targetPanel == null) EditorGUILayout.HelpBox("請先指派 StoryLinePanel。", MessageType.Info);
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

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("File Path");
            EditorGUILayout.SelectableLabel(StoryEditorContext.CurrentFilePath, EditorStyles.textField, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();
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
                if (GUILayout.Button("History", GUILayout.Height(30), GUILayout.Width(70)))
                {
                    ShowBackupMenu();
                }
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

            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No backups found"));
            }
            else
            {
                foreach (var backup in backups)
                {
                    string fileName = Path.GetFileName(backup);
                    string displayName = fileName.Replace(".bytes.bak", "").Replace("StoryPackage_", "").Replace($"{_chapterId}_", "");

                    menu.AddItem(new GUIContent($"Restore: {displayName}"), false, () => {
                        if (EditorUtility.DisplayDialog("Restore Backup", $"Restore from '{fileName}'?\n\nCurrent unsaved changes (if any) will be lost.", "Yes, Restore", "Cancel"))
                        {
                            if (StoryEditorContext.RestoreBackup(backup, path))
                            {
                                LoadAndGenerate();
                                Debug.Log($"[StoryEditorWindow] Successfully restored from backup: {fileName}");
                            }
                        }
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
            EditorGUILayout.BeginVertical(GUILayout.Width(200));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("+ Create Node", GUILayout.Height(25)))
            {
                var newNode = StoryEditorContext.CreateNewNode(_currentPackage);
                _selectedNodeId = newNode.Id;
                SaveToDiskAndRefresh();
                // 修正：新增節點後強制重繪，確保 UI 狀態同步
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(5);
            _nodeListScrollPos = EditorGUILayout.BeginScrollView(_nodeListScrollPos, "box");

            foreach (var node in _currentPackage.Nodes)
            {
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool matchId = node.Id.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchText = node.Choices.Any(c => c.Text.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!matchId && !matchText) continue;
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
            if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                var selectedNode = _currentPackage.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
                if (selectedNode != null)
                {
                    DrawNodeDetails(selectedNode);
                }
                else
                {
                    EditorGUILayout.LabelField("Selected node not found (might be deleted).");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a node from the list to edit.", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawNodeDetails(StoryNode node)
        {
            bool isDirty = false;

            DrawNodeHeader(node, ref isDirty);
            if (_selectedNodeId == null) return;

            EditorGUI.BeginChangeCheck();
            node.VideoPath = EditorGUILayout.TextField("Video Path", node.VideoPath);
            if (EditorGUI.EndChangeCheck()) isDirty = true;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Choices ({node.Choices.Count}):", EditorStyles.boldLabel);

            for (int i = 0; i < node.Choices.Count; i++)
            {
                // 修正：使用 BeginChangeCheck 包裹 DrawSingleChoiceRow 以捕捉文字修改
                EditorGUI.BeginChangeCheck();
                bool structureChanged = DrawSingleChoiceRow(node, i);

                if (structureChanged)
                {
                    // 結構改變 (刪除/移動)，資料已經被 DrawSingleChoiceRow 修改
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                    break;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    // 內容改變 (文字欄位)，標記為髒，稍後存檔
                    isDirty = true;
                }
            }

            if (GUILayout.Button("+ Add Choice"))
            {
                string newId = StoryEditorContext.GenerateUniqueNextID(_currentPackage, node);
                node.Choices.Add(new Choice { Text = "New Choice", TargetNodeId = newId });
                isDirty = true;
            }

            if (isDirty) SaveToDiskAndRefresh();
        }

        private void DrawNodeHeader(StoryNode node, ref bool isDirty)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Editing Node: {node.Id}", EditorStyles.boldLabel);

            GUI.backgroundColor = Styles.ErrorColor;
            if (GUILayout.Button("Delete Node", GUILayout.Width(100)))
            {
                var dependentNodes = StoryEditorContext.GetDependentNodes(_currentPackage, node.Id);

                if (dependentNodes.Count > 1)
                {
                    var others = dependentNodes.Where(id => id != node.Id).ToList();
                    string listStr = string.Join("\n- ", others.Take(10));
                    if (others.Count > 10) listStr += "\n... and more";

                    string message = $"Node '{node.Id}' has {others.Count} EXCLUSIVE descendants.\nDelete branch?";

                    if (EditorUtility.DisplayDialog("Delete Branch?", message, "Yes, Delete All", "Cancel"))
                    {
                        int removedRefs = StoryEditorContext.DeleteNodesAndCleanReferences(_currentPackage, dependentNodes);
                        Debug.Log($"Deleted branch starting at '{node.Id}'. Removed {removedRefs} external references.");
                        _selectedNodeId = null;
                        SaveToDiskAndRefresh();
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    if (EditorUtility.DisplayDialog("Delete Node", $"Delete '{node.Id}'?", "Yes", "No"))
                    {
                        StoryEditorContext.DeleteNodeAndCleanReferences(_currentPackage, node.Id);
                        _selectedNodeId = null;
                        SaveToDiskAndRefresh();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            string newId = EditorGUILayout.DelayedTextField("Node ID", node.Id);
            if (newId != node.Id && !string.IsNullOrEmpty(newId))
            {
                if (_currentPackage.Nodes.Any(n => n.Id == newId))
                {
                    EditorUtility.DisplayDialog("Error", $"ID '{newId}' already exists!", "OK");
                }
                else
                {
                    StoryEditorContext.RenameNode(_currentPackage, node.Id, newId);
                    _selectedNodeId = newId;
                    isDirty = true;
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.Space();
        }

        private bool DrawSingleChoiceRow(StoryNode node, int index)
        {
            var choice = node.Choices[index];
            bool listStructureChanged = false;

            EditorGUILayout.BeginVertical("helpbox");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Option {index + 1}", EditorStyles.miniBoldLabel);

            if (index > 0)
            {
                if (GUILayout.Button("↑", GUILayout.Width(25), GUILayout.Height(15)))
                {
                    var temp = node.Choices[index];
                    node.Choices[index] = node.Choices[index - 1];
                    node.Choices[index - 1] = temp;
                    listStructureChanged = true;
                }
            }

            GUI.backgroundColor = Styles.ErrorColor;
            if (GUILayout.Button("X", GUILayout.Width(20), GUILayout.Height(15)))
            {
                HandleDeleteChoice(node, index, choice);
                listStructureChanged = true;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (!listStructureChanged)
            {
                choice.Text = EditorGUILayout.TextField("Text", choice.Text);
                HandleTargetIdField(choice);
            }

            EditorGUILayout.EndVertical();
            return listStructureChanged;
        }

        private void HandleTargetIdField(Choice choice)
        {
            string currentTargetId = choice.TargetNodeId;
            EditorGUILayout.BeginHorizontal();

            string newTargetId = EditorGUILayout.DelayedTextField("Target ID", currentTargetId);

            if (!string.IsNullOrEmpty(currentTargetId) && _currentPackage.Nodes.Any(n => n.Id == currentTargetId))
            {
                GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
                if (GUILayout.Button("Jump", EditorStyles.miniButton, GUILayout.Width(45)))
                {
                    _selectedNodeId = currentTargetId;
                    GUI.FocusControl(null);
                    SceneView.RepaintAll();
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            if (newTargetId != currentTargetId)
            {
                ProcessTargetIdChange(choice, currentTargetId, newTargetId);
            }

            bool targetExists = string.IsNullOrEmpty(choice.TargetNodeId) || _currentPackage.Nodes.Any(n => n.Id == choice.TargetNodeId);
            if (!targetExists)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("Target ID not found!", MessageType.Warning);
                GUI.backgroundColor = Styles.SelectionColor;
                if (GUILayout.Button("Create", GUILayout.Width(60), GUILayout.Height(38)))
                {
                    if (StoryEditorContext.CreateSpecificNode(_currentPackage, choice.TargetNodeId) != null)
                    {
                        SaveToDiskAndRefresh();
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ProcessTargetIdChange(Choice choice, string oldId, string newId)
        {
            bool oldTargetExists = _currentPackage.Nodes.Any(n => n.Id == oldId);

            if (oldTargetExists && !string.IsNullOrEmpty(oldId))
            {
                int option = EditorUtility.DisplayDialogComplex(
                    "Modify Target Node",
                    $"Change target from '{oldId}' to '{newId}'.\n\nRename node '{oldId}' to '{newId}'?\nOr just Repoint?",
                    "Rename Node", "Just Repoint", "Cancel");

                if (option == 0)
                {
                    if (_currentPackage.Nodes.Any(n => n.Id == newId))
                    {
                        EditorUtility.DisplayDialog("Error", $"Node '{newId}' already exists!", "OK");
                    }
                    else
                    {
                        if (oldId == _selectedNodeId) _selectedNodeId = newId;
                        StoryEditorContext.RenameNode(_currentPackage, oldId, newId);
                        SaveToDiskAndRefresh();
                        GUIUtility.ExitGUI();
                    }
                }
                else if (option == 1)
                {
                    choice.TargetNodeId = newId;
                    SaveToDiskAndRefresh();
                    // 修正：Repoint 雖然資料修改較小，但為了統一行為與刷新，這裡也 ExitGUI
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                choice.TargetNodeId = newId;
                SaveToDiskAndRefresh();
                GUIUtility.ExitGUI(); // 這裡也可以選擇 ExitGUI 以確保一致性
            }
        }

        private void HandleDeleteChoice(StoryNode parentNode, int index, Choice choice)
        {
            string targetId = choice.TargetNodeId;
            parentNode.Choices.RemoveAt(index);

            if (!string.IsNullOrEmpty(targetId) && _currentPackage.Nodes.Any(n => n.Id == targetId))
            {
                if (!StoryEditorContext.IsNodeReferenced(_currentPackage, targetId))
                {
                    if (EditorUtility.DisplayDialog("Delete Orphaned Node?", $"Node '{targetId}' is now orphaned.\nDelete it?", "Yes", "No"))
                    {
                        StoryEditorContext.DeleteNode(_currentPackage, targetId);
                        if (_selectedNodeId == targetId) _selectedNodeId = null;
                    }
                }
            }
            // 修正：這裡移除 SaveToDiskAndRefresh，交由上層 DrawNodeDetails 透過回傳值來處理
        }

        private void CheckAndCleanOrphans()
        {
            var orphans = StoryEditorContext.GetOrphanedNodes(_currentPackage);
            if (orphans.Count == 0)
            {
                EditorUtility.DisplayDialog("Check Orphans", "No orphaned nodes found.", "OK");
                return;
            }

            string msg = $"Found {orphans.Count} orphaned nodes:\n\n- {string.Join("\n- ", orphans.Take(10))}{(orphans.Count > 10 ? "\n..." : "")}\n\nDelete them?";
            if (EditorUtility.DisplayDialog("Found Orphans", msg, "Yes, Delete All", "Cancel"))
            {
                StoryEditorContext.DeleteNodes(_currentPackage, orphans);
                if (orphans.Contains(_selectedNodeId)) _selectedNodeId = null;
                SaveToDiskAndRefresh();
            }
        }

        private void SaveToDiskAndRefresh()
        {
            if (_currentPackage == null) return;
            string path = StoryEditorContext.CurrentFilePath;
            StoryEditorContext.SavePackage(_currentPackage, path);

            if (_currentPackage.Nodes.Count > 0)
            {
                _targetPanel.RefreshStoryLayout(_currentPackage.Nodes[0], _currentPackage);
            }
            else
            {
                _targetPanel.ClearOldLayout();
            }
            EditorUtility.SetDirty(_targetPanel.gameObject);
            SceneView.RepaintAll();
        }

        private void CreateAndLoad()
        {
            var newPackage = StoryEditorContext.CreateDefaultPackage(_chapterId.ToString());
            StoryEditorContext.SavePackage(newPackage, StoryEditorContext.CurrentFilePath);
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