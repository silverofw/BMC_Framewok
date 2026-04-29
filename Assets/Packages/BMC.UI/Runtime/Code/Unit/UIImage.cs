using BMC.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.UI
{
    [RequireComponent(typeof(Image))]
    public class UIImage : MonoBehaviour
    {
        // 宣告一個可序列化的結構，讓 Unity Inspector 能夠顯示與編輯
        [Serializable]
        public struct LangSprite
        {
            public SystemLanguage language;
            public Sprite sprite;
        }

        [SerializeField]
        private List<LangSprite> localizedSprites = new();

        private Image image;

        private void Awake()
        {
            image = GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
            }
            Local();
        }

        // 改為大寫開頭的 public 方法，方便 Editor 腳本呼叫
        public void Local()
        {
            if (image == null) image = GetComponent<Image>();
            if (image == null) return;

            SystemLanguage crtLang = LocalMgr.Instance.CrtLang;

            // 尋找符合當前語言的圖片並替換
            foreach (var item in localizedSprites)
            {
                if (item.language == crtLang)
                {
                    if (item.sprite != null)
                    {
                        image.sprite = item.sprite;
                    }
                    break;
                }
            }
        }
    }
}