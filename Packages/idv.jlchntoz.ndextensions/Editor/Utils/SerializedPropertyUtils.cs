using System;
using System.Reflection;
using UnityEditor;

namespace JLChnToZ.NDExtensions.Editors {
    public static class SerializedPropertyUtils {
        delegate FieldInfo GetFieldInfoAndStaticTypeFromPropertyDelegate(SerializedProperty property, out Type type);

        static readonly GetFieldInfoAndStaticTypeFromPropertyDelegate getFieldInfoAndStaticTypeFromProperty;

        static SerializedPropertyUtils() {
            var methodInfo = Type.GetType("UnityEditor.ScriptAttributeUtility, UnityEditor", false)?
                .GetMethod("GetFieldInfoAndStaticTypeFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (methodInfo != null)
                getFieldInfoAndStaticTypeFromProperty = Delegate.CreateDelegate(
                    typeof(GetFieldInfoAndStaticTypeFromPropertyDelegate), methodInfo, false
                ) as GetFieldInfoAndStaticTypeFromPropertyDelegate;
        }

        public static FieldInfo GetFieldInfoAndStaticType(this SerializedProperty property, out Type type) {
            if (getFieldInfoAndStaticTypeFromProperty == null) {
                type = null;
                return null;
            }
            return getFieldInfoAndStaticTypeFromProperty(property, out type);
        }
    }
}