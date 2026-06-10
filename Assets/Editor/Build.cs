using BMC.Build.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

public class Build
{
    // === 日常開發推薦使用這個：極速模式 ===
    [MenuItem("BMC/Build/Fast Patch (僅編譯熱更DLL並打包)", false, 99)]
    public static void BuildFastPatch()
    {
        List<(string, EBuildPipeline)> list = new List<(string, EBuildPipeline)>
        {
            ("DefaultPackage", EBuildPipeline.BuiltinBuildPipeline),
            ("RawPackage", EBuildPipeline.RawFileBuildPipeline),
        };
        // 傳入 true，啟用 Fast Mode
        BuildScript.BuildPatch(AOTGenericReferences.PatchedAOTAssemblyList, list, fastMode: true);
    }

    // === 大保養/修改 AOT 時使用這個：完整模式 ===
    [MenuItem("BMC/Build/Full Patch (完整生成AOT與熱更DLL並打包)", false, 100)]
    public static void BuildFullPatch()
    {
        List<(string, EBuildPipeline)> list = new List<(string, EBuildPipeline)>
        {
            ("DefaultPackage", EBuildPipeline.BuiltinBuildPipeline),
            ("RawPackage", EBuildPipeline.RawFileBuildPipeline),
        };
        // 傳入 false，進行耗時的 GenerateAll
        BuildScript.BuildPatch(AOTGenericReferences.PatchedAOTAssemblyList, list, fastMode: false);
    }

    /// <summary>
    /// 複製所有來源的熱更新 DLL 到資源目錄
    /// </summary>
    [MenuItem("BMC/Build/CopyHotUpdateAssemblies", false, 110)]
    public static void CopyHotUpdateAssemblies()
    {
        BuildScript.CopyHotUpdateAssemblies(AOTGenericReferences.PatchedAOTAssemblyList);
    }
}