#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using InfiniteMap.Proto;
using Google.Protobuf;

namespace InfiniteMap.Unity.Editor
{
    /// <summary>
    /// 獨立的地圖編輯器基底視窗，處理 Chunk 的基礎設定與存檔管理。
    /// 作為 Base Class，允許其他專案繼承並擴充功能。
    /// </summary>
    public class InfiniteMapEditorWindow : EditorWindow
    {
        protected int worldId = 0;
        protected int preGenerateRadius = 2;

        // 移轉至基底的加載中心參數
        protected int targetPosX = 0;
        protected int targetPosY = 0;

        protected virtual int CurrentChunkSize => 16;
        protected virtual float CurrentTileSize => 1f;
        protected virtual int CurrentLoadRadius => 2;
        protected virtual InfiniteWorldController ActiveController => null;
        protected virtual Vector3 CurrentFocusPosition => Vector3.zero;
        protected virtual bool IsShowChunkGizmos => true;

        protected virtual void OnEnable()
        {
            UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
        }

        protected virtual void OnDisable()
        {
            UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
        }

        protected virtual void OnGUI()
        {
            GUILayout.Label("無邊際世界 - 基礎地圖編輯器", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawInfiniteMapBaseSettings();
            EditorGUILayout.Space(15);

            GUILayout.Label("Data & Save Management", EditorStyles.boldLabel);
            DrawInfiniteMapSaveTools();
            EditorGUILayout.Space(20);

            EditorGUILayout.LabelField("地圖編輯功能 (請在子類別中實作)", EditorStyles.boldLabel);
        }

        protected void DrawInfiniteMapBaseSettings()
        {
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("無邊際世界設定 (Base)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            worldId = EditorGUILayout.IntField("世界編號 (Zone ID)", worldId);
            preGenerateRadius = EditorGUILayout.IntField("預生成半徑", preGenerateRadius);

            EditorGUILayout.Space(5);

            // ================================================================
            // 已優化：手動加載中心 (Tick) 縮減成單行排版
            // ================================================================
            EditorGUILayout.BeginHorizontal();

            // 暫時重置縮進，防止水平排版時前方留白過多
            int originalIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUILayout.LabelField("手動 Tick:", GUILayout.Width(65));

            // 縮短輸入框前文字 (X / Y) 的佔用寬度，使數據能緊湊顯示
            float originalLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 15;

            targetPosX = EditorGUILayout.IntField("X", targetPosX, GUILayout.Width(65));
            targetPosY = EditorGUILayout.IntField("Y", targetPosY, GUILayout.Width(65));

            // 還原預設標籤寬度
            EditorGUIUtility.labelWidth = originalLabelWidth;

            GUILayout.Space(5);

            if (GUILayout.Button("更新中心 (Tick)", GUILayout.Height(18), GUILayout.ExpandWidth(true)))
            {
                ExecuteBaseTick();
            }

            // 還原原本的縮進
            EditorGUI.indentLevel = originalIndent;
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 執行手動加載的基礎核心邏輯，子類別可覆寫以擴充高度(H)或專案檢查。
        /// </summary>
        protected virtual void ExecuteBaseTick()
        {
            if (Application.isPlaying && ActiveController != null)
            {
                // 改為對應 XY 平面，Z 為 0 (或高度)
                ActiveController.UpdateWorldFocus(new Vector3(targetPosX * CurrentTileSize, targetPosY * CurrentTileSize, 0));
                Debug.Log($"[Map Editor] 已更新無邊際地圖加載中心為: [{targetPosX}, {targetPosY}]");
            }
            else if (!Application.isPlaying)
            {
                Debug.LogWarning("[Map Editor] 請在 Play Mode 下點擊此按鈕以加載地圖區塊。");
            }
        }

        // ============================================
        // 底層存檔管理工具 (支援一排橫向顯示所有功能)
        // ============================================
        protected void DrawInfiniteMapSaveTools(int targetZoneId = -1)
        {
            int zoneId = targetZoneId == -1 ? worldId : targetZoneId;

            EditorGUILayout.BeginHorizontal("helpbox");

            // 1. 初始化世界
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button("INIT ZONE", GUILayout.Height(25)))
            {
                GenerateEditorWorld(zoneId);
            }
            GUI.backgroundColor = Color.white;

            // 2. 開啟 Map 資料夾
            if (GUILayout.Button("OPEN MAP DIR", GUILayout.Height(25)))
            {
                OpenSaveFolder(zoneId);
            }

            // 3. 清除 Map 資料 (遊戲運行時禁用)
            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            GUI.color = Application.isPlaying ? Color.gray : new Color(1f, 0.6f, 0.2f);
            if (GUILayout.Button(Application.isPlaying ? "運行中不可刪除" : "CLEAR MAP", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("確認清空", $"確定要清空 Map {zoneId} 的所有編輯存檔嗎？此動作將刪除該 Zone 下的所有 Chunk 資料。", "確定清空", "取消"))
                {
                    ClearCurrentMapSaves(zoneId);
                }
            }
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            // 4. 開啟 Player 存檔資料夾
            if (GUILayout.Button("OPEN PLAYER DIR", GUILayout.Height(25)))
            {
                OpenPlayerSavesDirectory();
            }

            // 5. 清除全部 Player 存檔 (遊戲運行時禁用)
            EditorGUI.BeginDisabledGroup(Application.isPlaying);
            GUI.color = Application.isPlaying ? Color.gray : new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button(Application.isPlaying ? "運行中不可刪除" : "CLEAR PLAYER", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("確認刪除全部紀錄", $"確定要刪除玩家的所有遊玩紀錄地圖資料嗎？", "確定刪除", "取消"))
                {
                    ClearAllPlayerSaves();
                }
            }
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        protected virtual void GenerateEditorWorld(int targetZoneId = -1)
        {
            int zoneId = targetZoneId == -1 ? worldId : targetZoneId;
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string dir = Path.Combine(baseDir, $"Zone_{zoneId}");

            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    Debug.Log($"[MapEditor] 成功建立目錄: {dir}");
                }

                int successCount = 0;
                int totalCount = (preGenerateRadius * 2 + 1) * (preGenerateRadius * 2 + 1);

                for (int cx = -preGenerateRadius; cx <= preGenerateRadius; cx++)
                {
                    for (int cy = -preGenerateRadius; cy <= preGenerateRadius; cy++)
                    {
                        string filePath = Path.Combine(dir, $"chunk_{zoneId}_{cx}_{cy}.bytes");

                        try
                        {
                            ChunkProto proto = new ChunkProto
                            {
                                Cx = cx,
                                Cy = cy,
                                LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            };

                            byte[] mockData = proto.ToByteArray();
                            File.WriteAllBytes(filePath, mockData);
                            successCount++;
                        }
                        catch (System.Exception writeEx)
                        {
                            Debug.LogError($"[MapEditor] 建立 Chunk ({cx}, {cy}) 失敗！\n路徑: {filePath}\n錯誤原因: {writeEx.Message}");
                        }
                    }
                }

                AssetDatabase.Refresh();

                if (successCount == totalCount)
                {
                    Debug.Log($"[MapEditor] 已成功於資料夾 {dir} 建立測試世界: Zone_{zoneId} (共 {successCount} 個區塊)");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapEditor] 建立測試世界時發生嚴重錯誤！\n原因: {ex.Message}");
            }
        }

