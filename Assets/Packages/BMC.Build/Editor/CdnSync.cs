using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 把 YooAsset 的建置產出同步到 CDN 部署資料夾。
    ///
    /// 【為什麼要有這一步】以前是人工從 Bundles/ 整包複製過去，兩個問題都真的發生過：
    ///   1. 夾帶建置副產物 —— OutputCache、OutputCache.manifest、.report、v2 遺留的 .json
    ///      全被傳上公開 CDN，其中 .report 一份就 1.3MB，而客戶端永遠不會下載它們。
    ///   2. 沒有先清空舊版本 —— 升 YooAsset 大版之後 bundle 雜湊全變，舊檔案一個都用不到，
    ///      卻會一直留在 CDN 上。
    /// 交給程式做，這兩件事都不會再發生。
    ///
    /// 上傳(Cloudflare Pages)這一步還沒接，目前只負責把資料夾整理成「可以直接部署」的狀態，
    /// 見 <see cref="Deploy"/>。
    /// </summary>
    public static class CdnSync
    {
        /// <summary>
        /// 同步單一資源包。sourceDirectory 傳 BuildResult.OutputPackageDirectory。
        /// 回傳目的地路徑；profile 沒設定 cdnRoot 或找不到平台對應時回 null(視為刻意跳過)。
        /// </summary>
        public static string Sync(BuildProfile profile, BuildTarget target, string sourceDirectory)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.cdnRoot))
                return null;

            string platformFolder = profile.GetPlatformFolder(target);
            if (string.IsNullOrWhiteSpace(platformFolder))
            {
                Debug.LogWarning($"[CdnSync] profile 裡沒有 {target} 的平台資料夾設定，略過 CDN 同步。");
                return null;
            }

            if (!Directory.Exists(sourceDirectory))
            {
                Debug.LogError($"[CdnSync] 來源目錄不存在: {sourceDirectory}");
                return null;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            // 版本用 Application.version：執行期的 GetHostServerURL 也是用它組網址的，
            // 兩邊必須一致，不能用資源包自己的建置版本號(那是 yyyy-MM-dd-分鐘)。
            string destDirectory = Path.Combine(projectRoot, profile.cdnRoot, platformFolder, Application.version);

            if (profile.cdnClearBeforeCopy && Directory.Exists(destDirectory))
                Directory.Delete(destDirectory, true);
            Directory.CreateDirectory(destDirectory);

            var filters = profile.cdnFileFilters ?? new System.Collections.Generic.List<string>();
            int copied = 0, skipped = 0;
            long bytes = 0;

            foreach (var file in Directory.GetFiles(sourceDirectory))
            {
                string ext = Path.GetExtension(file);
                if (!filters.Any(f => string.Equals(f, ext, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                string dest = Path.Combine(destDirectory, Path.GetFileName(file));
                File.Copy(file, dest, true);
                bytes += new FileInfo(file).Length;
                copied++;
            }

            Debug.Log($"[CdnSync] {platformFolder}/{Application.version}: 複製 {copied} 個檔案"
                      + $"({bytes / 1024f / 1024f:F1} MB)，略過 {skipped} 個建置副產物\n  -> {destDirectory}");
            return destDirectory;
        }

        /// <summary>
        /// 上傳到 Cloudflare Pages。目前只印出該怎麼做 —— 憑證與 wrangler 還沒接。
        ///
        /// 之後要接的話：npx wrangler pages deploy &lt;資料夾&gt; --project-name=&lt;專案名&gt;，
        /// 需要環境變數 CLOUDFLARE_API_TOKEN 與 CLOUDFLARE_ACCOUNT_ID。
        /// wrangler 會自己算差異、只上傳變更的檔案，比整包拖曳快得多。
        /// </summary>
        public static void Deploy(BuildProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.cdnRoot))
                return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string root = Path.Combine(projectRoot, profile.cdnRoot, "..");
            Debug.Log($"[CdnSync] CDN 資料夾已就緒，可以直接部署：\n  {Path.GetFullPath(root)}\n"
                      + "  (自動上傳尚未接上，目前請手動拖曳到 Cloudflare Pages)");
        }
    }
}
