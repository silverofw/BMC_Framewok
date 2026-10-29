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

        public static string Write() => WriteInternal(FileName, "BuildLogs", null, null);

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
            return WriteInternal(DemoFileName, "BuildLogs_Demo", extraArgs, null);
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
            return WriteInternal(FullFileName, "BuildLogs_Full", extraArgs, null);
        }

        /// <summary>
        /// 產生一支任意組合的打包 bat。給消費端自己的版本用 —— 版本怎麼切是專案自己的事，
        /// 框架不該知道「steam_demo」「web_demo」這種專案名詞，但寫 bat 的那套樣板
        /// (Unity 路徑、出手前檢查、兩趟 executeMethod、log 資料夾、失敗摘要)應該共用。
        ///
        /// 在這之前這個方法是 private，消費端要多一種版本只能整段複製一份 bat 樣板，
        /// 樣板一改就兩邊不同步。
        /// </summary>
        /// <param name="fileName">產出的檔名，例如 build_web_demo.bat</param>
        /// <param name="logDirName">log 資料夾名，例如 BuildLogs_WebDemo</param>
        /// <param name="extraArgs">兩趟 executeMethod 都會帶上的額外參數，可為 null</param>
        /// <param name="doneNote">接在成功訊息後面的一句話，可為 null。有這個參數，
        /// 「只有完成訊息不一樣」的版本就不必整段複製樣板。</param>
        public static string WriteCustom(string fileName, string logDirName, string extraArgs,
                                         string doneNote = null)
            => WriteInternal(fileName, logDirName, extraArgs, doneNote);

        private static string WriteInternal(string fileName, string logDirName, string extraArgs,
                                            string doneNote)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, fileName);

            // chcp 65001 + 無 BOM 的 UTF-8：Windows 10/11 的 cmd 這樣才不會把中文顯示成亂碼。
            // 一定要 false：BOM 會被 cmd 當成第一行命令的一部分，連 @echo off 都會壞掉。
            File.WriteAllText(path, Build(EditorApplication.applicationPath, projectRoot, logDirName,
                                          extraArgs, doneNote),
                              new UTF8Encoding(false));

            Debug.Log($"[BatchFileWriter] 已產生 {path}");
            return path;
        }

        /// <summary>
        /// 失敗時把 log 裡的錯誤行撈出來印在畫面上。
        ///
        /// 【為什麼要這段】Unity batchmode 的 log 動輒兩百萬行，以前失敗只印一句「詳細訊息
        /// 看 log」，每次都要自己去翻。先撈關鍵字，撈不到才退回印最後 20 行 —— 例如 Unity
        /// 在進到 C# 之前就死了，log 裡根本不會有 error CS 這類字樣。
        ///
        /// 整段包在一對雙引號裡，cmd 不會把裡面的 | 當管線。裡面不能出現 %，bat 會展開它。
        /// </summary>
        private const string FailTailCommand =
            "powershell -NoProfile -ExecutionPolicy Bypass -Command \""
            + "$p = $env:FAILLOG; "
            + "$m = @(Select-String -LiteralPath $p -Encoding utf8 "
            + "-Pattern 'error CS[0-9]|BuildFailedException|Aborting batchmode|^Exception|\\[CI\\]' "
            + "| Select-Object -Last 20); "
            + "if ($m.Count) { $m | ForEach-Object { $_.Line } } "
            + "else { Get-Content -LiteralPath $p -Tail 20 }\"";

        private static string Build(string unityPath, string projectRoot, string logDirName,
                                    string extraArgs, string doneNote)
        {
            string extra = string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs;
            string note = string.IsNullOrEmpty(doneNote) ? "" : doneNote;

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal");
            sb.AppendLine();
            sb.AppendLine(":: 由 BMC/Build/產生 build.bat 自動產生。路徑是這台機器的實況，換機器請重新產生。");
            sb.AppendLine();
            sb.AppendLine($"set \"UNITY={unityPath.Replace('/', '\\')}\"");
            sb.AppendLine($"set \"PROJECT={projectRoot}\"");
            sb.AppendLine($"set \"LOGDIR=%PROJECT%\\{logDirName}\"");
            sb.AppendLine("set \"LOCKFILE=%PROJECT%\\Temp\\UnityLockfile\"");
            sb.AppendLine();
            sb.AppendLine(":: ---- 出手前的三道檢查 ----");
            sb.AppendLine(":: 這三種情況以前都要等 Unity 跑進去才掛，訊息還埋在 log 深處。");
            sb.AppendLine();
            sb.AppendLine("if not exist \"%UNITY%\" (");
            sb.AppendLine("    echo [錯誤] 找不到 Unity：%UNITY%");
            sb.AppendLine("    echo        Unity 版本換掉了。請在編輯器重新產生這支 bat。");
            sb.AppendLine("    exit /b 2");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("if not exist \"%PROJECT%\\Assets\" (");
            sb.AppendLine("    echo [錯誤] 這個路徑下沒有專案：%PROJECT%");
            sb.AppendLine("    echo        專案搬過位置。請在編輯器重新產生這支 bat。");
            sb.AppendLine("    exit /b 2");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine(":: 同一個專案不能同時被兩個 Unity 開啟。用「附加零位元組」試開 UnityLockfile：");
            sb.AppendLine(":: 開得起來代表只是上次沒關乾淨留下的殘檔，開不起來才是真的有人佔著。");
            sb.AppendLine(":: 不包成 if/else 是因為 cmd 的巢狀括號配 || 很容易解析錯，goto 穩當得多。");
            sb.AppendLine("if not exist \"%LOCKFILE%\" goto lock_ok");
            sb.AppendLine("2>nul ( >>\"%LOCKFILE%\" call ) && goto lock_ok");
            sb.AppendLine("echo [錯誤] 這個專案正被另一個 Unity 開著，關掉它再跑一次。");
            sb.AppendLine("exit /b 2");
            sb.AppendLine(":lock_ok");
            sb.AppendLine();
            sb.AppendLine("if not exist \"%LOGDIR%\" mkdir \"%LOGDIR%\"");
            sb.AppendLine("set \"T0=%TIME%\"");
            sb.AppendLine();
            sb.AppendLine(":: 兩趟是必要的，不是保守。GenerateAll 產出的 AOTGenericReferences.cs");
            sb.AppendLine(":: 要等 Unity 重新編譯才算數，而那個編譯不會在同一次 executeMethod 內完成。");
            sb.AppendLine();
            sb.AppendLine("set \"STEP=1/2 Generate\"");
            sb.AppendLine("set \"FAILLOG=%LOGDIR%\\1_generate.log\"");
            sb.AppendLine("echo [1/2] %TIME%  HybridCLR 重生 (AOT + 橋接)...");
            sb.AppendLine("\"%UNITY%\" -batchmode -nographics -projectPath \"%PROJECT%\" "
                          + $"-logFile \"%FAILLOG%\" -executeMethod BMC.Build.Editor.CI.Generate{extra}");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine();
            sb.AppendLine("set \"STEP=2/2 BuildAll\"");
            sb.AppendLine("set \"FAILLOG=%LOGDIR%\\2_build.log\"");
            sb.AppendLine("echo [2/2] %TIME%  熱更 + 資源 + CDN 資料夾 + 母包...");
            sb.AppendLine("\"%UNITY%\" -batchmode -nographics -projectPath \"%PROJECT%\" "
                          + $"-logFile \"%FAILLOG%\" -executeMethod BMC.Build.Editor.CI.BuildAll{extra}");
            sb.AppendLine("if errorlevel 1 goto fail");
            sb.AppendLine();
            sb.AppendLine("echo.");
            sb.AppendLine("echo 完成（%T0% 起，%TIME% 止）。母包在 Builds\\ 底下，CDN 資料夾已整理好可直接部署。"
                          + note);
            sb.AppendLine("exit /b 0");
            sb.AppendLine();
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo [失敗] 第 %STEP% 趟掛了（%T0% 起，%TIME% 止）。完整 log：%FAILLOG%");
            sb.AppendLine("if not exist \"%FAILLOG%\" exit /b 1");
            sb.AppendLine("echo ---------------- log 裡的錯誤訊息 ----------------");
            sb.AppendLine(FailTailCommand);
            sb.AppendLine("echo --------------------------------------------------");
            sb.AppendLine("exit /b 1");
            return sb.ToString();
        }
    }
}
