using BMC.Core;
using BMC.UIToolkit;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.Story
{
    /// <summary>
    /// UI Toolkit 版節點縮圖項目，對應 uGUI 版的 StoryLineItem。
    /// 項目是可重複建立的清單元件(不是面板單例)，仿 BMC.UIToolkit.JoypadItem 的模式：
    /// 直接繼承 UIButton、用 new() 建立、內部 UXML 片段在建構子裡 CloneTree(this) 進自己身上。
    /// </summary>
    public class StoryLineItem : UIButton
    {
        public const string TemplateAddress = "UIT_StoryLineItem";

        public string NodeId { get; private set; }

        protected UILabel InfoLabel { get; private set; }
        protected Image PreviewImage { get; private set; }
        protected VisualElement CurrentHighlight { get; private set; }

        /// <summary>
        /// 保留給子類擴充用的插槽(例如遊戲端 EFMStoryLineItem 的解鎖圖示)，base 完全不碰。
        /// </summary>
        protected VisualElement ExtraSlot { get; private set; }

        private Action clickAction;

        public StoryLineItem(VisualTreeAsset template)
        {
            AddToClassList("story-line-item");

            template.CloneTree(this);
            InfoLabel = this.Q<UILabel>("info-label");
            PreviewImage = this.Q<Image>("preview-image");
            CurrentHighlight = this.Q<VisualElement>("current-highlight");
            ExtraSlot = this.Q<VisualElement>("extra-slot");
        }

        public virtual void Init(StoryNode node, Action action)
        {
            NodeId = node.Id;
            ReplaceClickHandler(() => action?.Invoke());

            InfoLabel?.Set($"[{node.Id}]");
            SetCurrent(StoryPlayer.Instance.IsCrtNode(node));

            LoadPreview(node.PreviewImagePath).Forget();
        }

        public void SetCurrent(bool isCurrent) =>
            CurrentHighlight?.EnableInClassList("story-line-item--current", isCurrent);

        /// <summary>
        /// 整個換掉點擊行為，取代 uGUI 版 `btn.OnClick = ...` 的「整個覆蓋」語意——
        /// UIButton.OnClick 是 event，只能用 +=/-=，重複 Init() 需要先取消訂閱上一個 handler
        /// 才不會疊加。子類需要完全接管點擊時(例如節點上鎖要改成彈 Toast)可直接呼叫本方法。
        /// </summary>
        protected void ReplaceClickHandler(Action action)
        {
            if (clickAction != null)
                OnClick -= clickAction;
            clickAction = action;
            if (clickAction != null)
                OnClick += clickAction;
        }

        private async UniTaskVoid LoadPreview(string previewImagePath)
        {
            if (PreviewImage == null)
            {
                Log.Warning($"[StoryLineItem] preview-image 元件未找到 (NodeID: {NodeId})");
                return;
            }
            if (string.IsNullOrEmpty(previewImagePath))
            {
                Log.Warning($"[StoryLineItem] previewImagePath 是空的 (NodeID: {NodeId})");
                return;
            }

            // YooAsset 的 AddressByFileName 定址是「不含副檔名的檔名」，previewImagePath 存的是含副檔名的檔名
            string address = Path.GetFileNameWithoutExtension(previewImagePath);
            if (!ResMgr.Instance.Check(address))
            {
                Log.Warning($"[StoryLineItem] 找不到預覽圖位址 '{address}' (NodeID: {NodeId}, previewImagePath: {previewImagePath})");
                return;
            }

            var sprite = await ResMgr.Instance.LoadAssetAsync<Sprite>(address, false);
            if (panel == null || PreviewImage == null || sprite == null)
            {
                Log.Warning($"[StoryLineItem] 載入預覽圖 Sprite 失敗 '{address}' (NodeID: {NodeId})");
                return;
            }

            PreviewImage.sprite = sprite;
        }
    }
}
