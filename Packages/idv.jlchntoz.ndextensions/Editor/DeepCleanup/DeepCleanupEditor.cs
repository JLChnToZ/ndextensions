using UnityEditor;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(DeepCleanup))]
    public class DeepCleanupEditor : Editor {
        static I18N i18n;
        public override void OnInspectorGUI() {
            if (i18n == null) i18n = I18N.Instance;
            I18NEditor.DrawLocaleField();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(i18n["DeepCleanup:note"], MessageType.Info);
        }
    }
}