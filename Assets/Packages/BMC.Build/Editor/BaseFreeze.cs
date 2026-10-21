using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 凍結母包：出母包時把「之後只出熱更也必須沿用的東西」封存起來，只出熱更時拿回來用。
    ///
    /// 【為什麼要封存】母包(exe + GameAssembly)發出去之後就不再重打，熱更只換 DLL 與資源。
    /// 但補充元資料用的 AOT dll 是從 HybridCLR 的裁剪輸出目錄複製的，那個目錄每出一次母包
    /// (不管哪個版本、哪個 Build Profile)就被覆寫一次。之後出熱更若直接拿目錄裡的檔案，
    /// 補充元資料就可能跟玩家手上那份母包對不上，而且不會有任何錯誤訊息。
    /// 所以母包一出完就把它們封存，熱更一律用封存的那份。
    ///
    /// 【封存內容】放在 專案根目錄/BuildBaseline/&lt;Build Profile 名稱&gt;/&lt;Application.version&gt;/
    ///   - AOT/*.dll：補充元資料用的裁剪後 AOT dll
    ///   - AOTGenericReferences.cs：出熱更時拿來比對，不一致代表 AOT 那邊動過，必須重出母包
    ///   - hashes.txt：exe 與 GameAssembly.dll 的 SHA-256，用來確認發出去的母包就是這一份
    /// 這個資料夾要進版控，換電腦也能出熱更。
    /// </summary>
    public static class BaseFreeze
    {
        /// <summary>跟 BuildScript.CopyHotUpdateAssemblies 的目的地一致。</summary>
        const string DllDestinationDir = "Assets/yoo/DefaultPackage/DLL";

        public static string BaselineDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "BuildBaseline", BuildScript.GetActiveProfileName(), Application.version);
        }

        /// <summary>母包出完後呼叫。playerPath 是 BuildReport.summary.outputPath(exe 路徑)。</summary>
        public static bool Archive(string playerPath)
        {
            string dir = BaselineDirectory();
            string aotDir = Path.Combine(dir, "AOT");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(aotDir);

            var hashes = new StringBuilder();

            string strippedDir = Path.Combine(HybridCLR.Editor.Settings.HybridCLRSettings.Instance.strippedAOTDllOutputRootDir,
                                              EditorUserBuildSettings.activeBuildTarget.ToString());
            foreach (var dllName in AotReferences.Resolve())
            {
                string source = Path.Combine(strippedDir, dllName);
                if (!File.Exists(source))
                {
                    Debug.LogError($"[BaseFreeze] 找不到裁剪後的 AOT dll：{source}，封存失敗。");
                    return false;
                }
                string dest = Path.Combine(aotDir, dllName);
                File.Copy(source, dest, true);
                hashes.AppendLine($"AOT/{dllName}\t{Sha256(dest)}");
            }

            string aotRefs = AotReferences.GeneratedFilePath();
            if (string.IsNullOrEmpty(aotRefs) || !File.Exists(aotRefs))
            {
                Debug.LogError("[BaseFreeze] 找不到 AOTGenericReferences.cs，封存失敗。");
                return false;
            }
            File.Copy(aotRefs, Path.Combine(dir, "AOTGenericReferences.cs"), true);

            if (!string.IsNullOrEmpty(playerPath) && File.Exists(playerPath))
            {
                hashes.AppendLine($"{Path.GetFileName(playerPath)}\t{Sha256(playerPath)}");
                string gameAssembly = Path.Combine(Path.GetDirectoryName(playerPath), "GameAssembly.dll");
                if (File.Exists(gameAssembly))
                    hashes.AppendLine($"GameAssembly.dll\t{Sha256(gameAssembly)}");
            }

            File.WriteAllText(Path.Combine(dir, "hashes.txt"), hashes.ToString());
            Debug.Log($"[BaseFreeze] 已封存母包基準 -> {dir}");
            return true;
        }

        /// <summary>
        /// 只出熱更時呼叫，在 CopyHotUpdateAssemblies 之後：把資源目錄裡的 AOT dll 換回封存的那份。
        /// 找不到封存、或 AOT 泛型清單跟母包不一致時回傳 false，呼叫端必須中止。
        /// </summary>
        public static bool RestoreForPatch()
        {
            string dir = BaselineDirectory();
            string aotDir = Path.Combine(dir, "AOT");
            if (!Directory.Exists(aotDir))
            {
                Debug.LogError($"[BaseFreeze] 找不到母包基準 {dir}。"
                               + "請確認 Active Build Profile 與版本號跟發出去的母包相同，或先出一次母包。");
                return false;
            }

            string archivedRefs = Path.Combine(dir, "AOTGenericReferences.cs");
            string currentRefs = AotReferences.GeneratedFilePath();
            if (!File.Exists(archivedRefs) || string.IsNullOrEmpty(currentRefs) || !File.Exists(currentRefs)
                || Sha256(archivedRefs) != Sha256(currentRefs))
            {
                Debug.LogError("[BaseFreeze] AOTGenericReferences.cs 跟母包封存的不一致：AOT 端改過或重跑過 Generate。"
                               + "這種改動熱更送不出去，必須重出母包(並升版本號)。");
                return false;
            }

            var expected = new HashSet<string>(AotReferences.Resolve());
            var archived = Directory.GetFiles(aotDir, "*.dll").Select(Path.GetFileName).ToList();
            foreach (var missing in expected.Where(n => !archived.Contains(n)))
            {
                Debug.LogError($"[BaseFreeze] 封存裡沒有 {missing}，跟目前的 AOT 清單對不上，已中止。");
                return false;
            }

            foreach (var file in archived)
                File.Copy(Path.Combine(aotDir, file), Path.Combine(DllDestinationDir, file + ".bytes"), true);

            AssetDatabase.Refresh();
            Debug.Log($"[BaseFreeze] 已換回母包基準的 {archived.Count} 個 AOT dll <- {dir}");
            return true;
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
