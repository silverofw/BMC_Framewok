#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

// [InitializeOnLoad] 標籤會讓這個腳本在 Unity 編輯器一打開，或程式碼重新編譯後自動執行
[InitializeOnLoad]
public class EditorAtlasLoader
{
    static EditorAtlasLoader()
    {
        // 註冊編輯器下的圖集請求事件
        SpriteAtlasManager.atlasRequested -= OnAtlasRequestedInEditor; // 預防重複註冊
        SpriteAtlasManager.atlasRequested += OnAtlasRequestedInEditor;
    }

    private static void OnAtlasRequestedInEditor(string atlasName, System.Action<SpriteAtlas> callback)
    {
        // 透過 AssetDatabase 尋找專案內同名的 SpriteAtlas
        string[] guids = AssetDatabase.FindAssets($"t:SpriteAtlas {atlasName}");

        if (guids.Length > 0)
        {
            // 取第一個找到的結果並轉換成路徑
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);

            // 加載圖集並回傳給 Unity
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
            {
                callback(atlas);
            }
        }
        else
        {
            Debug.LogWarning($"[EditorAtlasLoader] 在專案中找不到圖集: {atlasName}");
        }
    }
}
#endif