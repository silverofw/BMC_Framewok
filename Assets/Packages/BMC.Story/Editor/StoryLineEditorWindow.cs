using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Story.Editor
{
    public class StoryLineEditorWindow : EditorWindow
    {
        // 核心狀態
        private int _chapterId = 1;
        private StoryLinePanel _targetPanel;
        private StoryPackage _currentPackage;
        private string _selectedNodeId;

        // UI 狀態
        private Vector2 _nodeListScrollPos;
        private Vector2 _nodeDetailScrollPos;
        private string _searchFilter = "";

        // 樣式定義
        private static class Styles
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
            public static Color EventHeaderColor = new Color(0.25f, 0.25f, 0.25f);
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
            // 關閉視窗時清理備份與場景殘留
            StoryEditorContext.ClearAllBackups(StoryEditorContext.CurrentFilePath);
            if (_targetPanel != null)
            {
                _targetPanel.ClearOldLayout();
                EditorUtility.SetDirty(_targetPanel.gameObject);
            }
        }

        // ===================================================================================
        // Scene View Drawing (視覺化連線)
        // ===================================================================================
        private void OnSceneGUI(SceneView sceneView)
        {
            if (_currentPackage == null || _targetPanel == null) return;

            // 建立節點 ID 對應 Transform 的快取
            // 注意：這裡每次重繪都抓取可能會有效能開銷，但在編輯器模式下通常可接受
            var items = _targetPanel.GetComponentsInChildren<StoryLineItem>();
            if (items == null || items.Length == 0) return;

            var nodeMap = new Dictionary<string, Transform>();
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.NodeID) && !nodeMap.ContainsKey(item.NodeID))
                {
                    nodeMap.Add(item.NodeID, item.transform);
                }
            }

            // 繪製連線
            foreach (var node in _currentPackage.Nodes)
            {
                if (!nodeMap.ContainsKey(node.Id)) continue;

                Vector3 startPos = nodeMap[node.Id].position;
                var targets = StoryEditorContext.GetTargetNodeIds(node).ToList();
                bool isSelected = (node.Id == _selectedNodeId);

                // 節點圓盤顏色邏輯
                if (node.Id == "Start") Handles.color = Color.green;
                else if (targets.Count == 0) Handles.color = Color.red; // 終結點
                else Handles.color = Color.white;

                if (isSelected) Handles.color = Styles.SelectionColor;

                // 繪製節點位置
                Handles.DrawWireDisc(startPos, Vector3.up, 0.5f);
                if (isSelected)
                {
                    Handles.Label(startPos + Vector3.up * 1.0f, $"<color=cyan>Editing:</color> {node.Id}", Styles.SceneLabel);
                }

                // 繪製貝茲曲線
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

        // ===================================================================================
        // Main GUI (編輯器介面)
        // ===================================================================================
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

            if (_currentPackage != null)
            {
                DrawEditorInterface();
            }
            else
            {
                ShowWaitingInterface();
            }
        }

        private void DrawTargetPanelBinding()
        {
            EditorGUI.BeginChangeCheck();
            _targetPanel = (StoryLinePanel)EditorGUILayout.ObjectField("UI Panel (Scene)", _targetPanel, typeof(StoryLinePanel), true);
            if (EditorGUI.EndChangeCheck()) { }

            if (_targetPanel == null)
                EditorGUILayout.HelpBox("請先將場景中的 StoryLinePanel 拖曳至此。", MessageType.Warning);
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
                // 當切換章節 ID 時，重置當前編輯狀態
                StoryEditorContext.CurrentChapterId = _chapterId;
                _currentPackage = null;
                _selectedNodeId = null;
                SceneView.RepaintAll();
            }

            // 顯示當前檔案路徑 (唯讀)
            GUI.enabled = false;
            EditorGUILayout.TextField(StoryEditorContext.CurrentFilePath);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFileOperations()
        {
            bool fileExists = File.Exists(StoryEditorContext.CurrentFilePath);

            EditorGUILayout.BeginHorizontal();

            if (fileExists)
            {
                // 載入按鈕
                GUI.backgroundColor = Styles.SuccessColor;
                if (GUILayout.Button(new GUIContent(" Load & Edit", "載入並生成場景"), GUILayout.Height(30)))
                    LoadAndGenerate();

                // 歷史紀錄按鈕
                GUI.backgroundColor = Styles.BackupColor;
                if (GUILayout.Button(new GUIContent(" History", "查看自動備份"), GUILayout.Height(30), GUILayout.Width(70)))
                    ShowBackupMenu();
            }
            else
            {
                // 創建按鈕
                GUI.backgroundColor = Styles.WarningColor;
                if (GUILayout.Button("Create New Package", GUILayout.Height(30)))
                    CreateAndLoad();
            }

            GUI.backgroundColor = Color.white;

            // 清除場景按鈕 (不影響存檔)
            if (GUILayout.Button("Clear Scene View", GUILayout.Height(30), GUILayout.Width(120)))
                _targetPanel.ClearOldLayout();

            EditorGUILayout.EndHorizontal();
        }

        private void ShowWaitingInterface()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Status: Idle", EditorStyles.centeredGreyMiniLabel);
            if (File.Exists(StoryEditorContext.CurrentFilePath))
                EditorGUILayout.HelpBox($"File found: Chapter {_chapterId}\nClick 'Load & Edit' to begin.", MessageType.Info);
            else
                EditorGUILayout.LabelField("File not found. Create a new one to start.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
        }

        // ===================================================================================
        // Editor Core Interface (左右分割視窗)
        // ===================================================================================
        private void DrawEditorInterface()
        {
            EditorGUILayout.LabelField($"Package Content (Nodes: {_currentPackage.Nodes.Count})", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // Left: Node List
            DrawNodeListSidebar();

            // Right: Node Details
            DrawNodeDetailArea();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNodeListSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(240)); // 稍微加寬

            // 搜尋欄
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                _searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            // 新增節點按鈕
            if (GUILayout.Button("+ Create New Node", GUILayout.Height(25)))
            {
                var newNode = StoryEditorContext.CreateNewNode(_currentPackage);
                _selectedNodeId = newNode.Id;
                SaveToDiskAndRefresh();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(5);

            // 節點列表 Scroll View
            _nodeListScrollPos = EditorGUILayout.BeginScrollView(_nodeListScrollPos, "box", GUILayout.ExpandHeight(true));

            foreach (var node in _currentPackage.Nodes)
            {
                // 搜尋過濾
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    if (node.Id.IndexOf(_searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                // 繪製列表項目
                if (_selectedNodeId == node.Id) GUI.backgroundColor = Styles.SelectionColor;

                string displayName = string.IsNullOrEmpty(node.Id) ? "[Empty ID]" : node.Id;
                if (GUILayout.Button(displayName, EditorStyles.miniButton, GUILayout.Height(24)))
                {
                    _selectedNodeId = node.Id;
                    GUI.FocusControl(null); // 取消輸入框焦點，避免誤觸
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndScrollView();

            // 孤兒節點檢查
            EditorGUILayout.Space();
            GUI.backgroundColor = Styles.WarningColor;
            if (GUILayout.Button("Cleanup Orphans", GUILayout.Height(25))) CheckAndCleanOrphans();
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
                    EditorGUILayout.HelpBox("Error: Selected node does not exist.", MessageType.Error);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a node from the left list to edit properties.", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ===================================================================================
        // Node Property Drawing (屬性編輯邏輯)
        // ===================================================================================
        private void DrawNodeDetails(StoryNode node)
        {
            bool isDirty = false;

            // 1. ID 與 基本操作
            DrawNodeHeader(node, ref isDirty);
            if (_selectedNodeId == null) return; // 節點被刪除後直接返回

            EditorGUILayout.Space();

            // 2. Auto Jump 設定
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("Auto Jump Logic", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            node.AutoJumpDelay = EditorGUILayout.FloatField("Delay (Seconds)", node.AutoJumpDelay);
            if (EditorGUI.EndChangeCheck()) isDirty = true;

            DrawTargetIdSelector("Jump Target", () => node.AutoJumpNodeId, (val) => node.AutoJumpNodeId = val, node);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Events Pipeline (Execute sequentially)", EditorStyles.boldLabel);

            // 3. 事件列表
            for (int i = 0; i < node.OnEnterEvents.Count; i++)
            {
                var evt = node.OnEnterEvents[i];
                EditorGUILayout.BeginVertical("box");

                // Event Toolbar (Title + Move + Delete)
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label($"#{i + 1}  {evt.ActionCase}", EditorStyles.miniLabel);

                // 上移
                if (i > 0 && GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (node.OnEnterEvents[i], node.OnEnterEvents[i - 1]) = (node.OnEnterEvents[i - 1], node.OnEnterEvents[i]);
                    isDirty = true;
                }
                // 下移
                if (i < node.OnEnterEvents.Count - 1 && GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    (node.OnEnterEvents[i], node.OnEnterEvents[i + 1]) = (node.OnEnterEvents[i + 1], node.OnEnterEvents[i]);
                    isDirty = true;
                }
                // 刪除
                GUI.backgroundColor = Styles.ErrorColor;
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(25)))
                {
                    node.OnEnterEvents.RemoveAt(i);
                    isDirty = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break; // 中斷迴圈避免 Index Error
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                // Event Content
                EditorGUI.BeginChangeCheck();
                evt.DelaySeconds = EditorGUILayout.FloatField("Pre-Delay", evt.DelaySeconds);
                if (EditorGUI.EndChangeCheck()) isDirty = true;

                if (DrawEventContent(node, evt)) isDirty = true;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            // 4. 新增事件按鈕區
            EditorGUILayout.Space();
            DrawAddEventButtons(node, ref isDirty);

            // 統一存檔
            if (isDirty) SaveToDiskAndRefresh();
        }

        private void DrawAddEventButtons(StoryNode node, ref bool isDirty)
        {
            EditorGUILayout.LabelField("Add Event:", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Media (Video)", EditorStyles.miniButtonLeft)) { node.OnEnterEvents.Add(new StoryEvent { PlayVideo = new PlayVideoAction() }); isDirty = true; }
            if (GUILayout.Button("Media (BGM)", EditorStyles.miniButtonMid)) { node.OnEnterEvents.Add(new StoryEvent { PlayBgm = new PlayBackgroundMusicAction() }); isDirty = true; }
            if (GUILayout.Button("Media (SFX)", EditorStyles.miniButtonMid)) { node.OnEnterEvents.Add(new StoryEvent { PlaySfx = new PlaySoundEffectAction() }); isDirty = true; }
            if (GUILayout.Button("Media (Voice)", EditorStyles.miniButtonRight)) { node.OnEnterEvents.Add(new StoryEvent { PlayVoice = new PlayVoiceAction() }); isDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Logic (Stat)", EditorStyles.miniButtonLeft)) { node.OnEnterEvents.Add(new StoryEvent { UpdateStat = new UpdateCharacterStatAction() }); isDirty = true; }
            if (GUILayout.Button("Logic (Var)", EditorStyles.miniButtonMid)) { node.OnEnterEvents.Add(new StoryEvent { SetVariable = new SetVariableAction() }); isDirty = true; }
            GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
            if (GUILayout.Button("UI (Choices)", EditorStyles.miniButtonRight))
            {
                var act = new ShowChoicesAction();
                act.Choices.Add(new Choice { Text = "Option 1" });
                node.OnEnterEvents.Add(new StoryEvent { ShowChoices = act });
                isDirty = true;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Game (Dice)", EditorStyles.miniButtonLeft)) { node.OnEnterEvents.Add(new StoryEvent { GameDice = new GameDiceRollAction() }); isDirty = true; }
            if (GUILayout.Button("Game (Roulette)", EditorStyles.miniButtonMid)) { node.OnEnterEvents.Add(new StoryEvent { GameRussianRoulette = new GameRussianRouletteAction() }); isDirty = true; }
            if (GUILayout.Button("Game (QTE)", EditorStyles.miniButtonRight)) { node.OnEnterEvents.Add(new StoryEvent { GameQte = new GameQTEAction() }); isDirty = true; }
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawEventContent(StoryNode parentNode, StoryEvent evt)
        {
            if (evt.ActionCase == StoryEvent.ActionOneofCase.ShowChoices)
            {
                return DrawChoicesEditor(parentNode, evt.ShowChoices);
            }

            bool changed = false;
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
                    EditorGUILayout.BeginHorizontal();
                    evt.GameDice.DiceCount = EditorGUILayout.IntField("Count", evt.GameDice.DiceCount);
                    evt.GameDice.DiceFaces = EditorGUILayout.IntField("Faces", evt.GameDice.DiceFaces);
                    EditorGUILayout.EndHorizontal();
                    evt.GameDice.TargetValue = EditorGUILayout.IntField("Target >=", evt.GameDice.TargetValue);
                    DrawTargetIdSelector("Success ->", () => evt.GameDice.SuccessNodeId, (val) => evt.GameDice.SuccessNodeId = val);
                    DrawTargetIdSelector("Fail ->", () => evt.GameDice.FailNodeId, (val) => evt.GameDice.FailNodeId = val);
                    break;

                case StoryEvent.ActionOneofCase.GameRussianRoulette:
                    EditorGUILayout.BeginHorizontal();
                    evt.GameRussianRoulette.PlayerHp = EditorGUILayout.IntField("Player HP", evt.GameRussianRoulette.PlayerHp);
                    evt.GameRussianRoulette.OpponentHp = EditorGUILayout.IntField("Enemy HP", evt.GameRussianRoulette.OpponentHp);
                    EditorGUILayout.EndHorizontal();
                    DrawTargetIdSelector("Win ->", () => evt.GameRussianRoulette.WinNodeId, (val) => evt.GameRussianRoulette.WinNodeId = val);
                    DrawTargetIdSelector("Lose ->", () => evt.GameRussianRoulette.LoseNodeId, (val) => evt.GameRussianRoulette.LoseNodeId = val);
                    break;

                case StoryEvent.ActionOneofCase.GameQte:
                    evt.GameQte.Type = (GameQTEAction.Types.QTEType)EditorGUILayout.EnumPopup("Type", evt.GameQte.Type);
                    evt.GameQte.DurationSeconds = EditorGUILayout.FloatField("Time Limit", evt.GameQte.DurationSeconds);
                    DrawTargetIdSelector("Success ->", () => evt.GameQte.SuccessNodeId, (val) => evt.GameQte.SuccessNodeId = val);
                    DrawTargetIdSelector("Fail ->", () => evt.GameQte.FailNodeId, (val) => evt.GameQte.FailNodeId = val);
                    break;
            }

            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }

        private bool DrawChoicesEditor(StoryNode parentNode, ShowChoicesAction action)
        {
            bool changed = false;
            EditorGUILayout.LabelField($"Options ({action.Choices.Count})", EditorStyles.miniBoldLabel);

            for (int i = 0; i < action.Choices.Count; i++)
            {
                var choice = action.Choices[i];
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(25));

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

            if (GUILayout.Button("+ Add Choice"))
            {
                string nextId = StoryEditorContext.GenerateUniqueNextID(_currentPackage, parentNode);
                action.Choices.Add(new Choice { Text = "New Option", TargetNodeId = nextId });
                changed = true;
            }

            return changed;
        }

        // ===================================================================================
        // Helper Components (選擇器與對話框)
        // ===================================================================================

        private void DrawTargetIdSelector(string label, System.Func<string> getter, System.Action<string> setter, StoryNode parentForGen = null)
        {
            EditorGUILayout.BeginHorizontal();
            string current = getter();
            string newVal = EditorGUILayout.DelayedTextField(label, current);

            if (newVal != current) setter(newVal);

            // 狀態檢查
            bool exists = !string.IsNullOrEmpty(newVal) && _currentPackage.Nodes.Any(n => n.Id == newVal);
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
                    StoryEditorContext.CreateSpecificNode(_currentPackage, newVal);
                    SaveToDiskAndRefresh();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (parentForGen != null && isEmpty)
            {
                // 自動生成建議 ID
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
            EditorGUILayout.LabelField($"Node ID: {node.Id}", EditorStyles.boldLabel);

            // 刪除按鈕
            GUI.backgroundColor = Styles.ErrorColor;
            if (GUILayout.Button("Delete Node", GUILayout.Width(90)))
            {
                if (EditorUtility.DisplayDialog("Confirm Delete", $"Are you sure you want to delete node '{node.Id}'?\nThis will clear references in other nodes.", "Delete", "Cancel"))
                {
                    StoryEditorContext.DeleteNodeAndCleanReferences(_currentPackage, node.Id);
                    _selectedNodeId = null;
                    SaveToDiskAndRefresh();
                    GUIUtility.ExitGUI();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // 重新命名
            string newId = EditorGUILayout.DelayedTextField("Rename ID", node.Id);
            if (newId != node.Id && !string.IsNullOrEmpty(newId))
            {
                if (_currentPackage.Nodes.Any(n => n.Id == newId))
                    EditorUtility.DisplayDialog("Rename Failed", $"ID '{newId}' already exists.", "OK");
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
            if (orphans.Count == 0)
            {
                EditorUtility.DisplayDialog("Check Complete", "No orphan nodes found (All nodes are reachable from Start).", "OK");
                return;
            }

            if (EditorUtility.DisplayDialog("Orphans Found", $"Found {orphans.Count} unreachable nodes.\nDo you want to delete them?", "Delete All", "Cancel"))
            {
                StoryEditorContext.DeleteNodes(_currentPackage, orphans);
                if (orphans.Contains(_selectedNodeId)) _selectedNodeId = null;
                SaveToDiskAndRefresh();
            }
        }

        // ===================================================================================
        // I/O & Refresh
        // ===================================================================================

        private void SaveToDiskAndRefresh()
        {
            if (_currentPackage == null) return;
            StoryEditorContext.SavePackage(_currentPackage, StoryEditorContext.CurrentFilePath);

            if (_targetPanel != null)
            {
                if (_currentPackage.Nodes.Count > 0)
                    _targetPanel.RefreshStoryLayout(_currentPackage.Nodes[0], _currentPackage);
                else
                    _targetPanel.ClearOldLayout();

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

            _currentPackage = StoryEditorContext.LoadPackage(StoryEditorContext.CurrentFilePath);
            if (_currentPackage != null && _currentPackage.Nodes.Count > 0)
            {
                _targetPanel.RefreshStoryLayout(_currentPackage.Nodes[0], _currentPackage);
                EditorUtility.SetDirty(_targetPanel.gameObject);
                Repaint(); // 刷新編輯器視窗
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