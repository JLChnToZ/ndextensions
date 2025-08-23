using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(TagComponent), true)]
    public class TagComponentEditor : Editor {
        protected static I18N i18n;
        string typeName;

        public override void OnInspectorGUI() {
            if (i18n == null) i18n = I18N.Instance;
            I18NEditor.DrawLocaleField();
            EditorGUILayout.Space();
            DrawFields();
        }

        protected virtual void OnEnable() {
            typeName = target.GetType().Name;
        }

        protected virtual void DrawFields() {
            EditorGUILayout.LabelField("Test Title");
            var so = serializedObject;
            so.Update();
            var label = i18n[$"{typeName}:note"];
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.HelpBox(label, MessageType.Info);
            var iterator = so.GetIterator();
            iterator.NextVisible(true);
            if (iterator.NextVisible(false)) // Skip the first property which is the script reference
                while (iterator.NextVisible(false))
                    EditorGUILayout.PropertyField(iterator, true);
            so.ApplyModifiedProperties();
            EditorGUILayout.LabelField("Test Title");
        }
    }
}