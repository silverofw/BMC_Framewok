using Unity.Plastic.Newtonsoft.Json;
using System.IO;
using UnityEditor;
using UnityEngine;
using BMC.AI;

[CustomEditor(typeof(AgentPanel))]
public class AgentPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 繪製 AgentPanel 原本所有的 Inspector 欄位
        DrawDefaultInspector();

        AgentPanel panel = (AgentPanel)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("JSON 情境檔管理 (Context Presets)", EditorStyles.boldLabel);

        // --- 載入 JSON 按鈕 ---
        if (GUILayout.Button("從 JSON 載入情境設定 (Load from JSON)", GUILayout.Height(30)))
        {
            if (panel.contextConfigJson != null)
            {
                // 註冊 Undo 操作，讓使用者如果不小心蓋掉可以 Ctrl+Z 復原
                Undo.RecordObject(panel, "Load Contexts from JSON");
                try
                {
                    var loadedPresets = JsonConvert.DeserializeObject<PromptExtensionConfig[]>(panel.contextConfigJson.text);
                    if (loadedPresets != null && loadedPresets.Length > 0)
                    {
                        panel.contextPresets = loadedPresets;
                        Debug.Log($"<color=green>[AgentPanel Editor]</color> 成功從 JSON 載入 {panel.contextPresets.Length} 組情境設定！");

                        // 標記腳本已被修改，確保存檔時會保留變更
                        EditorUtility.SetDirty(panel);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"<color=red>[AgentPanel Editor]</color> JSON 設定檔解析失敗: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("<color=yellow>[AgentPanel Editor]</color> 請先拖曳 JSON 檔案至上方 [Context Config Json] 欄位！");
            }
        }

        EditorGUILayout.Space(5);

        // --- 匯出 JSON 按鈕 ---
        if (GUILayout.Button("匯出當前情境為 JSON (Export to JSON)", GUILayout.Height(30)))
        {
            try
            {
                string json = JsonConvert.SerializeObject(panel.contextPresets, Formatting.Indented);

                // 開啟 Unity 原生存檔視窗，讓使用者決定要存在哪裡
                string path = EditorUtility.SaveFilePanel(
                    "匯出情境設定檔",
                    Application.dataPath,
                    "AgentContextPresets",
                    "json");

                if (!string.IsNullOrEmpty(path))
                {
                    File.WriteAllText(path, json);
                    AssetDatabase.Refresh(); // 重新整理 Project 視窗以顯示新檔案
                    Debug.Log($"<color=green>[AgentPanel Editor]</color> 成功匯出 JSON 至: {path}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<color=red>[AgentPanel Editor]</color> 匯出 JSON 失敗: {ex.Message}");
            }
        }

        // --- 儲存設定按鈕 (無條件顯示) ---
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("資料儲存管理 (Data Management)", EditorStyles.boldLabel);

        if (GUILayout.Button("將當前設定強制寫入儲存 (Save Settings)", GUILayout.Height(30)))
        {
            try
            {
                // 無論如何先標記腳本已被修改，確保資料能被 Unity 捕捉
                EditorUtility.SetDirty(panel);

                // 自動判斷當前物件的狀態並執行對應的儲存邏輯
                if (PrefabUtility.IsPartOfPrefabInstance(panel.gameObject))
                {
                    // 情況 A：它是場景中的 Prefab，精準把特定屬性推回 Prefab 母檔
                    string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject);
                    if (!string.IsNullOrEmpty(prefabPath))
                    {
                        SerializedObject so = new SerializedObject(panel);

                        // 精準寫入：只把 activeContextIndex 和 JSON 相關陣列寫入 Prefab (預設)，避免覆蓋到場景專屬的 UI 參考
                        SerializedProperty activeIdxProp = so.FindProperty("activeContextIndex");
                        if (activeIdxProp != null) PrefabUtility.ApplyPropertyOverride(activeIdxProp, prefabPath, InteractionMode.UserAction);

                        SerializedProperty presetsProp = so.FindProperty("contextPresets");
                        if (presetsProp != null) PrefabUtility.ApplyPropertyOverride(presetsProp, prefabPath, InteractionMode.UserAction);

                        SerializedProperty jsonProp = so.FindProperty("contextConfigJson");
                        if (jsonProp != null) PrefabUtility.ApplyPropertyOverride(jsonProp, prefabPath, InteractionMode.UserAction);

                        // --- 新增：將動態同步的三個生成參數也一併寫入 Prefab ---
                        SerializedProperty modelTypeProp = so.FindProperty("modelType");
                        if (modelTypeProp != null) PrefabUtility.ApplyPropertyOverride(modelTypeProp, prefabPath, InteractionMode.UserAction);

                        SerializedProperty useHistoryProp = so.FindProperty("useHistory");
                        if (useHistoryProp != null) PrefabUtility.ApplyPropertyOverride(useHistoryProp, prefabPath, InteractionMode.UserAction);

                        SerializedProperty tempProp = so.FindProperty("temperature");
                        if (tempProp != null) PrefabUtility.ApplyPropertyOverride(tempProp, prefabPath, InteractionMode.UserAction);

                        Debug.Log("<color=green>[AgentPanel Editor]</color> 成功將當前 activeContextIndex 與情境資料同步覆寫至 Prefab 母檔！");
                    }
                }
                else if (PrefabUtility.IsPartOfPrefabAsset(panel.gameObject))
                {
                    // 情況 B：使用者直接雙擊打開 Prefab 母檔 (Prefab Mode) 在編輯，直接觸發 Asset 存檔
                    AssetDatabase.SaveAssets();
                    Debug.Log("<color=green>[AgentPanel Editor]</color> 成功儲存 Prefab 母檔設定！");
                }
                else
                {
                    // 情況 C：這只是一個單純的場景物件 (沒有變成 Prefab)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
                    Debug.Log("<color=green>[AgentPanel Editor]</color> 成功標記場景物件變更，請記得手動存檔場景 (Ctrl+S)！");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<color=red>[AgentPanel Editor]</color> 儲存失敗: {ex.Message}");
            }
        }
    }
}