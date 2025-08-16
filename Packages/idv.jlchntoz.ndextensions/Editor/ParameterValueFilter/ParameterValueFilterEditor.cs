using UnityEngine;
using UnityEditor;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(ParameterValueFilter))]
    public class ParameterValueFilterEditor : TagComponentEditor {
        SerializedProperty parameterParameter;
        SerializedProperty sourceParameterParameter;
        SerializedProperty minValueParameter;
        SerializedProperty maxValueParameter;
        SerializedProperty smoothValueParameter;
        SerializedProperty smoothParameterParameter;
        SerializedProperty remapValuesParameter;
        SerializedProperty remapMinParameter;
        SerializedProperty remapMaxParameter;
        SerializedProperty smoothTypeParameter;
        SerializedProperty timeBasedParameter;

        protected override void OnEnable() {
            base.OnEnable();
            parameterParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.parameter));
            sourceParameterParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.sourceParameter));
            minValueParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.minValue));
            maxValueParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.maxValue));
            smoothValueParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.smoothValue));
            smoothParameterParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.smoothParameter));
            remapValuesParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.remapValues));
            remapMinParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.remapMin));
            remapMaxParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.remapMax));
            smoothTypeParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.smoothType));
            timeBasedParameter = serializedObject.FindProperty(nameof(ParameterValueFilter.timeBased));
        }

        protected override void DrawFields() {
            serializedObject.Update();

            EditorGUILayout.HelpBox(i18n["ParameterValueFilter:note"], MessageType.Info);

            EditorGUILayout.PropertyField(parameterParameter, i18n.GetContent("ParameterValueFilter.parameter"));
            EditorGUILayout.PropertyField(sourceParameterParameter, i18n.GetContent("ParameterValueFilter.sourceParameter"));

            EditorGUILayout.PropertyField(minValueParameter, i18n.GetContent("ParameterValueFilter.minValue"));
            EditorGUILayout.PropertyField(maxValueParameter, i18n.GetContent("ParameterValueFilter.maxValue"));

            i18n.EnumFieldLayout(smoothTypeParameter, "ParameterValueFilter.smoothType");

            var smoothType = (SmoothType)smoothTypeParameter.intValue;

            if (smoothType != SmoothType.None) {
                using (new EditorGUILayout.HorizontalScope()) {
                    bool useValue = string.IsNullOrEmpty(smoothParameterParameter.FindPropertyRelative(nameof(AnimatorParameterRef.name)).stringValue);
                    if (useValue) EditorGUILayout.PropertyField(smoothValueParameter, i18n.GetContent("ParameterValueFilter.smoothValue"));
                    EditorGUILayout.PropertyField(smoothParameterParameter, useValue ? GUIContent.none : i18n.GetContent("ParameterValueFilter.smoothParameter"));
                }
                EditorGUILayout.PropertyField(timeBasedParameter, i18n.GetContent("ParameterValueFilter.timeBased"));
            }

            EditorGUILayout.PropertyField(remapValuesParameter, i18n.GetContent("ParameterValueFilter.remapValues"));
            using (new EditorGUI.IndentLevelScope())
                if (remapValuesParameter.boolValue) {
                    EditorGUILayout.PropertyField(remapMinParameter, i18n.GetContent("ParameterValueFilter.remapMin"));
                    EditorGUILayout.PropertyField(remapMaxParameter, i18n.GetContent("ParameterValueFilter.remapMax"));
                } else {
                    remapMinParameter.floatValue = minValueParameter.floatValue;
                    remapMaxParameter.floatValue = maxValueParameter.floatValue;
                }

            serializedObject.ApplyModifiedProperties();
        }
    }
}