        protected virtual void OpenSaveFolder(int targetZoneId = -1)
        {
            int zoneId = targetZoneId == -1 ? worldId : targetZoneId;
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string path = Path.Combine(baseDir, $"Zone_{zoneId}");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }

            EditorUtility.RevealInFinder(path);
        }

        protected virtual void ClearCurrentMapSaves(int zoneId)
        {
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap", $"Zone_{zoneId}");
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
                Debug.Log($"[MapEditor] 已成功清除 {baseDir} 下的所有存檔！");
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.LogWarning($"[MapEditor] 找不到存檔路徑: {baseDir}，無資料可清除。");
            }
        }

        protected virtual void ClearAllPlayerSaves()
        {
            string playerSavesDir = Path.Combine(Application.persistentDataPath, "WorldSaves");
            if (Directory.Exists(playerSavesDir))
            {
                Directory.Delete(playerSavesDir, true);
                Debug.Log($"[MapEditor] 已成功清除所有玩家遊玩紀錄！路徑：{playerSavesDir}");
            }
            else
            {
                Debug.LogWarning($"[MapEditor] 找不到玩家存檔路徑: {playerSavesDir}，無資料可清除。");
            }
        }

        protected virtual void OpenPlayerSavesDirectory()
        {
            string playerSavesDir = Path.Combine(Application.persistentDataPath, "WorldSaves");
            if (!Directory.Exists(playerSavesDir)) Directory.CreateDirectory(playerSavesDir);
            EditorUtility.RevealInFinder(playerSavesDir);
        }

        protected void SyncMaxStaticGuidFromRawPath(int zoneId)
        {
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string dir = Path.Combine(baseDir, $"Zone_{zoneId}");
            long maxGuid = 0;

            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "chunk_*.bytes");
                foreach (var file in files)
                {
                    try
                    {
                        byte[] data = File.ReadAllBytes(file);
                        ChunkProto proto = ChunkProto.Parser.ParseFrom(data);
                        foreach (var ent in proto.Entities)
                        {
                            if (EntityGuidFactory.IsStaticGuid(ent.Guid) && ent.Guid > maxGuid)
                            {
                                maxGuid = ent.Guid;
                            }
                        }
                    }
                    catch { }
                }
            }

            EntityGuidFactory.SetStaticCounter(maxGuid);
            Debug.Log($"[MapEditor] 已從 {dir} 恢復全域最大靜態 GUID 至: {maxGuid}");
        }

        // 核心修改：新增 targetZoneId 參數，以接收 MapEditorWindow 正確指定的 MapId
        protected async UniTask EditorForceSaveToPathAsync(InfiniteWorldController controller, string editorBasePath, int targetZoneId = -1)
        {
            if (controller == null) return;
            var activeChunks = controller.GetActiveChunks();
            if (activeChunks == null || activeChunks.Count == 0) return;

            // 如果有指定 targetZoneId (如編輯器的 MapId)，則強制使用，否則退回使用 Controller.WorldId
            int zoneId = targetZoneId != -1 ? targetZoneId : controller.WorldId;

            string dir = Path.Combine(editorBasePath, $"Zone_{zoneId}");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<UniTask> saveTasks = new List<UniTask>();
            foreach (var chunk in activeChunks.Values)
            {
                // 將 zoneId 傳遞進單塊地圖的存檔邏輯中
                saveTasks.Add(EditorSaveChunkStateAsync(controller, chunk, dir, zoneId));
            }

            await UniTask.WhenAll(saveTasks);
            Debug.Log($"[MapEditor] 已將 {activeChunks.Count} 個活躍區塊覆寫入開發資料夾: {dir}");
        }

        // 核心修改：新增 zoneId 參數，強制規範 chunk 檔名與目錄匹配
        private async UniTask EditorSaveChunkStateAsync(InfiniteWorldController controller, Chunk chunk, string saveDir, int zoneId)
        {
            ChunkProto proto = new ChunkProto
            {
                Cx = chunk.Pos.x,
                Cy = chunk.Pos.y,
                LastTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            HashSet<long> entityGuids = new HashSet<long>(chunk.Entities);

            foreach (long guid in entityGuids)
            {
                EntityProto latestState = controller.OnEntitySerialize?.Invoke(guid);
                if (latestState != null)
                {
                    proto.Entities.Add(latestState);
                }
            }

            byte[] dataToSave = proto.ToByteArray();

            // 核心修改：將 controller.WorldId 替換為傳入的專用 zoneId 組合檔名
            string fileName = $"chunk_{zoneId}_{chunk.Pos.x}_{chunk.Pos.y}.bytes";
            string filePath = Path.Combine(saveDir, fileName);

            try
            {
                File.WriteAllBytes(filePath, dataToSave);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MapEditor] 寫入存檔發生錯誤: {e.Message}");
            }

            await UniTask.Yield();
        }

        private void OnSceneGUI(UnityEditor.SceneView sceneView)
        {
            if (!IsShowChunkGizmos) return;

            int chunkSize = CurrentChunkSize;
            float tileSize = CurrentTileSize;

            if (Application.isPlaying)
            {
                var controller = ActiveController;
                if (controller != null)
                {
                    var activeChunks = controller.GetActiveChunks();
                    if (activeChunks != null)
                    {
                        foreach (var kvp in activeChunks)
                        {
                            DrawChunkGizmo(kvp.Key.x, kvp.Key.y, chunkSize, tileSize, new Color(0, 1, 0, 0.3f), new Color(0, 1, 0, 0.05f));
                        }
                    }
                }
            }
            else
            {
                Vector3 focusPos = CurrentFocusPosition;

                int pos3X = Mathf.FloorToInt(focusPos.x / tileSize + 0.5f);
                // 修改為讀取 y 軸計算高度 (XY 平面)
                int pos3Y = Mathf.FloorToInt(focusPos.y / tileSize + 0.5f);

                int centerCx = pos3X >= 0 ? pos3X / chunkSize : (pos3X + 1) / chunkSize - 1;
                int centerCy = pos3Y >= 0 ? pos3Y / chunkSize : (pos3Y + 1) / chunkSize - 1;

                int radius = CurrentLoadRadius;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        DrawChunkGizmo(centerCx + dx, centerCy + dy, chunkSize, tileSize, new Color(1, 1, 0, 0.5f), new Color(1, 1, 0, 0.05f));
                    }
                }
            }
        }

        private static void DrawChunkGizmo(int cx, int cy, int chunkSize, float tileSize, Color wireColor, Color solidColor)
        {
            float worldSize = chunkSize * tileSize;
            // 替換為在 XY 平面上的繪製位置 (原為 XZ 平面)
            Vector3 center = new Vector3(
                (cx * chunkSize + (chunkSize - 1) / 2f) * tileSize,
                (cy * chunkSize + (chunkSize - 1) / 2f) * tileSize,
                0
            );

            Handles.color = wireColor;
            Handles.DrawWireCube(center, new Vector3(worldSize, worldSize, 0.1f));

            Handles.color = solidColor;
            Handles.DrawAAConvexPolygon(
                center + new Vector3(-worldSize / 2, -worldSize / 2, 0),
                center + new Vector3(worldSize / 2, -worldSize / 2, 0),
                center + new Vector3(worldSize / 2, worldSize / 2, 0),
                center + new Vector3(-worldSize / 2, worldSize / 2, 0)
            );
        }
    }
}
#endif