using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 出版流程的總指揮。所有專案專屬的值都從 <see cref="BuildProfile"/> 來，這裡不留常數。
    ///
    /// 【流程為什麼要分成兩趟】HybridCLR 的 PrebuildCommand.GenerateAll() 最後一步
    /// AOTReferenceGeneratorCommand 會寫出 Assets/.../AOTGenericReferences.cs 然後
    /// AssetDatabase.Refresh()。那次 refresh 觸發的重新編譯與 domain reload 不會在方法回傳前
    /// 完成 —— 也就是說「同一次執行」讀到的 AOTGenericReferences.PatchedAOTAssemblyList
    /// 一定是舊的。在編輯器裡人可以看到檔案變了再跑一次，但無人值守的流程不會，
    /// 會靜默地把舊的 AOT 補充元資料打進包。所以拆成 Generate 與 BuildAll 兩個進入點，
    /// 由外部(build.bat)分兩次啟動 Unity，讓編譯在兩次之間完成。
    /// </summary>
    public static class BuildRunner
    {
        // =========================================================
        // 選單
        // =========================================================

        [MenuItem("BMC/Build/1. HybridCLR 重生 (AOT + 橋接)", false, 200)]
        public static void MenuGenerate()
        {
            Generate();
            EditorUtility.DisplayDialog("HybridCLR",
                "已重新生成 AOT 泛型清單與橋接函式。\n\n"
                + "Unity 會接著重新編譯 —— 等狀態列跑完再執行下一步，"
                + "否則讀到的 AOT 清單還是舊的。", "了解");
        }

        [MenuItem("BMC/Build/2. 一鍵出版 (熱更 + 資源 + CDN 資料夾 + 母包)", false, 201)]
        public static void MenuBuildAll()
        {
            var profile = BuildProfile.Find();
            if (profile == null) return;

            BuildAll(profile, fastMode: true, buildPlayer: true);
        }

        [MenuItem("BMC/Build/2b. 只出資源與 CDN 資料夾 (不出母包)", false, 202)]
        public static void MenuBuildPatchOnly()
        {
            var profile = BuildProfile.Find();
            if (profile == null) return;

            BuildAll(profile, fastMode: true, buildPlayer: false);
        }

        /// <summary>
        /// 【為什麼上傳要獨立成一個選單，而不是接在出版後面】Cloudflare Pages 免費方案
        /// 每月 500 次 deployment，Direct Upload 也計入。反覆測試打包很容易吃掉配額，
        /// 所以出版只負責把本機的 CDN 資料夾整理好，要不要送上去由人決定。
        /// </summary>
        [MenuItem("BMC/Build/3. 上傳 CDN 到 Cloudflare Pages", false, 203)]
        public static void MenuDeployCdn()
        {
            var profile = BuildProfile.Find();
            if (profile == null) return;

            if (!EditorUtility.DisplayDialog("上傳 CDN",
                    $"要把 {profile.cdnRoot} 上傳到 Cloudflare Pages 專案 "
                    + $"\"{profile.cdnProjectName}\" 嗎？" + System.Environment.NewLine
                    + System.Environment.NewLine
                    + "會計入該專案每月的 deployment 配額。", "上傳", "取消"))
                return;

            CdnSync.Deploy(profile);
        }

        [MenuItem("BMC/Build/產生 build.bat (不開 Unity 打包用)", false, 300)]
        public static void MenuCreateBatch()
        {
            string path = BatchFileWriter.Write();
            EditorUtility.RevealInFinder(path);
        }

        // =========================================================
        // 步驟
        // =========================================================

        /// <summary>第 1 趟：重生 AOT 泛型清單與橋接函式。跑完必須讓 Unity 重新編譯才算數。</summary>
        public static void Generate()
        {
            PrebuildCommand.GenerateAll();
            Debug.Log("[BuildRunner] HybridCLR GenerateAll 完成。");
        }

        /// <summary>
        /// 第 2 趟：編熱更 DLL -> 複製到資源目錄 -> 打資源包 -> 同步 CDN 資料夾 -> 出母包。
        /// 任何一步失敗就中止並回傳 false。
        /// </summary>
        public static bool BuildAll(BuildProfile profile, bool fastMode, bool buildPlayer)
        {
            if (profile == null)
            {
                Debug.LogError("[BuildRunner] 沒有 profile。");
                return false;
            }

            if (!profile.Validate(out string reason))
            {
                Debug.LogError($"[BuildRunner] profile 設定有問題，已中止：{reason}");
                return false;
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            Debug.Log($"[BuildRunner] 開始出版：target={target} version={Application.version} fastMode={fastMode}");

            // 1. 熱更 DLL
            //    fastMode 只編熱更組件，不重生 AOT —— 前提是 Generate() 已經在上一趟跑過。
            if (fastMode)
                CompileDllCommand.CompileDllActiveBuildTarget();
            else
                PrebuildCommand.GenerateAll();

            // 2. 複製熱更與補充元資料用的 AOT dll 到資源目錄
            BuildScript.CopyHotUpdateAssemblies(AotReferences.Resolve());

            // 3. 打資源包
            foreach (var entry in profile.packages)
            {
                ApplyToYooAsset(entry);

                var result = BuildScript.ExecutePackageBuild(entry.packageName, entry.pipeline);
                if (result == null || !result.Success)
                {
                    Debug.LogError($"[BuildRunner] 資源包 {entry.packageName} 建置失敗：{result?.ErrorInfo}");
                    return false;
                }

                Debug.Log($"[BuildRunner] 資源包 {entry.packageName} 完成 -> {result.OutputPackageDirectory}");

                // 4. 同步 CDN 資料夾(只複製客戶端真的會下載的檔案)
                CdnSync.Sync(profile, target, result.OutputPackageDirectory);
            }

            if (!buildPlayer)
                return true;

            // 5. 母包
            var report = BuildScript.BuildForTarget(target, BuildScript.GetExtensionForTarget(target));
            if (report == null || report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError($"[BuildRunner] 母包建置失敗：{report?.summary.result}");
                return false;
            }

            Debug.Log($"[BuildRunner] 全部完成 -> {report.summary.outputPath}");
            return true;
        }

        /// <summary>
        /// 把 profile 的值寫進 YooAsset 的 EditorPrefs。
        ///
        /// 【為什麼每次都要寫】YooAsset 的建置設定存在 EditorPrefs，鍵值含 productGUID 與管線名，
        /// 換機器或改管線就查不到舊值而靜默回退到預設。BundledCopyOption 的預設是 None，
        /// 那會讓 TaskCreateCatalog 整個跳過、打出沒有 BuiltinCatalog 的包，一直到執行期
        /// 才以 404 爆出來。把 profile 當唯一真相每次覆寫，就不會再有這種靜默失效。
        /// </summary>
        private static void ApplyToYooAsset(BuildProfile.PackageEntry e)
        {
            string pipelineName = e.pipeline.ToString();

            BundleBuilderSetting.SetPackageBuildPipeline(e.packageName, pipelineName);
            BundleBuilderSetting.SetPackageBundledCopyOption(e.packageName, pipelineName, e.bundledCopyOption);
            BundleBuilderSetting.SetPackageBundledCopyParams(e.packageName, pipelineName, e.bundledCopyParams ?? string.Empty);
            BundleBuilderSetting.SetPackageCompressOption(e.packageName, pipelineName, e.compressOption);
            BundleBuilderSetting.SetPackageFileNameStyle(e.packageName, pipelineName, e.fileNameStyle);
            BundleBuilderSetting.SetPackageClearBuildCache(e.packageName, pipelineName, e.clearBuildCache);
            BundleBuilderSetting.SetPackageUseAssetDependencyDB(e.packageName, pipelineName, e.useAssetDependencyDB);

            Debug.Log($"[BuildRunner] {e.packageName} 套用設定：{pipelineName} / "
                      + $"{e.bundledCopyOption}({e.bundledCopyParams}) / {e.compressOption} / {e.fileNameStyle}");
        }
    }
}
