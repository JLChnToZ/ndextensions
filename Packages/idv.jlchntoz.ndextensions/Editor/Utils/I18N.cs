using System;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using ThirdParties.LitJson;

namespace JLChnToZ.NDExtensions.Editors {
    public class I18N : ScriptableObject {
        const string ASSET_PATH = "Packages/idv.jlchntoz.ndextensions/Locale/I18N.asset";
        const string PREF_KEY = "nd.lang";
        const string DEFAULT_LANGUAGE = "en";
        static readonly GUIContent tempContent = new GUIContent();
        static I18N instance;
        [SerializeField] TextAsset i18nData;
        string[] languageNames;
        string[] languageKeys;
        readonly Dictionary<string, Dictionary<string, string>> i18nDict = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Type, (string[] names, int[] values)> enumCache = new Dictionary<Type, (string[], int[])>();
        [NonSerialized] string currentLanguage;

        public string CurrentLanguage {
            get => currentLanguage;
            set {
                currentLanguage = value;
                EditorPrefs.SetString(PREF_KEY, value);
                enumCache.Clear();
            }
        }

        public int LanguageIndex {
            get => Array.IndexOf(languageKeys, currentLanguage);
            set => CurrentLanguage = languageKeys[value];
        }

        public string[] LanguageNames => languageNames;

        public static I18N Instance {
            get {
                if (instance == null)
                    instance = AssetDatabase.LoadAssetAtPath<I18N>(ASSET_PATH);
                if (instance == null) {
                    instance = CreateInstance<I18N>();
                    instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return instance;
            }
        }

        public string this[string key] {
            get {
                if (i18nDict.TryGetValue(currentLanguage, out var langDict) &&
                    langDict.TryGetValue(key, out var value))
                    return value;
                if (i18nDict.TryGetValue(DEFAULT_LANGUAGE, out langDict) &&
                    langDict.TryGetValue(key, out value))
                    return value;
                return null;
            }
        }

        public string GetOrDefault(string key, string defaultValue = null) {
            var value = this[key];
            return string.IsNullOrEmpty(value) ? defaultValue ?? key : value;
        }

        public GUIContent GetContent(string key, string defaultValue = null) {
            GetContent(key, tempContent, defaultValue);
            return tempContent;
        }

        public void GetContent(string key, GUIContent output, string defaultValue = null) {
            output.text = GetOrDefault(key, defaultValue);
            output.tooltip = GetOrDefault($"{key}:tooltip", "");
            output.image = null;
        }

        void OnEnable() => Reload();

        public void Reload() {
            if (i18nData == null) return;
            var jsonData = JsonMapper.ToObject(i18nData.text);
            i18nDict.Clear();
            var comparer = StringComparer.OrdinalIgnoreCase;
            languageNames = new string[jsonData.Count];
            languageKeys = new string[jsonData.Count];
            int i = 0;
            foreach (var lang in jsonData.Keys) {
                var langDict = new Dictionary<string, string>(comparer);
                var langData = jsonData[lang];
                foreach (var key in langData.Keys)
                    switch (key) {
                        case "_alias":
                            foreach (JsonData aliasKey in langData[key])
                                alias[(string)aliasKey] = lang;
                            break;
                        case "_name":
                            languageNames[i] = (string)langData[key];
                            break;
                        default:
                            langDict[key] = (string)langData[key];
                            break;
                    }
                i18nDict[lang] = langDict;
                languageKeys[i] = lang;
                i++;
            }
            if (string.IsNullOrEmpty(currentLanguage)) {
                currentLanguage = CultureInfo.CurrentCulture.Name;
                currentLanguage = EditorPrefs.GetString(PREF_KEY, currentLanguage);
            }
            if (i18nDict.ContainsKey(currentLanguage)) return;
            if (alias.TryGetValue(currentLanguage, out var aliasLang) &&
                i18nDict.ContainsKey(aliasLang)) {
                currentLanguage = aliasLang;
                return;
            }
            if (currentLanguage.Length >= 2) {
                var regionless = currentLanguage.Substring(0, 2);
                if (i18nDict.ContainsKey(regionless)) {
                    currentLanguage = regionless;
                    return;
                }
            }
            currentLanguage = DEFAULT_LANGUAGE;
        }

        public void EnumFieldLayout(SerializedProperty enumProperty, string label = null) {
            enumProperty.GetFieldInfoAndStaticType(out var type);
            var localizedContent = GetContent(string.IsNullOrEmpty(label) ? enumProperty.name : label);
            if (type == null || !type.IsEnum) {
                EditorGUILayout.PropertyField(enumProperty, localizedContent);
                return;
            }
            if (!enumCache.TryGetValue(type, out var cache)) {
                var values = Enum.GetValues(type);
                var names = new string[values.Length];
                var iValues = new int[values.Length];
                for (int i = 0; i < values.Length; i++) {
                    var value = values.GetValue(i);
                    names[i] = GetOrDefault($"{type.Name}.{value}");
                    if (string.IsNullOrEmpty(names[i]))
                        names[i] = ObjectNames.NicifyVariableName(value.ToString());
                    iValues[i] = Convert.ToInt32(value);
                }
                cache = (names, iValues);
                enumCache[type] = cache;
            }
            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            using (var propScope = new EditorGUI.PropertyScope(rect, localizedContent, enumProperty))
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                rect = EditorGUI.PrefixLabel(rect, propScope.content);
                int index = Array.IndexOf(cache.values, enumProperty.intValue);
                if (index < 0) index = 0;
                index = EditorGUI.Popup(rect, index, cache.names);
                if (changeCheck.changed) enumProperty.intValue = cache.values[index];
            }
        }
    }

    [CustomEditor(typeof(I18N))]
    public class I18NEditor : Editor {
        SerializedProperty i18nData;

        void OnEnable() {
            i18nData = serializedObject.FindProperty("i18nData");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.HelpBox("This is an editor only scriptable object, it's used to store i18n data for editor only.", MessageType.Info);
            EditorGUILayout.PropertyField(i18nData);
            if (GUILayout.Button("Reload")) (target as I18N).Reload();
            serializedObject.ApplyModifiedProperties();
        }

        public static void DrawLocaleField() {
            var i18n = I18N.Instance;
            var languageIndex = i18n.LanguageIndex;
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                languageIndex = EditorGUILayout.Popup(i18n.GetOrDefault("Language"), languageIndex, i18n.LanguageNames);
                if (changeCheck.changed) i18n.LanguageIndex = languageIndex;
            }
            var machineTranslated = i18n["MachineTranslationMessage"];
            if (!string.IsNullOrEmpty(machineTranslated)) EditorGUILayout.HelpBox(machineTranslated, MessageType.Info);
        }
    }
}
