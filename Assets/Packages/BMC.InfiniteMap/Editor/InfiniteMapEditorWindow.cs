using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using InfiniteMap;
using Cysharp.Threading.Tasks;
using InfiniteMap.Proto;
using Google.Protobuf;
using InfiniteMap.Unity; // 加入以使用 EntityGuidFactory

namespace InfiniteMap.Unity.Editor
{
    /// <summary>
    /// 獨立的地圖編輯器基底視窗，處理 Chunk 的基礎設定與存檔管理。
    /// 作為 Base Class，允許其他專案繼承並擴充功能。
    /// </summary>
    public class InfiniteMapEditorWindow : EditorWindow
    {
        // 改為 protected 讓子類別可以存取或連動專案的 MapId
        protected int worldId = 0;
        protected int preGenerateRadius = 2;

        // =========================================================
        // 資料提供介面：供子類別覆寫以接入專案特定的資料源 (例如 Game.Instance)
        // =========================================================
        protected virtual int CurrentChunkSize => 16;
        protected virtual float CurrentTileSize => 1f;
        protected virtual int CurrentLoadRadius => 2;
        protected virtual InfiniteWorldController ActiveController => null;
        protected virtual Vector3 CurrentFocusPosition => Vector3.zero;
        protected virtual bool IsShowChunkGizmos => true;

        protected virtual void OnEnable()
        {
            // 訂閱場景繪製事件，這樣即使沒有特定的 MonoBehavior 也能在 Scene 視窗畫東西
            SceneView.duringSceneGui += OnSceneGUI;
        }

        protected virtual void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        // 改為 protected virtual 讓子類別可以 override 整個視窗的繪製邏輯
        protected virtual void OnGUI()
        {
            GUILayout.Label("無邊際世界 - 基礎地圖編輯器", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawInfiniteMapBaseSettings();
            EditorGUILayout.Space(15);

            DrawInfiniteMapSaveTools();
            EditorGUILayout.Space(20);

            EditorGUILayout.LabelField("地圖編輯功能 (請在子類別中實作)", EditorStyles.boldLabel);
        }

        /// <summary>
        /// 提供給子類別呼叫：繪製世界基礎設定 (Zone ID, 半徑等)
        /// </summary>
        protected void DrawInfiniteMapBaseSettings()
        {
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("無邊際世界設定 (Base)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            worldId = EditorGUILayout.IntField("世界編號 (Zone ID)", worldId);
            preGenerateRadius = EditorGUILayout.IntField("預生成半徑", preGenerateRadius);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 提供給子類別呼叫：繪製存檔管理工具 (生成、開啟資料夾)
        /// </summary>
        protected void DrawInfiniteMapSaveTools()
        {
            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("底層存檔管理工具 (Base)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button($"建立/重置測試世界資料 (Zone_{worldId})", GUILayout.Height(30)))
            {
                GenerateEditorWorld();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(5);

            if (GUILayout.Button("開啟存檔資料夾", GUILayout.Height(25)))
            {
                OpenSaveFolder();
            }
            EditorGUILayout.EndVertical();
        }

        protected virtual void GenerateEditorWorld()
        {
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string dir = Path.Combine(baseDir, $"Zone_{worldId}");

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
                        string filePath = Path.Combine(dir, $"chunk_{worldId}_{cx}_{cy}.bytes");

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
                    Debug.Log($"[MapEditor] 已成功於資料夾 {dir} 建立測試世界: Zone_{worldId} (共 {successCount} 個區塊)");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapEditor] 建立測試世界時發生嚴重錯誤！\n原因: {ex.Message}");
            }
        }

        protected virtual void OpenSaveFolder()
        {
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string path = Path.Combine(baseDir, $"Zone_{worldId}");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }

            EditorUtility.RevealInFinder(path);
        }

        // =========================================================
        // 編輯器專屬：存檔與覆寫邏輯
        // =========================================================

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

        protected async UniTask EditorForceSaveToPathAsync(InfiniteWorldController controller, string editorBasePath)
        {
            if (controller == null) return;
            var activeChunks = controller.GetActiveChunks();
            if (activeChunks == null || activeChunks.Count == 0) return;

            string dir = Path.Combine(editorBasePath, $"Zone_{controller.WorldId}");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<UniTask> saveTasks = new List<UniTask>();
            foreach (var chunk in activeChunks.Values)
            {
                saveTasks.Add(EditorSaveChunkStateAsync(controller, chunk, dir));
            }

            await UniTask.WhenAll(saveTasks);
            Debug.Log($"[MapEditor] 已將 {activeChunks.Count} 個活躍區塊覆寫入開發資料夾: {dir}");
        }

        private async UniTask EditorSaveChunkStateAsync(InfiniteWorldController controller, Chunk chunk, string saveDir)
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
            string fileName = $"chunk_{controller.WorldId}_{chunk.Pos.x}_{chunk.Pos.y}.bytes";
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

        // =========================================================
        // 場景視覺化繪製邏輯 (不再使用 [DrawGizmo])
        // =========================================================

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!IsShowChunkGizmos) return;

            float actualChunkWorldSize = CurrentChunkSize * CurrentTileSize;

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
                            DrawChunkGizmo(kvp.Key.x, kvp.Key.y, actualChunkWorldSize, new Color(0, 1, 0, 0.3f), new Color(0, 1, 0, 0.05f));
                        }
                    }
                }
            }
            else
            {
                Vector3 focusPos = CurrentFocusPosition;

                int pos3X = Mathf.FloorToInt(focusPos.x / CurrentTileSize);
                int pos3Y = Mathf.FloorToInt(focusPos.z / CurrentTileSize);

                int centerCx = pos3X >= 0 ? pos3X / CurrentChunkSize : (pos3X + 1) / CurrentChunkSize - 1;
                int centerCy = pos3Y >= 0 ? pos3Y / CurrentChunkSize : (pos3Y + 1) / CurrentChunkSize - 1;

                int radius = CurrentLoadRadius;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        DrawChunkGizmo(centerCx + dx, centerCy + dy, actualChunkWorldSize, new Color(1, 1, 0, 0.5f), new Color(1, 1, 0, 0.05f));
                    }
                }
            }
        }

        private static void DrawChunkGizmo(int cx, int cy, float size, Color wireColor, Color solidColor)
        {
            Vector3 center = new Vector3(
                cx * size + (size / 2f), 0, cy * size + (size / 2f)
            );

            Handles.color = wireColor;
            Handles.DrawWireCube(center, new Vector3(size, 0.1f, size));

            Handles.color = solidColor;
            Handles.DrawAAConvexPolygon(
                center + new Vector3(-size / 2, 0, -size / 2),
                center + new Vector3(size / 2, 0, -size / 2),
                center + new Vector3(size / 2, 0, size / 2),
                center + new Vector3(-size / 2, 0, size / 2)
            );
        }
    }
}