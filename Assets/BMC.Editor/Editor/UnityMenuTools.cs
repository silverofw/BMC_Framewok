using BMC.Core;
using Cysharp.Threading.Tasks;
using SQLite;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// BMC
/// </summary>
public class UnityMenuTools
{
    // % = Ctrl (Windows) or Cmd (Mac)
    // # = Shift
    // & = Alt
    // g = The 'G' key
    // Result: Press Ctrl+G to run this function
    //[MenuItem("HotKeys/Start Game %g")]
    [MenuItem("HotKeys/Start Game")]
    public static void StartGame()
    {
        // 1. Prevent execution if already playing
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false; // Optional: Toggle stop if already playing
            return;
        }

        // 2. Ask to save changes in the current scene before switching
        // This prevents losing work if you are editing a level and hit the hotkey.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return; // User cancelled the save operation
        }

        Debug.Log($"<color=green>Bootstrapping Game...</color> Current Scene: {EditorSceneManager.GetActiveScene().name}");

        // 3. Check scene and switch if necessary
        string desiredSceneName = "Patch";
        string scenePath = "Assets/Scenes/Patch.unity"; // Ensure this path is exact

        if (EditorSceneManager.GetActiveScene().name != desiredSceneName)
        {
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else
            {
                Debug.LogError($"<b>CRITICAL:</b> Could not find scene at {scenePath}. Check your folder structure.");
                return; // Do not enter play mode if scene is missing
            }
        }

        // 4. Start Play Mode
        EditorApplication.isPlaying = true;
    }
    [MenuItem("BMC/Folder/OpenPersistentData")]
    public static void OpenPersistentDataFolder()
    {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
    [MenuItem("BMC/Folder/OpenTemporaryCache")]
    public static void OpenTemporaryCacheFolder()
    {
        EditorUtility.RevealInFinder(Application.temporaryCachePath);
    }
    [MenuItem("BMC/Folder/OpenDataPath")]
    public static void OpenDataPathFolder()
    {
        EditorUtility.RevealInFinder(Application.dataPath);
    }

    [MenuItem("BMC/Test SQL")]
    public static void OpenTest()
    {
        var key = $"{Application.persistentDataPath}/DataBase";
        SQLMgr.Instance.InitAsyncConns(key);
        SQLMgr.Instance.CreateTableAsync<StoryChapterSQLData>(key).ContinueWith(c => {
            SQLMgr.Instance.Clear();
        });
    }

    public class StoryChapterSQLData
    {
        [PrimaryKey]
        public int GUID {
            get;
            set;
        }
        
        public string StoryChapter { get; set; }



        public override string ToString() => $"[{GUID}] Entitys:\n{StoryChapter}";
    }
}
