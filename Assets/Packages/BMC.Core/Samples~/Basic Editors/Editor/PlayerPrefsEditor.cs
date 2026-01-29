//
//  PlayerPrefsEditor
//
//

// ------------------------------
// ※ サマリー
// PlayerPrefsの内容を表示・変更するEditorWindow。
// データ保存先をPlayerPrefsにしていれば、そのデータ操作ができます。
// 開発中に何らかのパラメタをエディタ起動中に変更して動作を見る用途にも使えます。
// ------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityEditor;
using Microsoft.Win32;

namespace cardooo.editors
{
    public class PlayerPrefsEditor : EditorWindow
    {
        // ==========================================================
        // enum, class fields
        // ==========================================================

        private enum PrefsType
        {
            Float,
            Int,
            String
        }

        private enum Mode
        {
            Edit,
            HowToUse,
            Create,
            DeleteAll,
        }

        private class PrefsEntry
        {
            public class PrefsValue
            {
                public PrefsType PrefsType   { get; set; }
                public string    StringValue { get; set; }
                public int       IntValue    { get; set; }
                public float     FloatValue  { get; set; }
            }

            public string     Name              { get; set; }
            public bool       IsMarkedToDelete  { get; set; }
            public PrefsValue Current           { get; }
            public PrefsValue Initial           { get; }
            public PrefsType  CurrentType       => Current.PrefsType;
            public string     CurrentTypeString => Current.PrefsType.ToString();

            public string ValueAsString
            {
                get
                {
                    switch (Current.PrefsType)
                    {
                        case PrefsType.Float:
                            return Current.FloatValue.ToString();
                        case PrefsType.Int:
                            return Current.IntValue.ToString();
                        case PrefsType.String:
                        default:
                            return Current.StringValue;
                    }
                }
            }

            public bool HasChanged
            {
                get
                {
                    if (Current.PrefsType != Initial.PrefsType)
                    {
                        return true;
                    }

                    switch (Current.PrefsType)
                    {
                        case PrefsType.Float:
                            return Current.FloatValue != Initial.FloatValue;
                        case PrefsType.Int:
                            return Current.IntValue != Initial.IntValue;
                        case PrefsType.String:
                        default:
                            return Current.StringValue != Initial.StringValue;
                    }
                }
            }

            public PrefsEntry(string name = "", PrefsType prefsType = PrefsType.Int, string valueStr = "0")
            {
                Name = name;
                Current   = new PrefsValue();
                Initial   = new PrefsValue();
                switch (prefsType)
                {
                    case PrefsType.Int:
                        Current.IntValue  = Initial.IntValue  = int.Parse(valueStr);
                        Current.PrefsType = Initial.PrefsType = PrefsType.Int;
                        break;
                    case PrefsType.Float:
                        Current.FloatValue = Initial.FloatValue = float.Parse(valueStr);
                        Current.PrefsType  = Initial.PrefsType  = PrefsType.Float;
                        break;
                    case PrefsType.String:
                    default:
                        Current.StringValue = Initial.StringValue = valueStr;
                        Current.PrefsType   = Initial.PrefsType   = PrefsType.String;
                        break;
                }
            }

            public void Reset()
            {
                Current.IntValue    = Initial.IntValue;
                Current.StringValue = Initial.StringValue;
                Current.FloatValue  = Initial.FloatValue;
                Current.PrefsType   = Initial.PrefsType;
            }

            public void Save()
            {
                Initial.IntValue    = Current.IntValue;
                Initial.StringValue = Current.StringValue;
                Initial.FloatValue  = Current.FloatValue;
                Initial.PrefsType   = Current.PrefsType;
            }
        }

        // ==========================================================
        // serialize fields
        // ==========================================================


        // ==========================================================
        // variables
        // ==========================================================

        private List<PrefsEntry> m__prefsEntryList;

        private Mode mCurrentMode = Mode.Edit;

        private PrefsEntry mNewPref;

        private Vector2 mScrollPosition;

        // ==========================================================
        // parameters
        // ==========================================================

