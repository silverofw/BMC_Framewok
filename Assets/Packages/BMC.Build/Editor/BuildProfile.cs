using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 一個專案的出版設定。整套建置流程只從這裡取值，程式碼裡不留任何專案常數 ——
    /// 別的專案裝了 BMC.Build 之後，只要建一份這個資產就能直接用。
    ///
    /// 【為什麼不繼續用 YooAsset 的 EditorPrefs】BundleBuilderSetting 把壓縮方式、檔名樣式、
    /// 首包拷貝選項這些存在 EditorPrefs，鍵值是 {productGUID}_{包名}_{管線名}_{欄位}。
    /// 這代表設定跟著「機器」跑而不是跟著「專案」跑，換一台電腦、換一條建置管線，或是
    /// YooAsset 改了管線名稱(v3 把 BuiltinBuildPipeline 改名成 LegacyBuildPipeline，
    /// productName 鍵改成 productGUID 鍵)，查不到舊值就靜默回退到預設值。
    /// 實際踩過：BundledCopyOption 因此變成 None，TaskCreateCatalog 整個被跳過，
    /// 打出來的包沒有 BuiltinCatalog.bytes，一直到執行期才以 404 爆出來。
    /// 所以流程每次都會把 profile 的值「寫回」EditorPrefs 再建置(見 BuildRunner.ApplyToYooAsset)，
    /// 讓這份進版控的資產成為唯一真相。
    /// </summary>
    [CreateAssetMenu(fileName = "BMCBuildProfile", menuName = "BMC/Build Profile", order = 0)]
    public class BuildProfile : ScriptableObject
    {
        // =========================================================
        // 資源包
        // =========================================================

        [Serializable]
        public class PackageEntry
        {
            public string packageName = "DefaultPackage";
            public EBuildPipeline pipeline = EBuildPipeline.LegacyBuildPipeline;

            [Tooltip("首包資源的拷貝選項。資源全走 CDN 的專案用 ClearAndCopyByTags，"
                     + "並把 tag 設成一個不對應任何資源的名字 —— 這樣一個 bundle 都不會進母包，"
                     + "但 catalog 仍會產生。設成 None 會讓 catalog 整個不產生，執行期會 404。")]
            public EBundledCopyOption bundledCopyOption = EBundledCopyOption.ClearAndCopyByTags;

            [Tooltip("ByTags 時的標籤，多個用分號分隔。ByTags 而這裡留空的話 YooAsset 會直接丟例外。")]
            public string bundledCopyParams = "BUILTIN";

            public ECompressOption compressOption = ECompressOption.LZ4;
            public EFileNameStyle fileNameStyle = EFileNameStyle.HashName;
            public bool clearBuildCache = true;
            public bool useAssetDependencyDB = false;
        }

        [Header("資源包")]
        public List<PackageEntry> packages = new List<PackageEntry> { new PackageEntry() };

        // =========================================================
        // 母包
        // =========================================================

        [Header("母包")]
        [Tooltip("相對於專案根目錄。實際輸出是 <這裡>/<平台>/<BuildProfile 名>/v<版本號>/")]
        public string buildOutputRoot = "Builds";

        // =========================================================
        // CDN
        // =========================================================

        [Serializable]
        public class PlatformFolder
        {
            public BuildTarget target = BuildTarget.StandaloneWindows64;

            [Tooltip("CDN 上的平台資料夾名。必須跟執行期組出來的網址一致 —— "
                     + "FsmInitializePackage.GetHostServerURL 用的是 RuntimePlatform 的名字，"
                     + "所以 Windows 是 WindowsPlayer 而不是 StandaloneWindows64。")]
            public string folderName = "WindowsPlayer";
        }

        [Header("CDN")]
        [Tooltip("相對於專案根目錄的 CDN 部署資料夾，例如 bmc-meow-siege-cdn/CDN。留空 = 這一步跳過。")]
        public string cdnRoot = "";

        public List<PlatformFolder> platformFolders = new List<PlatformFolder> { new PlatformFolder() };

        [Tooltip("只有這些副檔名會被複製到 CDN。YooAsset 執行期只抓 .bundle 與 "
                 + "DefaultPackage_*.bytes / .hash / .version，其餘(.report / .json / OutputCache)"
                 + "都是建置副產物，整包複製時很容易夾帶上去。")]
        public List<string> cdnFileFilters = new List<string> { ".bundle", ".bytes", ".hash", ".version" };

        [Tooltip("同步前先清空該版本目錄。關掉的話舊版本的 bundle 會殘留，"
                 + "資源升級後那些檔案永遠不會再被下載，只是佔空間。")]
        public bool cdnClearBeforeCopy = true;

        // =========================================================
        // 取得
        // =========================================================

        /// <summary>
        /// 找出專案裡唯一的 BMC 出版 profile。
        /// 【為什麼不用固定路徑】不同專案放的位置不一樣，寫死就違背了「換專案只改資產」的目標。
        /// 【為什麼還要再 Load 一次過濾】Unity 6 內建了 UnityEditor.Build.Profile.BuildProfile
        /// （Settings/Build Profiles 底下那些）。FindAssets("t:BuildProfile") 只比類別名，
        /// 會把兩邊都找出來。只接受真的載成我們這個型別的資產，才不會跟編輯器的 profile 撞名。
        /// </summary>
        public static BuildProfile Find()
        {
            var profiles = AssetDatabase.FindAssets($"t:{nameof(BuildProfile)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<BuildProfile>(path))
                .Where(p => p != null)
                .ToArray();

            if (profiles.Length == 0)
            {
                Debug.LogError($"[BuildProfile] 專案裡找不到任何 {nameof(BuildProfile)}。"
                               + "請用 Assets > Create > BMC > Build Profile 建立一份。");
                return null;
            }

            if (profiles.Length > 1)
            {
                Debug.LogError($"[BuildProfile] 找到 {profiles.Length} 份 profile，無法判斷要用哪一份：\n  "
                               + string.Join("\n  ", profiles.Select(AssetDatabase.GetAssetPath)));
                return null;
            }

            return profiles[0];
        }

        /// <summary>設定有沒有明顯的錯，回傳 false 時 reason 說明原因。建置前先擋，不要等到執行期。</summary>
        public bool Validate(out string reason)
        {
            if (packages == null || packages.Count == 0)
            {
                reason = "沒有設定任何資源包";
                return false;
            }

            foreach (var p in packages)
            {
                if (string.IsNullOrWhiteSpace(p.packageName))
                {
                    reason = "有資源包沒有填名稱";
                    return false;
                }

                bool byTags = p.bundledCopyOption == EBundledCopyOption.ClearAndCopyByTags
                              || p.bundledCopyOption == EBundledCopyOption.OnlyCopyByTags;
                if (byTags && string.IsNullOrWhiteSpace(p.bundledCopyParams))
                {
                    reason = $"資源包 {p.packageName} 用了 {p.bundledCopyOption} 卻沒填標籤，"
                             + "YooAsset 會直接丟 InvalidOperationException";
                    return false;
                }

                if (p.bundledCopyOption == EBundledCopyOption.None)
                {
                    reason = $"資源包 {p.packageName} 的拷貝選項是 None —— "
                             + "那會讓 BuiltinCatalog 整個不產生，執行期初始化會 404";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>這個平台在 CDN 上的資料夾名；沒設定就回 null。</summary>
        public string GetPlatformFolder(BuildTarget target)
        {
            return platformFolders?.FirstOrDefault(p => p.target == target)?.folderName;
        }
    }
}
