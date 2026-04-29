using BMC.Core;
using BMC.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BMC.EditorExtensions
{
    [CustomEditor(typeof(UIImage))]
    [CanEditMultipleObjects]
    public class UIImageEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Localization Preview (Editor)", EditorStyles.boldLabel);

            // 使用橫向佈局排列按鈕
            EditorGUILayout.BeginHorizontal();

            DrawLangButton("EN", SystemLanguage.English);
            DrawLangButton("TC", SystemLanguage.ChineseTraditional);
            DrawLangButton("SC", SystemLanguage.ChineseSimplified);
            DrawLangButton("JP", SystemLanguage.Japanese);

            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 繪製特定語言的按鈕並執行更新
        /// </summary>
        private void DrawLangButton(string label, SystemLanguage lang)
        {
            if (GUILayout.Button(label, GUILayout.Height(25)))
            {
                UpdateLocalization(lang);
            }
        }

        private void UpdateLocalization(SystemLanguage lang)
        {
            // 初始化 Editor 環境下的資料
            // 直接借用 UITextEditor 中的設定，避免重複代碼
            LocalMgr.Instance.Load(new UITextEditor.EditorConfigLang(), lang);

            foreach (var obj in targets)
            {
                UIImage item = (UIImage)obj;
                if (item == null) continue;

                // 註冊復原動作
                Undo.RecordObject(item, $"Update Local Image to {lang}");

                // 執行在地化切換圖片
                item.Local();

                // 確保 Image 物件被標記為 Dirty，刷新 Editor 畫面
                EditorUtility.SetDirty(item);

                Image img = item.GetComponent<Image>();
                if (img != null)
                {
                    EditorUtility.SetDirty(img);
                }
            }
        }
    }
}