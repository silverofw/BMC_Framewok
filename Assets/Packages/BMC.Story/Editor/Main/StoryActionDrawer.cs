using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    /// <summary>
    /// Story Action 編輯器面板的基底類別。
    /// 只要繼承此類別並放在專案中，StoryLineEditorWindow 就會自動透過 TypeCache 註冊並顯示在選單中。
    /// </summary>
    public abstract class StoryActionDrawer
    {
        // 顯示在「新增 Event」按鈕下拉選單中的路徑，例如 "Media/Play Video"
        public abstract string MenuPath { get; }

        // 對應的 Protobuf ActionCase
        public abstract StoryEvent.ActionOneofCase ActionCase { get; }

        // 當企劃點擊新增時，負責產生帶有預設值的 StoryEvent
        public abstract StoryEvent CreateNewEvent();

        // 負責繪製該 Action 的詳細屬性面板。如果有修改到資料，請回傳 true。
        // node: 當前節點 | evt: 當前事件 | window: 提供 Helper API (如 ID 選擇器) 的主視窗
        public abstract bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window);
    }
}