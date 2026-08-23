using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 不開 Unity 介面時的進入點，給 -batchmode -executeMethod 用。
    ///
    /// 【為什麼不做成一個方法就好】見 <see cref="BuildRunner"/> 的說明：GenerateAll 產出的
    /// AOTGenericReferences.cs 必須被 Unity 重新編譯之後才算數，而那個編譯不會在同一次
    /// executeMethod 之內完成。所以外部必須分兩次啟動 Unity，先 Generate 再 BuildAll。
    ///
    /// 【為什麼自己呼叫 EditorApplication.Exit】batchmode 只靠 -quit 的話，中途丟例外的
    /// 結束碼不一定可靠，外層腳本會誤判成功。這裡明確用 0/1 收尾，build.bat 才擋得住。
    ///
    /// 支援的參數(接在 Unity 命令列後面)：
    ///   -bmcProfile &lt;資產路徑&gt;   指定要用哪一份 BuildProfile(專案有多份時)
    ///   -bmcNoPlayer             只出資源與 CDN 資料夾，不出母包
    ///   -bmcFullGenerate         BuildAll 時連 AOT 一起重生(等同不分兩趟，通常不需要)
    /// </summary>
    public static class CI
    {
        /// <summary>第 1 趟。</summary>
        public static void Generate()
        {
            Run("Generate", () =>
            {
                BuildRunner.Generate();
                return true;
            });
        }

        /// <summary>第 2 趟：熱更 DLL + 資源包 + CDN 資料夾 + 母包。</summary>
        public static void BuildAll()
        {
            Run("BuildAll", () =>
            {
                var profile = ResolveProfile();
                if (profile == null) return false;

                bool ok = BuildRunner.BuildAll(
                    profile,
                    fastMode: !HasFlag("-bmcFullGenerate"),
                    buildPlayer: !HasFlag("-bmcNoPlayer"));

                if (ok) CdnSync.Deploy(profile);
                return ok;
            });
        }

        // =========================================================

        private static void Run(string name, Func<bool> body)
        {
            int code = 1;
            try
            {
                Debug.Log($"[CI] === {name} 開始 ===");
                code = body() ? 0 : 1;
                Debug.Log($"[CI] === {name} 結束，結束碼 {code} ===");
            }
            catch (Exception e)
            {
                // 這裡一定要自己印出來：batchmode 的例外訊息有時只會出現在 Editor.log 深處，
                // 外層腳本看到的只有一個結束碼。
                Debug.LogError($"[CI] {name} 丟出例外：{e}");
            }
            finally
            {
                EditorApplication.Exit(code);
            }
        }

        private static BuildProfile ResolveProfile()
        {
            string path = GetArg("-bmcProfile");
            if (string.IsNullOrEmpty(path))
                return BuildProfile.Find();

            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            if (profile == null)
                Debug.LogError($"[CI] -bmcProfile 指定的路徑載不到 BuildProfile: {path}");
            return profile;
        }

        private static string GetArg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }

        private static bool HasFlag(string key)
        {
            return Environment.GetCommandLineArgs()
                              .Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
