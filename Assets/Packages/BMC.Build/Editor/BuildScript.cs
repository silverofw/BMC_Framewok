using HybridCLR.Editor.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace BMC.Build.Editor
{
    public class BuildScript
    {
        // 移除未使用的 public static IReadOnlyList<string> PatchedAOTAssemblyList; 避免與參數混淆

        private static string[] GetEnabledScenes()
        {
            // 優化：使用 LINQ 簡化獲取啟用場景的路徑
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        // --- 獲取當前 Build Profile 名稱 ---
        private static string GetActiveProfileName()
        {
            string profileName = "DefaultProfile";

            try
            {
                // 根據 Unity 源碼：internal static BuildProfile activeBuildProfile { get; set; }
                // 因為是 internal，必須使用反射 (Reflection) 加上 NonPublic 標籤才能合法存取
                var propertyInfo = typeof(EditorUserBuildSettings).GetProperty(
                    "activeBuildProfile",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

                if (propertyInfo != null && propertyInfo.GetValue(null) is UnityEngine.Object profileObj && profileObj != null)
                {
                    profileName = profileObj.name;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BuildScript] 無法讀取 Build Profile，將使用預設名稱。錯誤: {e.Message}");
            }

            return profileName;
        }

        // 修改 BuildForTarget：移除硬編碼的 subfolder，改為自動讀取當前的 Build Profile 名稱
        private static UnityEditor.Build.Reporting.BuildReport BuildForTarget(BuildTarget target, string fileExtension)
        {
            // 防呆機制：如果不是透過 "Build Active Platform" 呼叫，確保目前編輯器平台與目標平台一致
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.LogError($"[打包失敗] 嘗試打包的目標是 {target}，但目前編輯器處於 {EditorUserBuildSettings.activeBuildTarget} 平台！請先切換平台。");
                return null;
            }

            // 安全獲取當前 Active 的 Build Profile 名稱
            string profileName = GetActiveProfileName();

            // 路徑格式: Builds/<平台>/<BuildProfile>/v<版本號>
            string buildPath = Path.Combine("Builds", target.ToString(), profileName, $"v{Application.version}");
            if (!Directory.Exists(buildPath)) { Directory.CreateDirectory(buildPath); }

            string locationPathName = Path.Combine(buildPath, $"{Application.companyName}_{Application.productName}_{Application.version}{fileExtension}");

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = locationPathName,
                target = target,
                options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log($"Starting build for {target} using profile '{profileName}' to {locationPathName}...");
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"Build for {target} Succeeded! Path: {summary.outputPath}");
            }
            else
            {
                Debug.Log($"Build for {target} Failed with result: {summary.result}");
            }

            return report;
        }

        // --- 智慧副檔名判斷 ---
        private static string GetExtensionForTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.Android:
                    // 自動判斷目前設定是打 APK 還是 AAB
                    return EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.iOS:
                    return ""; // iOS 輸出的是資料夾 (Xcode專案)
                default:
                    return "";
            }
        }

        // --- MenuItem 列表 ---

        // ★ 終極整合按鈕：自動打包當前啟用的平台與 Profile ★
        [MenuItem("BMC/Build/Build Active Platform (自動打包當前平台)", false, 1)]
        public static void BuildActivePlatform()
        {
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            string extension = GetExtensionForTarget(activeTarget);
            BuildForTarget(activeTarget, extension);
        }

        // --- 特殊工具：建置並打包 iOS IPA (需要 Mac 環境) ---
        [MenuItem("BMC/Build/iOS Tools/Build and Package IPA", false, 50)]
        public static void BuildAndPackageIOS()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                Debug.LogError("請先將 Unity 平台切換至 iOS 才能執行此打包腳本。");
                return;
            }

            // 僅能在 Mac 上執行此打包流程
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.LogError("iOS packaging can only be performed on a Mac Editor.");
                return;
            }

            // 步驟 1: 執行 Unity Build 以產生 Xcode 專案
            var report = BuildForTarget(BuildTarget.iOS, "");

            // 如果 Unity Build 失敗，則停止後續流程
            if (report == null || report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("Unity build failed. Aborting Xcode packaging process.");
                return;
            }

            Debug.Log("Unity build successful. Starting Xcode packaging process...");

            // 步驟 2: 執行外部 Shell 腳本來打包 .ipa

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string xcodeProjectPath = report.summary.outputPath;

            string ipaOutputPath = Path.Combine(projectRoot, "Builds", "iOS_IPA");
            if (!Directory.Exists(ipaOutputPath)) { Directory.CreateDirectory(ipaOutputPath); }

            string shellScriptPath = Path.Combine(projectRoot, "build_ipa.sh");
            string exportOptionsPath = Path.Combine(projectRoot, "ExportOptions.plist");

            if (!File.Exists(shellScriptPath) || !File.Exists(exportOptionsPath))
            {
                Debug.LogError($"Missing required files! Ensure 'build_ipa.sh' and 'ExportOptions.plist' exist in your project root: {projectRoot}");
                return;
            }

            System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{shellScriptPath}\" \"{xcodeProjectPath}\" \"{ipaOutputPath}\" \"{exportOptionsPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(processInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                Debug.Log("Shell Script Output:\n" + output);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError("Shell Script Error:\n" + error);
                }
            }

            Debug.Log("Xcode packaging process finished.");
        }

        // [Bug 修復]: 移除了靜態 BuildTarget 緩存。靜態變數在切換平台時不會自動更新，會導致打包出錯。
        // 現在改為即時讀取 EditorUserBuildSettings.activeBuildTarget。

        /// <summary>
        /// 建置 Patch 資源包
        /// </summary>
        /// <param name="fastMode">是否開啟快速模式（僅編譯熱更DLL，不重新生成AOT）</param>
        public static void BuildPatch(IReadOnlyList<string> patchedAOTAssemblyList, List<(string, EBuildPipeline)> list, bool fastMode = false)
        {
            // 1. HybridCLR 編譯程序集
            if (fastMode)
            {
                // Fast Mode: 僅編譯業務邏輯的熱更新 DLL，速度極快 (日常開發使用)
                CompileDllCommand.CompileDllActiveBuildTarget();
                Debug.Log("HybridCLR hot-update dll compile completed (Fast Mode).");
            }
            else
            {
                // Full Mode: 重新生成橋接代碼、AOT與熱更 DLL (大保養使用)
                PrebuildCommand.GenerateAll();
                Debug.Log("HybridCLR aot dll and hot-update dll build completed (Full Mode).");
            }

            // 2. 複製熱更Assembly到YooAssets資料夾
            CopyHotUpdateAssemblies(patchedAOTAssemblyList);

            // 3. YooAssets 打包資源
            foreach (var (packageName, pipeline) in list)
            {
                BuildResult buildResult = null;
                switch (pipeline)
                {
                    case EBuildPipeline.BuiltinBuildPipeline:
                        buildResult = ExecuteBuildtinBuild(packageName);
                        break;
                    case EBuildPipeline.RawFileBuildPipeline:
                        buildResult = ExecuteRawFileBuild(packageName);
                        break;
                    default:
                        Debug.LogError($"Unsupported build pipeline: {pipeline}");
                        continue;
                }

                if (buildResult != null && buildResult.Success)
                {
                    Debug.Log($"YooAssets {packageName} build successful.");
                }
                else if (buildResult != null)
                {
                    Debug.LogError($"YooAssets build failed: {buildResult.ErrorInfo}");
                }
            }
        }

        /// <summary>
        /// 搬運 YooAsset.Editor.ScriptableBuildPipelineViewer 內容
        /// </summary>
        protected static BuildResult ExecuteBuildtinBuild(string PackageName)
        {
            var PipelineName = EBuildPipeline.BuiltinBuildPipeline.ToString();

            BuiltinBuildParameters buildParameters = new BuiltinBuildParameters
            {
                BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = PipelineName,
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                // 即時讀取當前平台，避免快取 Bug
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = PackageName,
                PackageVersion = GetDefaultPackageVersion(),
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PackageName, PipelineName),
                BuildinFileCopyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(PackageName, PipelineName),
                BuildinFileCopyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(PackageName, PipelineName),
                CompressOption = AssetBundleBuilderSetting.GetPackageCompressOption(PackageName, PipelineName),
                ClearBuildCacheFiles = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName),
                UseAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName),
                EncryptionServices = CreateEncryptionServicesInstance(PackageName, PipelineName),
                ManifestProcessServices = CreateManifestProcessServicesInstance(PackageName, PipelineName),
                ManifestRestoreServices = CreateManifestRestoreServicesInstance(PackageName, PipelineName)
            };

            Debug.Log($"BuildinFileCopyOption: {buildParameters.BuildinFileCopyOption}");

            BuiltinBuildPipeline pipeline = new BuiltinBuildPipeline();
            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
            return buildResult;
        }

        protected static BuildResult ExecuteRawFileBuild(string PackageName)
        {
            var PipelineName = EBuildPipeline.RawFileBuildPipeline.ToString();

            RawFileBuildParameters buildParameters = new RawFileBuildParameters
            {
                BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = PipelineName,
                BuildBundleType = (int)EBuildBundleType.RawBundle,
                // 即時讀取當前平台，避免快取 Bug
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = PackageName,
                PackageVersion = GetDefaultPackageVersion(),
                VerifyBuildingResult = true,
                FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PackageName, PipelineName),
                BuildinFileCopyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(PackageName, PipelineName),
                BuildinFileCopyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(PackageName, PipelineName),
                ClearBuildCacheFiles = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName),
                UseAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName),
                EncryptionServices = CreateEncryptionServicesInstance(PackageName, PipelineName),
                ManifestProcessServices = CreateManifestProcessServicesInstance(PackageName, PipelineName),
                ManifestRestoreServices = CreateManifestRestoreServicesInstance(PackageName, PipelineName)
            };

            Debug.Log($"BuildinFileCopyOption: {buildParameters.BuildinFileCopyOption}");

            RawFileBuildPipeline pipeline = new RawFileBuildPipeline();
            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
            return buildResult;
        }

        private static string GetDefaultPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        /// <summary>
        /// 创建资源包加密服务类实例
        /// </summary>
        protected static IEncryptionServices CreateEncryptionServicesInstance(string PackageName, string PipelineName)
        {
            var className = AssetBundleBuilderSetting.GetPackageEncyptionServicesClassName(PackageName, PipelineName);
            var classType = EditorTools.GetAssignableTypes(typeof(IEncryptionServices)).Find(x => x.FullName.Equals(className));
            return classType != null ? (IEncryptionServices)Activator.CreateInstance(classType) : null;
        }

        /// <summary>
        /// 创建资源清单加密服务类实例
        /// </summary>
        protected static IManifestProcessServices CreateManifestProcessServicesInstance(string PackageName, string PipelineName)
        {
            var className = AssetBundleBuilderSetting.GetPackageManifestProcessServicesClassName(PackageName, PipelineName);
            var classType = EditorTools.GetAssignableTypes(typeof(IManifestProcessServices)).Find(x => x.FullName.Equals(className));
            return classType != null ? (IManifestProcessServices)Activator.CreateInstance(classType) : null;
        }

        /// <summary>
        /// 创建资源清单解密服务类实例
        /// </summary>
        protected static IManifestRestoreServices CreateManifestRestoreServicesInstance(string PackageName, string PipelineName)
        {
            var className = AssetBundleBuilderSetting.GetPackageManifestRestoreServicesClassName(PackageName, PipelineName);
            var classType = EditorTools.GetAssignableTypes(typeof(IManifestRestoreServices)).Find(x => x.FullName.Equals(className));
            return classType != null ? (IManifestRestoreServices)Activator.CreateInstance(classType) : null;
        }

        public static void CopyHotUpdateAssemblies(IReadOnlyList<string> PatchedAOTAssemblyList)
        {
            // 取得所有需要熱更新的 DLL 檔案名稱 (已去重)
            var hotUpdateAssemblies = GetHotUpdateAssemblies();

            // HybridCLR 熱更新 DLL 的輸出目錄
            string hotUpdateDllsDir = Path.Combine(HybridCLR.Editor.Settings.HybridCLRSettings.Instance.hotUpdateDllCompileOutputRootDir, EditorUserBuildSettings.activeBuildTarget.ToString());

            // 您的 YooAssets 資源目標目錄
            string destinationDir = "Assets/yoo/DefaultPackage/DLL";

            // 確保目標資料夾存在
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // 搬運熱更新 DLL
            foreach (var dll in hotUpdateAssemblies)
            {
                string dllName = dll + ".dll";
                string sourceFile = Path.Combine(hotUpdateDllsDir, dllName);

                if (File.Exists(sourceFile))
                {
                    string destFile = Path.Combine(destinationDir, dllName + ".bytes");
                    File.Copy(sourceFile, destFile, true);
                    Debug.Log($"[HotUpdate] Copied '{dllName}' to '{destFile}'");
                }
                else
                {
                    Debug.LogError($"[HotUpdate] DLL not found: {sourceFile}");
                }
            }

            // 搬運 AOT 補充元數據 DLL
            if (PatchedAOTAssemblyList != null)
            {
                foreach (var dllName in PatchedAOTAssemblyList)
                {
                    string aotDllsDir = Path.Combine(HybridCLR.Editor.Settings.HybridCLRSettings.Instance.strippedAOTDllOutputRootDir, EditorUserBuildSettings.activeBuildTarget.ToString());
                    string sourceFile = Path.Combine(aotDllsDir, dllName);

                    if (File.Exists(sourceFile))
                    {
                        string destFile = Path.Combine(destinationDir, dllName + ".bytes");
                        File.Copy(sourceFile, destFile, true);
                        Debug.Log($"[AOT] Copied '{dllName}' to '{destFile}'");
                    }
                    else
                    {
                        // 提示優化：標註這是 AOT DLL 丟失，提醒可能需要 Generate All
                        Debug.LogWarning($"[AOT] DLL not found: {sourceFile}. 若有修改 AOT 代碼，請先執行 'Generate -> All' 或是 Build 一次 Player。");
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("AssetDatabase refreshed. Assemblies copy complete.");
        }

        /// <summary>
        /// 從 HybridCLRSettings 中獲取所有熱更新程序集列表 (會自動去重)
        /// </summary>
        private static IEnumerable<string> GetHotUpdateAssemblies()
        {
            var settings = HybridCLR.Editor.Settings.HybridCLRSettings.Instance;

            // 使用 HashSet 自動處理重複的名稱
            var hotUpdateAssemblies = new HashSet<string>();

            // 1. 從 hotUpdateAssemblyDefinitions (asmdef) 添加
            if (settings.hotUpdateAssemblyDefinitions != null)
            {
                foreach (var asmdef in settings.hotUpdateAssemblyDefinitions.Where(a => a != null))
                {
                    hotUpdateAssemblies.Add(asmdef.name);
                }
            }

            // 2. 從 hotUpdateAssemblies (手動列表) 添加
            if (settings.hotUpdateAssemblies != null)
            {
                foreach (var assembly in settings.hotUpdateAssemblies)
                {
                    hotUpdateAssemblies.Add(assembly);
                }
            }

            // 3. 從 preserveHotUpdateAssemblies (隨主包發布的熱更列表) 添加
            if (settings.preserveHotUpdateAssemblies != null)
            {
                foreach (var assembly in settings.preserveHotUpdateAssemblies)
                {
                    hotUpdateAssemblies.Add(assembly);
                }
            }

            return hotUpdateAssemblies;
        }
    }
}