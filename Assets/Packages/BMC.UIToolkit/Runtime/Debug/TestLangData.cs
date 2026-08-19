using System.Collections.Generic;
using BMC.Core;
using UnityEngine;

namespace BMC.UIToolkit
{
    /// <summary>
    /// 除錯用的內建語系表。
    ///
    /// 專案正式的語系來源是 Luban 的 Tblocalization（由 ConfigLang 讀取），
    /// 但那張表目前只有 Continue／Start／Item_1 之類的遊戲內容鍵值，
    /// 沒有 UI 通用的確認／取消，因此無法驗證介面的多語言流程。
    ///
    /// 這裡提供一份只存在於除錯組件的小型語系表，涵蓋 BMC.UIToolkit
    /// 內建面板用到的鍵值，讓「切語言 → 介面即時換字」可以被實際看到。
    /// 按下「測試多語言(Continue)」載入 ConfigLang 後就會被取代，不影響正式流程。
    /// </summary>
    public class TestLangData : LangData
    {
        private static readonly Dictionary<string, Dictionary<SystemLanguage, string>> Table = new()
        {
            ["COMMON_OK"] = new()
            {
                [SystemLanguage.English] = "OK",
                [SystemLanguage.ChineseTraditional] = "確認",
                [SystemLanguage.ChineseSimplified] = "确认",
                [SystemLanguage.Japanese] = "確認",
            },
            ["COMMON_CANCEL"] = new()
            {
                [SystemLanguage.English] = "Cancel",
                [SystemLanguage.ChineseTraditional] = "取消",
                [SystemLanguage.ChineseSimplified] = "取消",
                [SystemLanguage.Japanese] = "キャンセル",
            },
            ["COMMON_CLOSE"] = new()
            {
                [SystemLanguage.English] = "Close",
                [SystemLanguage.ChineseTraditional] = "關閉",
                [SystemLanguage.ChineseSimplified] = "关闭",
                [SystemLanguage.Japanese] = "閉じる",
            },
            ["PREVIEW_LOCALIZED"] = new()
            {
                [SystemLanguage.English] = "This line follows the current language.",
                [SystemLanguage.ChineseTraditional] = "這一行會跟著目前語言變動。",
                [SystemLanguage.ChineseSimplified] = "这一行会跟着当前语言变动。",
                [SystemLanguage.Japanese] = "この行は現在の言語に追従します。",
            },
        };

        /// <summary>
        /// 查不到時原樣回傳 key，與 ConfigLang 的行為一致——
        /// UI 元件靠這個約定判斷「查無此鍵」並保留原本編排的文字。
        /// </summary>
        public override string Local(string key)
        {
            if (key != null && Table.TryGetValue(key, out var langs))
            {
                if (langs.TryGetValue(LocalMgr.Instance.CrtLang, out var value))
                    return value;
                if (langs.TryGetValue(SystemLanguage.English, out var fallback))
                    return fallback;
            }
            return key;
        }
    }
}
