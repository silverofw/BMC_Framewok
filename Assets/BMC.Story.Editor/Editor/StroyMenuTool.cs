using BMC.Story;
using Google.Protobuf;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace BMC.Story.Editor
{
    public class StroyMenuTool
    {
        static StoryPackage getTestData(int id)
        {
            var package = new StoryPackage();
            package.ChapterId = $"{id}";

            // 模擬一些測試資料
            package.Nodes.Add(new StoryNode { Id = "Start", VideoPath = "v1.mp4" });
            package.Nodes.Add(new StoryNode { Id = "Start2", VideoPath = "v2.mp4" });
            package.Nodes.Add(new StoryNode { Id = "Start3", VideoPath = "v3.mp4" });
            return package;
        }

        [MenuItem("BMC/Story/GenProtoToJson")]
        public static void GenProtoToJson()
        {
            var id = 1;
            var name = $"StoryPackage_{id}.json";
            var folderPath = Path.GetFullPath("./Assets/yoo/DefaultPackage/Proto");
            var fullPath = Path.Combine(folderPath, name);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // --- 建立資料 ---
            var proto = getTestData(id);

            // 設定：WithFormatDefaultValues(true) 很重要！
            // 它可以讓預設值 (如 0, false, "") 也被寫入 JSON。
            // 如果不加這行，空的欄位會被省略，手動編輯時會很不方便。
            var formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));

            // 將物件轉換為 JSON 字串
            string jsonContent = formatter.Format(proto);

            // 4. 寫入文件
            File.WriteAllText(fullPath, jsonContent);

            // 6. 刷新 Unity 編輯器，讓檔案立刻出現
            AssetDatabase.Refresh();

            Debug.Log($"[GenProto] 成功生成文件：{fullPath}");
        }

        [MenuItem("BMC/Story/GenProto (Binary)")]
        public static void GenProto()
        {
            var id = 1;
            // 1. 副檔名改為 .bytes (Unity 識別二進制資源的標準格式)
            var fileName = $"StoryPackage_{id}.bytes";

            var folderPath = Path.GetFullPath("./Assets/yoo/DefaultPackage/Proto");
            var fullPath = Path.Combine(folderPath, fileName);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // --- 建立資料 ---
            var proto = getTestData(id);

            // --- 2. 核心修改：寫入二進制流 (Binary Write) ---

            // 使用 File.Create 建立文件流
            using (var output = File.Create(fullPath))
            {
                // 直接呼叫 Protobuf 原生的 WriteTo 方法
                // 這會把物件壓縮成 byte[] 並寫入硬碟
                proto.WriteTo(output);
            }

            AssetDatabase.Refresh();

            Debug.Log($"[GenProto] 成功生成二進制文件：{fullPath}");
        }

        [MenuItem("BMC/Story/TestLoadProto")]
        public static void TestLoadProto()
        {
            string path = "Assets/yoo/DefaultPackage/Proto/StoryPackage_1.bytes";
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

            if (asset != null)
            {
                // 從 byte[] 還原
                StoryPackage package = StoryPackage.Parser.ParseFrom(asset.bytes);
                Debug.Log($"讀取成功，章節：{package.Nodes[0].VideoPath}");
            }
        }
    }
}