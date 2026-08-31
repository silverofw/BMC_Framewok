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
        /// 上傳到 Cloudflare Pages（Direct Upload）。
        ///
        /// 走 npx wrangler，不需要全域安裝。wrangler 會比對雜湊、只上傳變更過的檔案，
        /// 所以雖然每次都指定整個資料夾，實際傳輸量遠小於資料夾大小。
        ///
        /// 【憑證】不在這裡處理，交給 wrangler 自己解析：有 CLOUDFLARE_API_TOKEN
        /// 環境變數就用它，否則用 `npx wrangler login` 存在使用者家目錄的 OAuth 憑證。
        /// 兩種都不會進版控。
        ///
        /// 【為什麼不預設在每次出版時上傳】Cloudflare Pages 免費方案每月 500 次
        /// deployment，Direct Upload 也計入。反覆測試打包很容易吃掉配額，所以要明確
        /// 指定才會傳（選單的「上傳 CDN」或批次的 -bmcDeploy）。
        /// </summary>
        /// <returns>成功回傳 true；沒設定專案名稱視為刻意跳過，回傳 false。</returns>
        public static bool Deploy(BuildProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.cdnRoot))
                return false;

            if (string.IsNullOrWhiteSpace(profile.cdnProjectName))
            {
                Debug.LogWarning("[CdnSync] profile 沒有設定 cdnProjectName，略過上傳。"
                                 + "（Cloudflare Pages 後台的專案名稱，就是 <名稱>.pages.dev 那一段）");
                return false;
            }

            // cdnRoot 指到的是「平台/版本」的上一層（例如 bmc-meow-siege-cdn/CDN），
            // 而 Pages 要的是站台根目錄，也就是再往上一層。
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string siteRoot = Path.GetFullPath(Path.Combine(projectRoot, profile.cdnRoot, ".."));

            if (!Directory.Exists(siteRoot))
            {
                Debug.LogError($"[CdnSync] 要上傳的資料夾不存在：{siteRoot}");
                return false;
            }

            string args = $"wrangler pages deploy \"{siteRoot}\" "
                        + $"--project-name={profile.cdnProjectName} --commit-dirty=true";

            Debug.Log($"[CdnSync] 開始上傳 -> {profile.cdnProjectName}"
                             + System.Environment.NewLine + "  " + siteRoot);
            return RunNpx(args, projectRoot);
        }

        /// <summary>
        /// 執行 npx。Windows 的 npx 是批次檔，不能直接當成執行檔啟動，要透過 cmd。
        /// </summary>
        static bool RunNpx(string arguments, string workingDirectory)
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

#if UNITY_EDITOR_WIN
            info.FileName = "cmd.exe";
            info.Arguments = "/c npx " + arguments;
#else
            info.FileName = "npx";
            info.Arguments = arguments;
#endif

            try
            {
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    if (process == null)
                    {
                        Debug.LogError("[CdnSync] 無法啟動 npx，請確認已安裝 Node.js。");
                        return false;
                    }

                    // 【一定要非同步讀】ReadToEnd() 會阻塞到程序結束，那樣底下的
                    // WaitForExit(逾時) 永遠等不到機會執行，逾時形同虛設 ——
                    // 而卡在等登入正是最需要逾時的情況。
                    var stdoutBuffer = new System.Text.StringBuilder();
                    var stderrBuffer = new System.Text.StringBuilder();
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuffer.AppendLine(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuffer.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // wrangler 在沒有憑證時會想開瀏覽器做 OAuth，批次模式下會一直等下去。
                    // 給一個上限，逾時就中止並說明原因，而不是讓打包流程整個卡住。
                    if (!process.WaitForExit(TimeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        Debug.LogError($"[CdnSync] 上傳逾時（{TimeoutMs / 60000} 分鐘）。"
                                       + "常見原因是還沒登入而 wrangler 在等瀏覽器授權 —— "
                                       + "請先在終端機執行一次 npx wrangler login，或設定 CLOUDFLARE_API_TOKEN。");
                        return false;
                    }

                    var stdout = stdoutBuffer.ToString();
                    var stderr = stderrBuffer.ToString();

                    if (!string.IsNullOrWhiteSpace(stdout))
                        Debug.Log("[CdnSync] " + stdout.Trim());

                    if (process.ExitCode != 0)
                    {
                        Debug.LogError($"[CdnSync] 上傳失敗（exit {process.ExitCode}）"
                                               + System.Environment.NewLine + stderr.Trim());
                        return false;
                    }

                    // wrangler 把進度訊息寫在 stderr，成功時那不是錯誤
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Debug.Log("[CdnSync] " + stderr.Trim());

                    Debug.Log("[CdnSync] 上傳完成。");
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CdnSync] 執行 npx 失敗：{e.Message}");
                return false;
            }
        }

        /// <summary>上傳的時間上限。大部分只會傳變更的少數檔案，真的跑滿通常代表卡在等登入。</summary>
        const int TimeoutMs = 10 * 60 * 1000;
    }
}
