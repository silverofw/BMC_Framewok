using System.Collections.Generic;
using UnityEngine;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 熱更新畫面專用的內建多語表。
    /// Patch 場景執行時熱更 DLL（含專案的多語系統與存檔裡的語言設定）和 YooAsset 資源都還沒載入，
    /// 所以這裡另備一份小表，只收熱更新畫面會用到的字。
    /// 語言只看系統語言：有支援就用，否則英文。
    /// 專案要換字（例如標題）或加字，在 PatchView 開始顯示前呼叫 <see cref="Set"/>。
    /// </summary>
    public static class PatchLocal
    {
        private enum Lang { TC, EN, JP, SC }

        private static readonly Lang _lang = Detect();

        // 每列依序是 繁中、英文、日文、簡中
        private static readonly Dictionary<string, string[]> _table = new Dictionary<string, string[]>
        {
            { "PATCH_TITLE", new[] { "讀取中 ...", "Loading ...", "読み込み中 ...", "读取中 ..." } },

            { "PATCH_STEP_INIT", new[] { "初始化資源包", "Initializing resources", "リソースを初期化中", "初始化资源包" } },
            { "PATCH_STEP_REQUEST_VERSION", new[] { "取得資源版本", "Checking resource version", "リソースのバージョンを確認中", "获取资源版本" } },
            { "PATCH_STEP_UPDATE_MANIFEST", new[] { "更新資源清單", "Updating resource list", "リソースリストを更新中", "更新资源清单" } },
            { "PATCH_STEP_CREATE_DOWNLOADER", new[] { "檢查需要下載的資源", "Checking for downloads", "ダウンロードを確認中", "检查需要下载的资源" } },
            { "PATCH_STEP_DOWNLOADING", new[] { "開始下載資源", "Starting download", "ダウンロードを開始", "开始下载资源" } },
            { "PATCH_STEP_DOWNLOAD_OVER", new[] { "資源下載完成", "Download complete", "ダウンロード完了", "资源下载完成" } },
            { "PATCH_STEP_CLEAR_CACHE", new[] { "清理舊的快取檔案", "Clearing old cache", "古いキャッシュを削除中", "清理旧的缓存文件" } },
            { "PATCH_STEP_DONE", new[] { "更新完畢", "Update complete", "アップデート完了", "更新完毕" } },
            // {0}/{1} 檔案數，{2}/{3} MB
            { "PATCH_DOWNLOAD_PROGRESS", new[] { "下載中 {0}/{1}（{2}MB/{3}MB）", "Downloading {0}/{1} ({2}MB/{3}MB)", "ダウンロード中 {0}/{1}（{2}MB/{3}MB）", "下载中 {0}/{1}（{2}MB/{3}MB）" } },
            // {0} 遊戲版本，{1} 資源版本
            { "PATCH_VERSION_INFO", new[] { "版本 {0}　資源 {1}", "Version {0}  Resources {1}", "バージョン {0}　リソース {1}", "版本 {0}　资源 {1}" } },

            { "PATCH_MSG_TITLE", new[] { "提示", "Notice", "お知らせ", "提示" } },
            { "PATCH_BTN_OK", new[] { "確認", "OK", "確認", "确认" } },
            { "PATCH_BTN_RETRY", new[] { "重試", "Retry", "再試行", "重试" } },
            { "PATCH_BTN_DOWNLOAD", new[] { "下載", "Download", "ダウンロード", "下载" } },
            // {0} 檔案數，{1} MB
            { "PATCH_FOUND_UPDATE", new[] {
                "發現新的遊戲資源，共 {0} 個檔案、{1}MB，是否現在下載？",
                "New game data found: {0} files, {1}MB. Download now?",
                "新しいゲームデータがあります（{0} ファイル、{1}MB）。今すぐダウンロードしますか？",
                "发现新的游戏资源，共 {0} 个文件、{1}MB，是否现在下载？" } },
            { "PATCH_INIT_FAILED", new[] {
                "遊戲資源初始化失敗。",
                "Failed to initialize game data.",
                "ゲームデータの初期化に失敗しました。",
                "游戏资源初始化失败。" } },
            { "PATCH_VERSION_REQUEST_FAILED", new[] {
                "無法取得資源版本，請檢查網路連線。",
                "Couldn't check the resource version. Please check your network connection.",
                "リソースのバージョンを取得できませんでした。ネットワーク接続を確認してください。",
                "无法获取资源版本，请检查网络连接。" } },
            { "PATCH_MANIFEST_FAILED", new[] {
                "無法更新資源清單，請檢查網路連線。",
                "Couldn't update the resource list. Please check your network connection.",
                "リソースリストを更新できませんでした。ネットワーク接続を確認してください。",
                "无法更新资源清单，请检查网络连接。" } },
            // {0} 檔名
            { "PATCH_DOWNLOAD_FAILED", new[] {
                "檔案下載失敗：{0}",
                "Failed to download file: {0}",
                "ファイルのダウンロードに失敗しました：{0}",
                "文件下载失败：{0}" } },
            { "PLEASE_UPDATE_APPLICATION", new[] {
                "遊戲有新版本，請更新後再進入遊戲。",
                "A new version of the game is available. Please update before playing.",
                "新しいバージョンがあります。アップデートしてからプレイしてください。",
                "游戏有新版本，请更新后再进入游戏。" } },
        };

        /// <summary>
        /// 取得目前語言的文字；表裡沒有的 key 原樣傳回，方便看出漏了哪個字。
        /// </summary>
        public static string Get(string key)
        {
            if (key != null && _table.TryGetValue(key, out var texts))
                return texts[(int)_lang];
            return key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>
        /// 新增或覆蓋一個 key，依序傳入 繁中、英文、日文、簡中。
        /// </summary>
        public static void Set(string key, string tc, string en, string jp, string sc)
        {
            _table[key] = new[] { tc, en, jp, sc };
        }

        private static Lang Detect()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.ChineseSimplified:
                    return Lang.SC;
                case SystemLanguage.ChineseTraditional:
                case SystemLanguage.Chinese:
                    return Lang.TC;
                case SystemLanguage.Japanese:
                    return Lang.JP;
                default:
                    return Lang.EN;
            }
        }
    }
}
