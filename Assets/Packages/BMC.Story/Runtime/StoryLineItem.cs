using BMC.Core;
using BMC.UI;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.Story
{
    public class StoryLineItem : UI.UIPanel
    {
        [SerializeField] protected UIText info;
        [SerializeField] protected UIButton btn;
        [SerializeField] private GameObject select;
        [SerializeField] private Image preview;

        // 這些是 Runtime 必要的識別資料
        [HideInInspector] public string NodeID;
        [HideInInspector] public string VideoPath;

        public virtual void Init(StoryNode node, Action action)
        {
            NodeID = node.Id;
            btn.OnClick = () => action?.Invoke();

            if (info != null) info.Set($"[{node.Id}]");

            select.SetActive(StoryPlayer.Instance.IsCrtNode(node));

            LoadPreview(node.PreviewImagePath).Forget();
        }

        private async UniTaskVoid LoadPreview(string previewImagePath)
        {
            if (preview == null)
            {
                Log.Warning($"[StoryLineItem] preview 欄位未指定 (NodeID: {NodeID})");
                return;
            }
            if (string.IsNullOrEmpty(previewImagePath))
            {
                Log.Warning($"[StoryLineItem] previewImagePath 是空的 (NodeID: {NodeID})");
                return;
            }

            // YooAsset 的 AddressByFileName 定址是「不含副檔名的檔名」，previewImagePath 存的是含副檔名的檔名
            string address = Path.GetFileNameWithoutExtension(previewImagePath);
            if (!ResMgr.Instance.Check(address))
            {
                Log.Warning($"[StoryLineItem] 找不到預覽圖位址 '{address}' (NodeID: {NodeID}, previewImagePath: {previewImagePath})");
                return;
            }

            var sprite = await ResMgr.Instance.LoadAssetAsync<Sprite>(address, false);
            if (this == null || preview == null || sprite == null)
            {
                Log.Warning($"[StoryLineItem] 載入預覽圖 Sprite 失敗 '{address}' (NodeID: {NodeID})");
                return;
            }

            preview.sprite = sprite;
        }
    }
}