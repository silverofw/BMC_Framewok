using BMC.Build.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

public class Build
{
    [MenuItem("BMC/Build/Patch", false, 100)]
    public static void BuildPatch()
    {
        List<(string, EBuildPipeline)> list = new List<(string, EBuildPipeline)>
        {
            ("DefaultPackage", EBuildPipeline.BuiltinBuildPipeline),
            //("RawPackage", EBuildPipeline.RawFileBuildPipeline),
        };
        BuildScript.BuildPatch(AOTGenericReferences.PatchedAOTAssemblyList, list);
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
