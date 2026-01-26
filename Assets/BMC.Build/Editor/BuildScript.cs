using HybridCLR.Editor.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

public class BuildScript
{
    public static IReadOnlyList<string> PatchedAOTAssemblyList;
    private static string[] GetEnabledScenes()
    {
        List<string> editorScenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) { editorScenes.Add(scene.path); }
        }
        return editorScenes.ToArray();
    }

    private static UnityEditor.Build.Reporting.BuildReport BuildForTarget(BuildTarget target, string subfolder, string fileExtension)
    {
        string buildPath = Path.Combine("Builds", target.ToString());
        buildPath = Path.Combine(buildPath, $"v{Application.version}");
        if (!Directory.Exists(buildPath)) { Directory.CreateDirectory(buildPath); }
        string locationPathName = Path.Combine(buildPath, $"{Application.companyName}_{Application.productName}_{Application.version}{fileExtension}");
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = locationPathName,
            target = target,
            options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None,
        };
        Debug.Log($"Starting build for {target} to {locationPathName}...");
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


    // --- MenuItem 列表 ---

    [MenuItem("BMC/Build/Android", false, (int)BuildTarget.Android)]
    public static void BuildAndroid() { BuildForTarget(BuildTarget.Android, "Android", ".apk"); }

    [MenuItem("BMC/Build/StandaloneWindows64", false, (int)BuildTarget.StandaloneWindows64)]
    public static void BuildPC() { BuildForTarget(BuildTarget.StandaloneWindows64, "StandaloneWindows64", ".exe"); }

    [MenuItem("BMC/Build/iPhone (iOS)", false, (int)BuildTarget.iOS)]
    public static void BuildIOS() { BuildForTarget(BuildTarget.iOS, "iOS_XcodeProject", ""); }

    // --- 新增的：建置並打包 iOS ---
    [MenuItem("BMC/Build/Build and Package iPhone (iOS)", false, (int)BuildTarget.iOS)]
    public static void BuildAndPackageIOS()
    {
        // 僅能在 Mac 上執行此打包流程
        if (Application.platform != RuntimePlatform.OSXEditor)
        {
            Debug.LogError("iOS packaging can only be performed on a Mac Editor.");
            return;
        }

        // 步驟 1: 執行 Unity Build 以產生 Xcode 專案
        var report = BuildForTarget(BuildTarget.iOS, "iOS_XcodeProject", "");

        // 如果 Unity Build 失敗，則停止後續流程
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("Unity build failed. Aborting Xcode packaging process.");
            return;
        }

        Debug.Log("Unity build successful. Starting Xcode packaging process...");

        // 步驟 2: 執行外部 Shell 腳本來打包 .ipa

        // 專案根目錄
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        // Xcode 專案的路徑 (來自 Build Report)
        string xcodeProjectPath = report.summary.outputPath;

        // .ipa 輸出路徑 (我們設定在 Builds/iOS_IPA)
        string ipaOutputPath = Path.Combine(projectRoot, "Builds", "iOS_IPA");
        if (!Directory.Exists(ipaOutputPath)) { Directory.CreateDirectory(ipaOutputPath); }

        // Shell 腳本與設定檔的路徑
        string shellScriptPath = Path.Combine(projectRoot, "build_ipa.sh");
        string exportOptionsPath = Path.Combine(projectRoot, "ExportOptions.plist");

        if (!File.Exists(shellScriptPath) || !File.Exists(exportOptionsPath))
        {
            Debug.LogError($"Missing required files! Ensure 'build_ipa.sh' and 'ExportOptions.plist' exist in your project root: {projectRoot}");
            return;
        }

        // 設定 ProcessStartInfo 來執行 Shell 腳本
        System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo();
        processInfo.FileName = "/bin/bash";
        processInfo.Arguments = $"\"{shellScriptPath}\" \"{xcodeProjectPath}\" \"{ipaOutputPath}\" \"{exportOptionsPath}\"";
        processInfo.UseShellExecute = false;
        processInfo.RedirectStandardOutput = true;
        processInfo.RedirectStandardError = true;

        System.Diagnostics.Process process = System.Diagnostics.Process.Start(processInfo);

        // 讀取輸出與錯誤訊息
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Debug.Log("Shell Script Output:\n" + output);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError("Shell Script Error:\n" + error);
        }

        Debug.Log("Xcode packaging process finished.");
    }

    static BuildTarget BuildTarget = EditorUserBuildSettings.activeBuildTarget;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="patchedAOTAssemblyList"></param>
    public static void BuildPatch(IReadOnlyList<string> patchedAOTAssemblyList, List<(string, EBuildPipeline)> list)
    {
        // 1. HybridCLR 編譯熱更Assembly
        PrebuildCommand.GenerateAll();
        Debug.Log("HybridCLR aot dll and hot-update dll build completed.");

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
            if (buildResult.Success)
            {
                Debug.Log($"YooAssets {packageName} build successful.");
            }
            else
            {
                Debug.LogError($"YooAssets build failed: {buildResult.ErrorInfo}");
            }
        }
    }

    /// <summary>
    /// 搬運 YooAsset.Editor.ScriptableBuildPipelineViewer 內容
    /// </summary>
    /// <param name="PackageName"></param>
    /// <param name="PipelineName"></param>
    protected static BuildResult ExecuteBuildtinBuild(string PackageName)
    {
        var PipelineName = EBuildPipeline.BuiltinBuildPipeline.ToString();
        var fileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PackageName, PipelineName);
        var buildinFileCopyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(PackageName, PipelineName);
        var buildinFileCopyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(PackageName, PipelineName);
        var compressOption = AssetBundleBuilderSetting.GetPackageCompressOption(PackageName, PipelineName);
        var clearBuildCache = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName);
        var useAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName);

        BuiltinBuildParameters buildParameters = new BuiltinBuildParameters();
        buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
        buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
        buildParameters.BuildPipeline = PipelineName.ToString();
        buildParameters.BuildBundleType = (int)EBuildBundleType.AssetBundle;
        buildParameters.BuildTarget = BuildTarget;
        buildParameters.PackageName = PackageName;
        buildParameters.PackageVersion = GetDefaultPackageVersion();
        buildParameters.EnableSharePackRule = true;
        buildParameters.VerifyBuildingResult = true;
        buildParameters.FileNameStyle = fileNameStyle;
        buildParameters.BuildinFileCopyOption = buildinFileCopyOption;
        Debug.Log($"BuildinFileCopyOption: {buildinFileCopyOption}");
        buildParameters.BuildinFileCopyParams = buildinFileCopyParams;
        buildParameters.CompressOption = compressOption;
        buildParameters.ClearBuildCacheFiles = clearBuildCache;
        buildParameters.UseAssetDependencyDB = useAssetDependencyDB;
        buildParameters.EncryptionServices = CreateEncryptionServicesInstance(PackageName, PipelineName);
        buildParameters.ManifestProcessServices = CreateManifestProcessServicesInstance(PackageName, PipelineName);
        buildParameters.ManifestRestoreServices = CreateManifestRestoreServicesInstance(PackageName, PipelineName);

        BuiltinBuildPipeline pipeline = new BuiltinBuildPipeline();
        var buildResult = pipeline.Run(buildParameters, true);
        if (buildResult.Success)
            EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
        return buildResult;
    }

    protected static BuildResult ExecuteRawFileBuild(string PackageName)
    {
        var PipelineName = EBuildPipeline.RawFileBuildPipeline.ToString();
        var fileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PackageName, PipelineName);
        var buildinFileCopyOption = AssetBundleBuilderSetting.GetPackageBuildinFileCopyOption(PackageName, PipelineName);
        var buildinFileCopyParams = AssetBundleBuilderSetting.GetPackageBuildinFileCopyParams(PackageName, PipelineName);
        var clearBuildCache = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName);
        var useAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName);

        RawFileBuildParameters buildParameters = new RawFileBuildParameters();
        buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
        buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
        buildParameters.BuildPipeline = PipelineName.ToString();
        buildParameters.BuildBundleType = (int)EBuildBundleType.RawBundle;
        buildParameters.BuildTarget = BuildTarget;
        buildParameters.PackageName = PackageName;
        buildParameters.PackageVersion = GetDefaultPackageVersion();
        buildParameters.VerifyBuildingResult = true;
        buildParameters.FileNameStyle = fileNameStyle;
        buildParameters.BuildinFileCopyOption = buildinFileCopyOption;
        Debug.Log($"BuildinFileCopyOption: {buildinFileCopyOption}");
        buildParameters.BuildinFileCopyParams = buildinFileCopyParams;
        buildParameters.ClearBuildCacheFiles = clearBuildCache;
        buildParameters.UseAssetDependencyDB = useAssetDependencyDB;
        buildParameters.EncryptionServices = CreateEncryptionServicesInstance(PackageName, PipelineName);
        buildParameters.ManifestProcessServices = CreateManifestProcessServicesInstance(PackageName, PipelineName);
        buildParameters.ManifestRestoreServices = CreateManifestRestoreServicesInstance(PackageName, PipelineName);

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
        var classTypes = EditorTools.GetAssignableTypes(typeof(IEncryptionServices));
        var classType = classTypes.Find(x => x.FullName.Equals(className));
        if (classType != null)
            return (IEncryptionServices)Activator.CreateInstance(classType);
        else
            return null;
    }

    /// <summary>
    /// 创建资源清单加密服务类实例
    /// </summary>
    protected static IManifestProcessServices CreateManifestProcessServicesInstance(string PackageName, string PipelineName)
    {
        var className = AssetBundleBuilderSetting.GetPackageManifestProcessServicesClassName(PackageName, PipelineName);
        var classTypes = EditorTools.GetAssignableTypes(typeof(IManifestProcessServices));
        var classType = classTypes.Find(x => x.FullName.Equals(className));
        if (classType != null)
            return (IManifestProcessServices)Activator.CreateInstance(classType);
        else
            return null;
    }

    /// <summary>
    /// 创建资源清单解密服务类实例
    /// </summary>
    protected static IManifestRestoreServices CreateManifestRestoreServicesInstance(string PackageName, string PipelineName)
    {
        var className = AssetBundleBuilderSetting.GetPackageManifestRestoreServicesClassName(PackageName, PipelineName);
        var classTypes = EditorTools.GetAssignableTypes(typeof(IManifestRestoreServices));
        var classType = classTypes.Find(x => x.FullName.Equals(className));
        if (classType != null)
            return (IManifestRestoreServices)Activator.CreateInstance(classType);
        else
            return null;
    }

    /// <summary>
    /// 複製所有來源的熱更新 DLL 到資源目錄
    /// </summary>
    [MenuItem("BMC/Build/CopyHotUpdateAssemblies", false, 110)]
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

        foreach (var dll in hotUpdateAssemblies)
        {
            string dllName = dll + ".dll";
            string sourceFile = Path.Combine(hotUpdateDllsDir, dllName);

            if (File.Exists(sourceFile))
            {
                string destFile = Path.Combine(destinationDir, dllName + ".bytes");
                File.Copy(sourceFile, destFile, true);
                Debug.Log($"Copied '{dllName}' to '{destFile}'");
            }
            else
            {
                Debug.LogError($"Hot-update DLL not found: {sourceFile}");
            }
        }

        foreach (var dll in PatchedAOTAssemblyList)
        {
            string dllName = dll;
            string aotDllsDir = Path.Combine(HybridCLR.Editor.Settings.HybridCLRSettings.Instance.strippedAOTDllOutputRootDir, EditorUserBuildSettings.activeBuildTarget.ToString());
            string sourceFile = Path.Combine(aotDllsDir, dllName);

            if (File.Exists(sourceFile))
            {
                string destFile = Path.Combine(destinationDir, dllName + ".bytes");
                File.Copy(sourceFile, destFile, true);
                Debug.Log($"Copied '{dllName}' to '{destFile}'");
            }
            else
            {
                Debug.LogError($"Hot-update DLL not found: {sourceFile}");
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("AssetDatabase refreshed.");
    }

    /// <summary>
    /// 從 HybridCLRSettings 中獲取所有熱更新程序集列表 (會自動去重)
    /// </summary>
    /// <returns></returns>
    private static IEnumerable<string> GetHotUpdateAssemblies()
    {
        var settings = HybridCLR.Editor.Settings.HybridCLRSettings.Instance;

        // 使用 HashSet 自動處理重複的名稱
        var hotUpdateAssemblies = new HashSet<string>();

        // 1. 從 hotUpdateAssemblyDefinitions (asmdef) 添加
        if (settings.hotUpdateAssemblyDefinitions != null)
        {
            foreach (var asmdef in settings.hotUpdateAssemblyDefinitions)
            {
                if (asmdef != null)
                {
                    hotUpdateAssemblies.Add(asmdef.name);
                }
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