        private List<PrefsEntry> prefsEntryList
        {
            get
            {
                if (m__prefsEntryList == null)
                {
                    m__prefsEntryList = new List<PrefsEntry>();
                }

                return m__prefsEntryList;
            }
        }

        // ==========================================================
        // public methods
        // ==========================================================


        // ==========================================================
        // protected methods
        // ==========================================================


        // ==========================================================
        // private methods
        // ==========================================================

        [MenuItem("BMC/Tools/PlayerPrefsEditor")]
        private static void Init()
        {
            GetWindow<PlayerPrefsEditor>("PlayerPrefs");
        }

        private void OnGUI()
        {
            if (m__prefsEntryList == null)
            {
                RestorePlayerPrefs();
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("HowToUse", EditorStyles.toolbarButton))
            {
                mCurrentMode = Mode.HowToUse;
            }

            if (GUILayout.Button("Create", EditorStyles.toolbarButton))
            {
                mNewPref     = new PrefsEntry();
                mCurrentMode = Mode.Create;
                RepaintAndResetFocus();
            }

            if (GUILayout.Button("Delete All", EditorStyles.toolbarButton))
            {
                mCurrentMode = Mode.DeleteAll;
                RepaintAndResetFocus();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton))
            {
                SaveAll();
                RepaintAndResetFocus();
            }

            if (GUILayout.Button("Restore", EditorStyles.toolbarButton))
            {
                RestorePlayerPrefs();
                RepaintAndResetFocus();
            }

            GUILayout.EndHorizontal();

            if (mCurrentMode == Mode.HowToUse)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("使い方", EditorStyles.boldLabel);

                string howToUseString = @"このEditorWindowではPlayerPrefsの取得・追加・更新・削除ができます。

<Create>
PlayerPrefsを新規作成します。
NameとInitialValueを設定していない場合、または既存のNameをしている場合は作成できません。
confirm時、Saveが実行されます。

<DeleteAll>
全てのPlayerPrefsを削除します。
confirm時、Saveが実行されます。

<Save>
変更状態で黄色くなっているエントリと、削除状態で赤くなっているエントリが確定されます。
PlayerPrefs.Save()が呼ばれます。

<Restore>
PlayerPrefsを端末から取得し直します。
実行すると、Playモードを開始した時点の状態に戻ります。
confirm時、Saveが実行されます。";

