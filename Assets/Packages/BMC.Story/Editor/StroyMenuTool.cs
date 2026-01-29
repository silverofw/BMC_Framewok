using BMC.Story;
using Google.Protobuf;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class StroyMenuTool
    {
        // 1. 定義統一的路徑常量，方便維護
        private const string PROTO_ROOT = "Assets/yoo/DefaultPackage/Proto";
        private const string JSON_ROOT = "Assets/yoo/DefaultPackage/Json";

        // 2. 靜態實例化 Formatter 與 Parser，確保設定一致 (包含預設值)
        private static readonly JsonFormatter _jsonFormatter = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
        private static readonly JsonParser _jsonParser = new JsonParser(JsonParser.Settings.Default);

        [MenuItem("BMC/Story/Export All Proto to Json", false, 1)]
        public static void ExportAllProtoToJson()
        {
            if (!EnsureFolder(PROTO_ROOT)) return;
            EnsureFolder(JSON_ROOT, create: true);

            var files = Directory.GetFiles(PROTO_ROOT, "StoryPackage_*.bytes");
            int successCount = 0;

            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    string fileName = Path.GetFileName(filePath);

                    // 3. 顯示進度條
                    if (EditorUtility.DisplayCancelableProgressBar("Exporting JSON", $"Processing {fileName}...", (float)i / files.Length))
                    {
                        break;
                    }

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(filePath);
                        var package = StoryPackage.Parser.ParseFrom(bytes);

                        string jsonName = Path.ChangeExtension(fileName, ".json");
                        string jsonPath = Path.Combine(JSON_ROOT, jsonName);

                        File.WriteAllText(jsonPath, _jsonFormatter.Format(package));
                        successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Export] Failed to export {fileName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Export] Complete. Exported {successCount}/{files.Length} files to {JSON_ROOT}");
        }

        [MenuItem("BMC/Story/Import All Json to Proto", false, 2)]
        public static void ImportAllJsonToProto()
        {
            if (!EnsureFolder(JSON_ROOT)) return;
            EnsureFolder(PROTO_ROOT, create: true);

            var files = Directory.GetFiles(JSON_ROOT, "StoryPackage_*.json");
            int successCount = 0;

            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    string fileName = Path.GetFileName(filePath);

                    if (EditorUtility.DisplayCancelableProgressBar("Importing Proto", $"Processing {fileName}...", (float)i / files.Length))
                    {
                        break;
                    }

                    try
                    {
                        string jsonContent = File.ReadAllText(filePath);
                        var package = _jsonParser.Parse<StoryPackage>(jsonContent);

                        string protoName = Path.ChangeExtension(fileName, ".bytes");
                        string protoPath = Path.Combine(PROTO_ROOT, protoName);

                        using (var output = File.Create(protoPath))
                        {
                            package.WriteTo(output);
                        }
                        successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[Import] Failed to import {fileName}: {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Import] Complete. Imported {successCount}/{files.Length} files to {PROTO_ROOT}");
        }

        // ===================================================================================
        // Test / Debug Tools
        // ===================================================================================

        [MenuItem("BMC/Story/Debug/Gen Test Data (Both Formats)", false, 20)]
        public static void GenTestData()
        {
            int id = 1;
            var package = GetTestPackage(id);

            EnsureFolder(PROTO_ROOT, true);
            EnsureFolder(JSON_ROOT, true);

            // Save Proto
            string protoPath = Path.Combine(PROTO_ROOT, $"StoryPackage_{id}.bytes");
            using (var output = File.Create(protoPath))
            {
                package.WriteTo(output);
            }

            // Save JSON
            string jsonPath = Path.Combine(JSON_ROOT, $"StoryPackage_{id}.json");
            File.WriteAllText(jsonPath, _jsonFormatter.Format(package));

            AssetDatabase.Refresh();
            Debug.Log($"[Debug] Generated test data for Chapter {id} in both Proto and JSON.");
        }

        [MenuItem("BMC/Story/Debug/Test Load Proto", false, 21)]
        public static void TestLoadProto()
        {
            string path = Path.Combine(PROTO_ROOT, "StoryPackage_1.bytes");
            if (!File.Exists(path))
            {
                Debug.LogError($"File not found: {path}");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            var package = StoryPackage.Parser.ParseFrom(bytes);
            Debug.Log($"[Debug] Load Success. Chapter: {package.ChapterId}, Nodes: {package.Nodes.Count}");
        }

        private static StoryPackage GetTestPackage(int id)
        {
            var package = new StoryPackage { ChapterId = $"{id}" };
            package.Nodes.Add(new StoryNode { Id = "Start" });
            package.Nodes.Add(new StoryNode { Id = "Start2" });
            package.Nodes.Add(new StoryNode { Id = "Start3" });
            return package;
        }

        private static bool EnsureFolder(string path, bool create = false)
        {
            if (Directory.Exists(path)) return true;
            if (create)
            {
                Directory.CreateDirectory(path);
                return true;
            }
            Debug.LogWarning($"[StroyMenuTool] Folder not found: {path}");
            return false;
        }
    }
}