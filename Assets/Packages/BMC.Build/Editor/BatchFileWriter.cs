using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 產生「不開 Unity 直接打包」用的 build.bat。
    ///
    /// 【為什麼用產生的而不是版控裡放一份】Unity 的安裝路徑跟專案路徑每台機器都不一樣，
    /// 版控一份寫死路徑的 bat，換一台就要手改。從編輯器產生的話兩個路徑都是當下這台的實況。
    /// 產出的 bat 本身不建議進版控(路徑是機器相依的)。
    /// </summary>
    public static class BatchFileWriter
    {
        public const string FileName = "build.bat";
        public const string DemoFileName = "build_demo.bat";
        public const string FullFileName = "build_full.bat";

        public static string Write() => WriteInternal(FileName, "BuildLogs", null);

        /// <summary>
        /// 產生 Demo 版專用的 build_demo.bat：跟一般 build.bat 唯一的差別是兩趟
        /// -executeMethod 都多帶一個 -bmcActiveBuildProfile 參數，執行前先把 Unity 6
        /// 的 Active Build Profile 切到 demoBuildProfileAssetPath 指定的那份(帶
        /// DEMO_BUILD define)，其餘 HybridCLR/資源包/母包流程跟一般版完全共用，
        /// 輸出路徑也會因為 Active Build Profile 名稱不同而自動落在不同資料夾，
        /// 不會互相覆蓋(見 BuildScript.BuildForTarget 用 Build Profile 名稱當子資料夾)。
        /// </summary>
        /// <param name="demoBuildProfileAssetPath">Demo 用 Unity Build Profile 資產路徑，例如
        /// "Assets/Settings/Build Profiles/WindowsDemo.asset"</param>
        public static string WriteDemo(string demoBuildProfileAssetPath)
        {
            string extraArgs = $"-bmcActiveBuildProfile \"{demoBuildProfileAssetPath}\"";
            return WriteInternal(DemoFileName, "BuildLogs_Demo", extraArgs);
        }

        /// <summary>
        /// 產生完全版專用的 build_full.bat：切到帶 FULL_VERSION_BUILD define 的 Build Profile，
        /// 再多帶一個 -bmcFullVersion 旗標讓 BuildRunner.BuildAll 把「FullOnly」命名慣例的
        /// YooAsset 收集分組打開(見 BuildRunner.ApplyFullVersionOnlyGroups)。
        /// </summary>
        /// <param name="fullBuildProfileAssetPath">完全版用 Unity Build Profile 資產路徑，例如
        /// "Assets/Settings/Build Profiles/WindowsFull.asset"</param>
        public static string WriteFull(string fullBuildProfileAssetPath)
        {
            string extraArgs = $"-bmcActiveBuildProfile \"{fullBuildProfileAssetPath}\" -bmcFullVersion";
            return WriteInternal(FullFileName, "BuildLogs_Full", extraArgs);
        }

        private static string WriteInternal(string fileName, string logDirName, string extraArgs)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, fileName);

            // chcp 65001 + 無 BOM 的 UTF-8：Windows 10/11 的 cmd 這樣才不會把中文顯示成亂碼。
            File.WriteAllText(path, Build(EditorApplication.applicationPath, projectRoot, logDirName, extraArgs),
                              new UTF8Encoding(false));

            Debug.Log($"[BatchFileWriter] 已產生 {path}");
            return path;
        }

        private static string Build(string unityPath, string projectRoot, string logDirName, string extraArgs)
        {
            string extra = string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs;

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal");
            sb.AppendLine();
            sb.AppendLine(":: 由 BMC/Build/產生 build.bat 自動產生。路徑是這台機器的實況，換機器請重新產生。");
            sb.AppendLine(":: 執行前請先關閉 Unity —— 同一個專案不能同時被兩個 Unity 開啟。");
            sb.AppendLine();
            sb.AppendLine($"set \"UNITY={unityPath.Replace('/', '\\')}\"");
            sb.AppendLine($"set \"PROJECT={projectRoot}\"");
            sb.AppendLine($"set \"LOGDIR=%PROJECT%\\{logDirName}\"");
            sb.AppendLine("if not exist \"%LOGDIR%\" mkdir \"%LOGDIR%\"");
            sb.AppendLine();
            sb.AppendLine(":: 兩趟是必要的，不是保守。GenerateAll 產出的 AOTGenericReferences.cs");
            sb.AppendLine(":: 要等 Unity 重新編譯才算數，而那個編譯不會在同一次 executeMethod 內完成。");
            sb.AppendLine();
            sb.AppendLine("echo [1/2] HybridCLR 重生 (AOT + 橋接)...");
            sb.AppendLine("\"%UNITY%\" -batchmode -nographics -projectPath \"%PROJECT%\" "
                          + $"-logFile \"%LOGDIR%\\1_generate.log\" -executeMethod BMC.Build.Editor.CI.Generate{extra}");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine();
            sb.AppendLine("echo [2/2] 熱更 + 資源 + CDN 資料夾 + 母包...");
            sb.AppendLine("\"%UNITY%\" -batchmode -nographics -projectPath \"%PROJECT%\" "
                          + $"-logFile \"%LOGDIR%\\2_build.log\" -executeMethod BMC.Build.Editor.CI.BuildAll{extra}");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine();
            sb.AppendLine("echo.");
            sb.AppendLine("echo 完成。母包在 Builds\\ 底下，CDN 資料夾已整理好可直接部署。");
            sb.AppendLine("exit /b 0");
            sb.AppendLine();
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 失敗。詳細訊息看 %LOGDIR% 裡的 log。");
            sb.AppendLine("exit /b 1");
            return sb.ToString();
        }
    }
}
