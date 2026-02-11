using bmc;
using BMC.Core;
using BMC.UI;
using Luban;
using UnityEditor;
using UnityEngine;

namespace BMC.EditorExtensions
{
    [CustomEditor(typeof(UIText))]
    [CanEditMultipleObjects]
    public class UITextEditor : Editor
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
            LocalMgr.Instance.CrtLang = lang;
            LocalMgr.Instance.Data = new EditorConfigLang();

            foreach (var obj in targets)
            {
                UIText item = (UIText)obj;
                if (item == null) continue;

                // 註冊復原動作
                Undo.RecordObject(item, $"Update Local to {lang}");

                // 執行在地化
                item.Local();

                // 如果 UIText 裡面有引用 TMP_Text，也要確保該 Text 物件被標記為 Dirty 才會刷新畫面
                EditorUtility.SetDirty(item);

                // 強制讓場景檢視視窗重繪，確保文字立即改變
                if (item.GetComponent<TMPro.TMP_Text>() != null)
                {
                    EditorUtility.SetDirty(item.GetComponent<TMPro.TMP_Text>());
                }
            }
        }

        // 保持你原本的 EditorConfigLang 邏輯不變
        public class EditorConfigLang : LangData
        {
            private static Tblocalization _cachedTbl;

            public override string Local(string key)
            {
                if (_cachedTbl == null)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/yoo/DefaultPackage/Config/tblocalization.bytes");
                    if (asset != null)
                    {
                        _cachedTbl = new Tblocalization(new ByteBuf(asset.bytes));
                    }
                    else
                    {
                        Debug.LogError("Failed to load: Assets/yoo/DefaultPackage/Config/tblocalization.bytes");
                        return key;
                    }
                }

                var c = _cachedTbl.GetOrDefault(key);
                if (c == null) return key;

                return LocalMgr.Instance.CrtLang switch
                {
                    SystemLanguage.ChineseTraditional => c.Tc,
                    SystemLanguage.ChineseSimplified => c.Sc,
                    SystemLanguage.Japanese => c.Jp,
                    _ => c.En,
                };
            }
        }
    }
}