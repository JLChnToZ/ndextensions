using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(ParameterCompressor))]
    public class ParameterCompressorEditor : TagComponentEditor {
        static readonly GUIContent tempContent = new();
        ReorderableList parametersList;

        protected override void OnEnable() {
            base.OnEnable();
            parametersList = new(serializedObject, serializedObject.FindProperty(nameof(ParameterCompressor.parameters)), true, true, true, true) {
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawElement,
                elementHeightCallback = CalcElementHeight,
            };
        }

        protected override void DrawFields() {
            serializedObject.Update();
            parametersList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        void DrawHeader(Rect rect) {
            var sp = parametersList.serializedProperty;
            tempContent.text = sp.displayName;
            tempContent.tooltip = sp.tooltip;
            EditorGUI.LabelField(rect, tempContent, EditorStyles.boldLabel);
        }

        void DrawElement(Rect rect, int index, bool isActive, bool isFocused) {
            rect.height -= EditorGUIUtility.standardVerticalSpacing;
            var element = parametersList.serializedProperty.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(rect, element, GUIContent.none, true);
        }

        float CalcElementHeight(int index) {
            var element = parametersList.serializedProperty.GetArrayElementAtIndex(index);
            return EditorGUI.GetPropertyHeight(element, GUIContent.none, true);
        }
    }
}