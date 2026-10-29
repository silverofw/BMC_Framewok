using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BMC.Build.Editor
{
    /// <summary>
    /// 出母包時把 CDN 通道名(Build Profile 名稱)寫進 Resources，讓執行期組出跟 CdnSync 一樣的網址。
    ///
    /// 【為什麼寫進母包而不是靠 define 推】通道名只在出母包這一刻決定一次，之後母包凍結、
    /// 只出熱更也不會改它。執行期另寫一套 define 對照表，兩邊遲早會對不上，而且對不上時
    /// 玩家只會看到下載 404 然後掉回離線模式，不會有人發現。
    ///
    /// 【為什麼用完就刪】檔案內容跟著 Active Build Profile 變，留在專案裡每換一次 profile
    /// 就多一筆無意義的版控變更。只在 BuildPlayer 期間存在。
    ///
    /// 執行期讀法：Resources.Load&lt;TextAsset&gt;(ResourceName)?.text，讀不到代表沒分通道。
    /// 編輯器裡不會有這個檔，執行期請改用 Active Build Profile 的名稱。
    /// </summary>
    public static class CdnChannelStamp
    {
        public const string ResourceName = "BMCCdnChannel";

        const string Folder = "Assets/BMC.Generated";
        static string FilePath => Folder + "/Resources/" + ResourceName + ".txt";

        /// <summary>channel 為空時什麼都不做，回傳的 IDisposable 也是空操作。</summary>
        public static IDisposable Write(string channel)
        {
            if (string.IsNullOrEmpty(channel))
                return new Scope(false);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, channel);
            AssetDatabase.ImportAsset(FilePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[CdnChannelStamp] 母包 CDN 通道 = {channel}");
            return new Scope(true);
        }

        sealed class Scope : IDisposable
        {
            bool _active;
            public Scope(bool active) { _active = active; }

            public void Dispose()
            {
                if (!_active) return;
                _active = false;
                AssetDatabase.DeleteAsset(Folder);
            }
        }
    }
}
