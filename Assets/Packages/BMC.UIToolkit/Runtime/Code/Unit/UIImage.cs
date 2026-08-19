using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using BMC.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit
{
    /// <summary>
    /// UI Toolkit 版多語言圖片，對應 uGUI 版 BMC.UI 的 UIImage。
    ///
    /// 提供兩種指定方式：
    /// 1. local-key 屬性：譯文表回傳「圖片的資源位址」，透過 ResMgr 載入。
    ///    這是 UXML 上唯一可行的寫法，也讓換圖與換字共用同一份譯文表。
    /// 2. SetLocalizedSprites：由程式直接給定各語言的 Sprite，
    ///    對應 uGUI 版在 Inspector 逐語言指派的用法。
    ///
    /// 之所以沒有在 UXML 上提供「逐語言指派 Sprite」的清單，是因為 UXML 屬性
    /// 無法表達物件清單；需要那種用法時請走第 2 種方式。
    /// </summary>
    [UxmlElement]
    public partial class UIImage : Image
    {
        private string localKeyValue;
        private Dictionary<SystemLanguage, Sprite> localizedSprites;

        /// <summary>
        /// 多語言鍵值，譯文內容視為圖片的資源位址。留空則維持原本的圖片。
        /// </summary>
        [UxmlAttribute]
        public string localKey
        {
            get => localKeyValue;
            set
            {
                localKeyValue = value;
                ApplyLocalization();
            }
        }

        public UIImage()
        {
            // 圖片預設不吃點擊，行為比照 uGUI 版在 Awake 關掉 raycastTarget
            pickingMode = PickingMode.Ignore;
            this.BindLocalization(ApplyLocalization);
        }

        /// <summary>
        /// 指定各語言對應的圖片，對應 uGUI 版的 localizedSprites 清單。
        /// 設定後會立即依目前語言套用。
        /// </summary>
        public void SetLocalizedSprites(IEnumerable<KeyValuePair<SystemLanguage, Sprite>> sprites)
        {
            localizedSprites = new Dictionary<SystemLanguage, Sprite>();
            if (sprites != null)
            {
                foreach (var pair in sprites)
                    localizedSprites[pair.Key] = pair.Value;
            }
            ApplyLocalization();
        }

        public void ApplyLocalization()
        {
            // 程式指定的清單優先：它是明確的逐語言對應，語意比位址查表精確
            if (localizedSprites != null &&
                localizedSprites.TryGetValue(LocalMgr.Instance.CrtLang, out var localSprite))
            {
                if (localSprite != null)
                    sprite = localSprite;
                return;
            }

            if (string.IsNullOrEmpty(localKeyValue))
                return;
            if (!LocalMgr.Instance.IsReady)
                return;

            LoadByAddress(LocalMgr.Instance.Local(localKeyValue)).Forget();
        }

        private async UniTaskVoid LoadByAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                return;

            // 先驗位址：譯文表可能給了不存在的圖名，
            // 直接丟給 YooAsset 會噴出難以追查的載入錯誤
            if (!ResMgr.Instance.Check(address))
            {
                Log.Warning($"[UIImage] 圖片位址不存在: '{address}' (key: {localKeyValue})");
                return;
            }

            var loaded = await ResMgr.Instance.LoadAssetAsync<Sprite>(address, false);
            if (loaded == null)
                return;

            // 載入期間元件可能已被移除，或語言又變了，這裡再確認一次才套用
            if (panel == null)
                return;

            sprite = loaded;
        }
    }
}
