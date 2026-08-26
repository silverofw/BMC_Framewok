using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor.Settings;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 取得「需要補充元資料的 AOT 組件清單」。
    ///
    /// 【為什麼不直接用 AOTGenericReferences.PatchedAOTAssemblyList】那個類別是 HybridCLR
    /// 產生在**專案**裡的(Assets/&lt;設定的路徑&gt;/AOTGenericReferences.cs)，套件引用不到它 ——
    /// 這正是以前每個專案都要自己寫一份 Build.cs 把清單傳進來的原因。
    ///
    /// 【為什麼讀檔而不是反射讀那個常數】反射讀到的是「已經編譯進編輯器的值」。
    /// GenerateAll 剛把檔案重寫過、但 Unity 還沒完成重新編譯時，那個值是舊的 ——
    /// 於是這一輪會複製到跟實際 AOT 不相符的補充元資料，而且完全沒有錯誤訊息。
    /// 直接讀檔拿到的一定是最新結果。反射只當作讀檔失敗時的備援。
    ///
    /// (即使如此，GenerateAll 與實際建置仍建議分成兩次啟動 Unity —— 產生出來的
    ///  AOTGenericReferences.cs 本身也要被編進母包，見 BuildRunner 的說明。)
    /// </summary>
    public static class AotReferences
    {
        private static readonly Regex EntryPattern = new Regex("\"([^\"]+\\.dll)\"", RegexOptions.Compiled);

        public static IReadOnlyList<string> Resolve()
        {
            var fromFile = ReadFromGeneratedFile();
            if (fromFile != null && fromFile.Count > 0)
                return fromFile;

            var fromReflection = ReadByReflection();
            if (fromReflection != null && fromReflection.Count > 0)
            {
                Debug.LogWarning("[AotReferences] 讀不到產生的檔案，改用反射取得清單 —— "
                                 + "如果剛跑過 GenerateAll，這個值可能是舊的。");
                return fromReflection;
            }

            Debug.LogError("[AotReferences] 找不到 AOT 補充元資料清單。請先執行 HybridCLR 的 GenerateAll。");
            return Array.Empty<string>();
        }

        /// <summary>產生出來的檔案路徑。HybridCLR 自己是用 $"{Application.dataPath}/{設定}" 組的。</summary>
        public static string GeneratedFilePath()
        {
            var settings = HybridCLRSettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.outputAOTGenericReferenceFile))
                return null;
            return Path.Combine(Application.dataPath, settings.outputAOTGenericReferenceFile);
        }

        private static List<string> ReadFromGeneratedFile()
        {
            string path = GeneratedFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            string text = File.ReadAllText(path);

            // 只取 PatchedAOTAssemblyList 那個初始化區塊 —— 同一個檔案裡還有泛型型別與方法的
            // 清單，整份掃字串會撈到不相干的東西。
            int start = text.IndexOf("PatchedAOTAssemblyList", StringComparison.Ordinal);
            if (start < 0) return null;

            int end = text.IndexOf("};", start, StringComparison.Ordinal);
            if (end < 0) return null;

            return EntryPattern.Matches(text.Substring(start, end - start))
                               .Cast<Match>()
                               .Select(m => m.Groups[1].Value)
                               .Distinct()
                               .ToList();
        }

        private static List<string> ReadByReflection()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = asm.GetType("AOTGenericReferences"); }
                catch { continue; }          // 動態組件可能不允許查詢

                var field = type?.GetField("PatchedAOTAssemblyList");
                if (field?.GetValue(null) is IEnumerable<string> list)
                    return list.ToList();
            }
            return null;
        }
    }
}
