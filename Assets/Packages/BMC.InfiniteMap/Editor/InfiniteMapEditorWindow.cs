using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using InfiniteMap;

namespace InfiniteMap.Unity.Editor
{
    /// <summary>
    /// 獨立的地圖編輯器視窗，後續可用於視覺化編輯 Chunk 資料
    /// </summary>
    public class InfiniteMapEditorWindow : EditorWindow
    {
        private int worldId = 0;
        private int preGenerateRadius = 2;

        // 在上方工具列加入開啟此視窗的選單
        [MenuItem("BMC/Infinite Map Editor (無邊際地圖編輯器)", false, 1)]
        public static void ShowWindow()
        {
            // 建立並顯示視窗
            GetWindow<InfiniteMapEditorWindow>("地圖編輯器");
        }

        private void OnGUI()
        {
            GUILayout.Label("無邊際世界 - 地圖編輯器", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // === 基礎設定區塊 ===
            EditorGUILayout.LabelField("世界設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            worldId = EditorGUILayout.IntField("世界編號 (Zone ID)", worldId);
            preGenerateRadius = EditorGUILayout.IntField("預生成半徑", preGenerateRadius);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(15);

            // === 存檔管理區塊 ===
            EditorGUILayout.LabelField("存檔管理工具", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button($"建立/重置測試世界資料 (Zone_{worldId})", GUILayout.Height(30)))
            {
                GenerateEditorWorld();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space();

            if (GUILayout.Button("開啟存檔資料夾", GUILayout.Height(25)))
            {
                OpenSaveFolder();
            }

            EditorGUILayout.Space(20);

            // === 未來地圖編輯區 ===
            EditorGUILayout.LabelField("地圖編輯功能 (開發中...)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("未來可以在這裡加入畫筆工具、地塊類型選擇，並直接點擊 Scene 視窗來修改特定 Chunk 的資料。", MessageType.Info);
        }

        private void GenerateEditorWorld()
        {
            // 將輸出路徑改為 Assets/yoo/DefaultPackage/Proto/InfiniteMap/Zone_{worldId}
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string dir = Path.Combine(baseDir, $"Zone_{worldId}");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 預先生成玩家周圍的空白/基礎 Chunk 資料
            for (int cx = -preGenerateRadius; cx <= preGenerateRadius; cx++)
            {
                for (int cy = -preGenerateRadius; cy <= preGenerateRadius; cy++)
                {
                    // 在檔案名稱加上 worldId 確保 YooAsset 資源名稱唯一
                    string filePath = Path.Combine(dir, $"chunk_{worldId}_{cx}_{cy}.bytes");

                    // TODO: 未來這裡可替換成您建立 Proto 物件並 ToByteArray() 的實際預設地形邏輯
                    byte[] mockData = new byte[16];

                    File.WriteAllBytes(filePath, mockData);
                }
            }

            // 刷新 AssetDatabase，確保 Unity 編輯器與 YooAsset 能立刻偵測到新產生的資源
            AssetDatabase.Refresh();

            Debug.Log($"[MapEditor] 已成功於資料夾 {dir} 建立預設測試世界: Zone_{worldId} (共 {(preGenerateRadius * 2 + 1) * (preGenerateRadius * 2 + 1)} 個區塊)");
        }

        private void OpenSaveFolder()
        {
            string baseDir = Path.Combine(Application.dataPath, "yoo", "DefaultPackage", "Proto", "InfiniteMap");
            string path = Path.Combine(baseDir, $"Zone_{worldId}");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }

            // 在檔案總管 (Windows) 或 Finder (Mac) 中開啟資料夾
            EditorUtility.RevealInFinder(path);
        }

        // =========================================================
        // 場景視覺化除錯工具：繪製目前活耀的 Chunk 邊界
        // =========================================================

        /// <summary>
        /// 利用 DrawGizmo 屬性，在不修改 Manager 腳本的前提下，從外部為它繪製 Gizmos
        /// 注意：簽章已更新為接收 InfiniteWorldMono
        /// </summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        static void DrawWorldGizmos(InfiniteWorldMono manager, GizmoType gizmoType)
        {
            // 如果地圖編輯器視窗沒有開啟，就不繪製 Gizmos
            if (!HasOpenInstances<InfiniteMapEditorWindow>()) return;

            if (!manager.showChunkGizmos) return;

            float actualChunkWorldSize = manager.chunkSize * manager.tileSize;

            // 1. 如果遊戲執行中，畫出真實載入的 Chunk (綠色)
            if (Application.isPlaying)
            {
                if (manager.Controller != null)
                {
                    // 呼叫我們在 Controller 中新增的開放介面取得當生活躍區塊
                    var activeChunks = manager.Controller.GetActiveChunks();
                    if (activeChunks != null)
                    {
                        foreach (var kvp in activeChunks)
                        {
                            DrawChunkGizmo(kvp.Key.x, kvp.Key.y, actualChunkWorldSize, new Color(0, 1, 0, 0.3f), new Color(0, 1, 0, 0.05f));
                        }
                    }
                }
            }
            // 2. 如果在編輯器未執行狀態，畫出「預期」會載入的範圍預覽 (黃色)
            else
            {
                // 在編輯模式下，由於尚未呼叫 Tick()，CurrentFocusPosition 可能是零。
                // 為了方便視覺化，我們使用 Manager 自身物件的 Transform 座標作為預覽中心！
                Vector3 focusPos = manager.CurrentFocusPosition == Vector3.zero
                                    ? manager.transform.position
                                    : manager.CurrentFocusPosition;

                int pos3X = Mathf.FloorToInt(focusPos.x / manager.tileSize);
                int pos3Y = Mathf.FloorToInt(focusPos.z / manager.tileSize);

                int centerCx = pos3X >= 0 ? pos3X / manager.chunkSize : (pos3X + 1) / manager.chunkSize - 1;
                int centerCy = pos3Y >= 0 ? pos3Y / manager.chunkSize : (pos3Y + 1) / manager.chunkSize - 1;

                for (int dx = -manager.loadRadius; dx <= manager.loadRadius; dx++)
                {
                    for (int dy = -manager.loadRadius; dy <= manager.loadRadius; dy++)
                    {
                        DrawChunkGizmo(centerCx + dx, centerCy + dy, actualChunkWorldSize, new Color(1, 1, 0, 0.5f), new Color(1, 1, 0, 0.05f));
                    }
                }
            }
        }

        static void DrawChunkGizmo(int cx, int cy, float size, Color wireColor, Color solidColor)
        {
            Vector3 center = new Vector3(
                cx * size + (size / 2f),
                0, // 基準高度設為 0
                cy * size + (size / 2f)
            );

            Gizmos.color = wireColor;
            Gizmos.DrawWireCube(center, new Vector3(size, 0.1f, size));

            Gizmos.color = solidColor;
            Gizmos.DrawCube(center, new Vector3(size, 0.1f, size));
        }
    }
}