                GUILayout.TextArea(howToUseString);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close"))
                {
                    mCurrentMode = Mode.Edit;
                    RepaintAndResetFocus();
                }

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            else if (mCurrentMode == Mode.DeleteAll)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Delete All PlayerPrefs", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Confirm"))
                {
                    DeleteAll();
                    mCurrentMode = Mode.Edit;
                    RepaintAndResetFocus();
                }

                if (GUILayout.Button("Cancel"))
                {
                    mCurrentMode = Mode.Edit;
                    RepaintAndResetFocus();
                }

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            else if (mCurrentMode == Mode.Create)
            {
                if (mNewPref == null)
                {
                    mNewPref = new PrefsEntry();
                }

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Create New PlayerPref", EditorStyles.boldLabel);
                mNewPref.Name = EditorGUILayout.TextField("Name: ", mNewPref.Name);
                GUILayout.BeginHorizontal();
                switch (mNewPref.Current.PrefsType)
                {
                    case PrefsType.Int:
                        mNewPref.Current.IntValue = EditorGUILayout.IntField("Initial Value: ", mNewPref.Current.IntValue);
                        break;
                    case PrefsType.Float:
                        mNewPref.Current.FloatValue = EditorGUILayout.FloatField("Initial Value: ", mNewPref.Current.FloatValue);
                        break;
                    case PrefsType.String:
                    default:
                        mNewPref.Current.StringValue = EditorGUILayout.TextField("Initial Value: ", mNewPref.Current.StringValue);
                        break;
                }

                mNewPref.Current.PrefsType = (PrefsType)EditorGUILayout.EnumPopup(mNewPref.Current.PrefsType, GUILayout.MaxWidth(80));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Confirm"))
                {
                    if (string.IsNullOrEmpty(mNewPref.Name) || string.IsNullOrEmpty(mNewPref.ValueAsString))
                    {
                        UnityEngine.Debug.LogWarning("追加に失敗しました。名前か値が正しく設定されていません");
                    }
                    else if (prefsEntryList.Any(entry => entry.Name == mNewPref.Name))
                    {
                        UnityEngine.Debug.LogWarning("追加に失敗しました。同じ名前のPlayerPrefsが既に存在しています。");
                    }
                    else
                    {
                        prefsEntryList.Add(new PrefsEntry(mNewPref.Name, mNewPref.CurrentType, mNewPref.ValueAsString));
                        SaveAll();
                        mCurrentMode = Mode.Edit;
                        RepaintAndResetFocus();
                    }
                }

                if (GUILayout.Button("Cancel"))
                {
                    mCurrentMode = Mode.Edit;
                    RepaintAndResetFocus();
                }

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            mScrollPosition = GUILayout.BeginScrollView(mScrollPosition);
            if (prefsEntryList.Count == 0)
            {
                GUILayout.Label("No PlayerPrefs Found", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < prefsEntryList.Count; i++)
                {
                    GUILayout.BeginHorizontal(GUILayout.MinHeight(18));
                    if (prefsEntryList[i].IsMarkedToDelete)
                    {
                        GUI.color = Color.red;
                    }
                    else if (prefsEntryList[i].HasChanged)
                    {
                        GUI.color = Color.yellow;
                    }

                    float originalWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = (int)(position.width / 2f);
                    const int MaxWidth = 2000;
                    switch (prefsEntryList[i].Current.PrefsType)
                    {
                        case PrefsType.Int:
                            prefsEntryList[i].Current.IntValue = EditorGUILayout.IntField(prefsEntryList[i].Name, prefsEntryList[i].Current.IntValue,
                                EditorStyles.textField, GUILayout.MaxWidth(MaxWidth));
                            break;
                        case PrefsType.Float:
                            prefsEntryList[i].Current.FloatValue = EditorGUILayout.FloatField(prefsEntryList[i].Name, prefsEntryList[i].Current.FloatValue,
                                EditorStyles.textField, GUILayout.MaxWidth(MaxWidth));
                            break;
                        case PrefsType.String:
                        default:
                            prefsEntryList[i].Current.StringValue = EditorGUILayout.TextField(prefsEntryList[i].Name, prefsEntryList[i].Current.StringValue,
                                EditorStyles.textField, GUILayout.MaxWidth(MaxWidth));
                            break;
                    }

                    EditorGUIUtility.labelWidth = originalWidth;
                    GUILayout.FlexibleSpace();
                    prefsEntryList[i].Current.PrefsType = (PrefsType)EditorGUILayout.EnumPopup(prefsEntryList[i].Current.PrefsType, GUILayout.MaxWidth(80));
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(16), GUILayout.Height(16)))
                    {
                        prefsEntryList[i].IsMarkedToDelete = !prefsEntryList[i].IsMarkedToDelete;
                        RepaintAndResetFocus();
                    }

                    GUILayout.EndHorizontal();
                    GUI.color = Color.white;
                }
            }

            GUILayout.EndScrollView();
        }

        private void RepaintAndResetFocus()
        {
            Repaint();
            GUI.FocusControl("");
        }

        private void SaveAll()
        {
            // 削除するので後ろから順番に調べる
            for (int i = prefsEntryList.Count - 1; i >= 0; i--)
            {
                PrefsEntry info = prefsEntryList[i];

                if (info.IsMarkedToDelete)
                {
                    PlayerPrefs.DeleteKey(info.Name);
                    prefsEntryList.RemoveAt(i);
                    continue;
                }

                switch (info.Current.PrefsType)
                {
                    case PrefsType.Int:
                        PlayerPrefs.SetInt(info.Name, info.Current.IntValue);
                        break;
                    case PrefsType.Float:
                        PlayerPrefs.SetFloat(info.Name, info.Current.FloatValue);
                        break;
                    case PrefsType.String:
                    default:
                        PlayerPrefs.SetString(info.Name, info.Current.StringValue);
                        break;
                }

                info.Save();
            }

            PlayerPrefs.Save();
        }

        private void DeleteAll()
        {
            for (int i = prefsEntryList.Count - 1; i >= 0; i--)
            {
                PrefsEntry info = prefsEntryList[i];
                PlayerPrefs.DeleteKey(info.Name);
                prefsEntryList.RemoveAt(i);
            }
        }

        private void RestorePlayerPrefs()
        {
            prefsEntryList.Clear();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                RetrieveWindowsPlayerPrefs();
            }
            else
            {
                RetrieveMacPlayerPrefs();
            }

            // リストアした状態で保存をする。
            SaveAll();
        }

        private void RetrieveMacPlayerPrefs()
        {
            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            string plistPath =
                homePath + "/Library/Preferences/unity." + PlayerSettings.companyName + "." + PlayerSettings.productName + ".plist";
            Process          process          = new Process();
            ProcessStartInfo processStartInfo = new ProcessStartInfo("plutil", "-convert xml1 \"" + plistPath + "\"");
            process.StartInfo = processStartInfo;
            process.Start();
            process.WaitForExit();

            StreamReader sr        = new StreamReader(plistPath);
            string       pListData = sr.ReadToEnd();

            XmlDocument xml = new XmlDocument();
            xml.LoadXml(pListData);

            XmlElement plist = xml["plist"];
            if (plist == null)
            {
                UnityEngine.Debug.LogWarning("plistファイルの読み込みに失敗しました.");
                return;
            }

            XmlNode node = plist["dict"].FirstChild;
            while (node != null)
            {
                string name = node.InnerText;
                node = node.NextSibling;
                PrefsType  prefsType = GetPrefsTypeByNodeName(node.Name);
                PrefsEntry pref      = new PrefsEntry(name, prefsType, node.InnerText);
                node = node.NextSibling;
                prefsEntryList.Add(pref);
            }

            Process.Start("plutil", " -convert binary1 \"" + plistPath + "\"");
        }

        private void RetrieveWindowsPlayerPrefs()
        {
            var regKey = 
#if UNITY_EDITOR
                $@"Software\Unity\UnityEditor\{PlayerSettings.companyName}\{PlayerSettings.productName}";
#else
                $@"Software\{PlayerSettings.companyName}\{PlayerSettings.productName}";
#endif
            RegistryKey key = Registry.CurrentUser.OpenSubKey(regKey);

            foreach (string subkeyName in key.GetValueNames())
            {
                string keyName = subkeyName.Substring(0, subkeyName.LastIndexOf("_"));
                string val     = GetRegistryString(key, subkeyName);

                // RegistryKey の型を取得することはMonoではできないので、推測する
                int       testInt    = -1;
                PrefsType newType    = PrefsType.String;
                bool      couldBeInt = int.TryParse(val, out testInt);

                if (!float.IsNaN(PlayerPrefs.GetFloat(keyName, float.NaN)))
                {
                    val     = PlayerPrefs.GetFloat(keyName).ToString();
                    newType = PrefsType.Float;
                }
                else if (couldBeInt && (PlayerPrefs.GetInt(keyName, testInt + 1) == testInt))
                {
                    newType = PrefsType.Int;
                }
                else
                {
                    newType = PrefsType.String;
                }

                PrefsEntry pref = new PrefsEntry(keyName, newType, val);
                prefsEntryList.Add(pref);
            }
        }

        private static string GetRegistryString(RegistryKey regKey, string name)
        {
            var value = regKey.GetValue(name);
            switch (regKey.GetValueKind(name))
            {
                case RegistryValueKind.Binary:
                    var bytes = (byte[])value;
                    return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                default:
                    return value.ToString();
            }
       }

        private PrefsType GetPrefsTypeByNodeName(string name)
        {
            switch (name)
            {
                case "integer":
                    return PrefsType.Int;
                case "real":
                    return PrefsType.Float;
                case "string":
                    return PrefsType.String;
                default:
                    UnityEngine.Debug.LogWarning(string.Format("PlayerPrefsに不正なNodeNameが見つかりました Name:{0}", name));
                    return PrefsType.String;
            }
        }
    }
}
