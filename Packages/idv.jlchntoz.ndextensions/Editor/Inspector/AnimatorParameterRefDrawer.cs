using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomPropertyDrawer(typeof(AnimatorParameterRef), true)]
    [CustomPropertyDrawer(typeof(AnimatorParameterRefUsageAttribute), true)]
    public class AnimatorParameterRefDrawer : PropertyDrawer {
        static readonly ConditionalWeakTable<Component, ParameterCache> parameterCacheMap = new();
        static readonly GUIContent tempContent = new();
        static GUIStyle textFieldDropDownTextStyle, textFieldDropDownStyle;

        static ParameterCache GetCache(Component target, Type[] ignoreComponents = null, bool forceRefresh = false) {
            if (target == null) return null;
            var animator = target.GetComponentInParent<Animator>(true);
            if (animator != null) target = animator;
#if VRC_SDK_VRCSDK3
            var descriptor = target.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            if (descriptor != null) target = descriptor;
#endif
            if (!parameterCacheMap.TryGetValue(target, out var cache)) {
                cache = new ParameterCache(target.gameObject, ignoreComponents);
                parameterCacheMap.Add(target, cache);
            }
            if (forceRefresh) cache.Refresh();
            return cache;
        }

        public static void Draw(
            Rect position,
            SerializedProperty property,
            GUIContent label = null,
            Type[] ignoreComponents = null,
            ParameterType enforcedType = ParameterType.None
        ) {
            var target = property.serializedObject.targetObject as Component;
            using (new EditorGUI.PropertyScope(position, label, property)) {
                if (label == null || !string.IsNullOrEmpty(label.text))
                    position = EditorGUI.PrefixLabel(position, label);
                var nameProp = property.FindPropertyRelative(nameof(AnimatorParameterRef.name));
                var sourceProp = property.FindPropertyRelative(nameof(AnimatorParameterRef.source));
                var typeProp = property.FindPropertyRelative(nameof(AnimatorParameterRef.type));
                var nameValue = nameProp.stringValue;
                var sourceValue = sourceProp.objectReferenceValue as Component;
                var parameterCache = GetCache(target, ignoreComponents);
                var synchronizeState = enforcedType & ParameterType.SynchronizeSpecified;
                enforcedType &= ParameterType.AllTypes;
                bool isTypeEnforced = enforcedType != ParameterType.None;
                if (!isTypeEnforced && parameterCache != null) {
                    if (parameterCache.Parameters.TryGetValue((nameValue, sourceValue), out var _enforcedType))
                        enforcedType = _enforcedType.type.ToParameterType();
                    else {
                        if (parameterCache.GuessedComponents.TryGetValue(nameValue, out sourceValue) &&
                            parameterCache.Parameters.TryGetValue((nameValue, sourceValue), out _enforcedType))
                            enforcedType = _enforcedType.type.ToParameterType();
                        sourceProp.objectReferenceValue = sourceValue;
                    }
                }
                if (!enforcedType.Has((AnimatorControllerParameterType)typeProp.intValue) && enforcedType != ParameterType.None)
                    typeProp.intValue = (int)enforcedType.ToAnimatorControllerParameterType();
                var nameRect = position;
                var typeRect = position;
                nameRect.width = position.width * 0.6F;
                typeRect.xMin = nameRect.xMax + EditorGUIUtility.standardVerticalSpacing;
                if (parameterCache == null)
                    using (var changed = new EditorGUI.ChangeCheckScope()) {
                        EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
                        if (changed.changed) {
                            parameterCache?.GuessedComponents.TryGetValue(nameValue, out sourceValue);
                            sourceProp.objectReferenceValue = sourceValue;
                        }
                    }
                else
                    using (new EditorGUI.PropertyScope(nameRect, GUIContent.none, nameProp)) {
                        textFieldDropDownTextStyle ??= GUI.skin.FindStyle("TextFieldDropDownText");
                        textFieldDropDownStyle ??= GUI.skin.FindStyle("TextFieldDropDown");
                        var size = textFieldDropDownStyle.CalcSize(GUIContent.none);
                        var buttonRect = new Rect(nameRect.xMax - size.x, nameRect.y, size.x, nameRect.height);
                        var nameTextRect = new Rect(nameRect.x, nameRect.y, nameRect.width - size.x, nameRect.height);
                        using (var changed = new EditorGUI.ChangeCheckScope()) {
                            nameValue = EditorGUI.TextField(nameTextRect, nameValue, textFieldDropDownTextStyle);
                            if (changed.changed) {
                                nameProp.stringValue = nameValue;
                                parameterCache?.GuessedComponents.TryGetValue(nameValue, out sourceValue);
                                sourceProp.objectReferenceValue = sourceValue;
                            }
                        }
                        using (new EditorGUI.DisabledScope(parameterCache.Parameters.Count == 0))
                            if (GUI.Button(buttonRect, GUIContent.none, textFieldDropDownStyle)) {
                                parameterCache = GetCache(target, ignoreComponents, true);
                                if (parameterCache != null) {
                                    var menu = new GenericMenu();
                                    var menuPath = new List<string>();
                                    foreach (var p in parameterCache.Parameters)
                                        if ((!isTypeEnforced || enforcedType.Has(p.Value.type)) && synchronizeState.MatchesSynchronize(p.Value.synchroized)) {
                                            if (p.Key.source != null) menuPath.Add(p.Key.source.name);
                                            menuPath.Add(p.Key.propertyName);
                                            menu.AddItem(
                                                new GUIContent(string.Join("/", menuPath)),
                                                p.Key.propertyName == nameValue && p.Key.source == sourceValue,
                                                UpdateProperty,
                                                Tuple.Create(p.Key.propertyName, p.Key.source, p.Value.type, nameProp, typeProp, sourceProp)
                                            );
                                            menuPath.Clear();
                                        }
                                    menu.DropDown(nameRect);
                                }
                            }
                    }
                isTypeEnforced = enforcedType != ParameterType.None;
                using (new EditorGUI.DisabledScope(isTypeEnforced)) {
                    if (isTypeEnforced && sourceValue != null) {
                        tempContent.text = ((AnimatorControllerParameterType)typeProp.intValue).ToString();
                        tempContent.image = AssetPreview.GetMiniThumbnail(sourceValue);
                        tempContent.tooltip = "";
                        EditorGUI.LabelField(typeRect, tempContent, EditorStyles.popup);
                    } else
                        EditorGUI.PropertyField(typeRect, typeProp, GUIContent.none);
                }
                if (isTypeEnforced && sourceValue != null) {
                    tempContent.text = "";
                    tempContent.image = null;
                    tempContent.tooltip = string.Format(I18N.Instance.GetOrDefault("ParameterProvider", ""), sourceValue.name, sourceValue.GetType().Name);
                    if (GUI.Button(typeRect, tempContent, GUIStyle.none))
                        EditorGUIUtility.PingObject(sourceValue);
                }
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            if (attribute is AnimatorParameterRefUsageAttribute attr)
                Draw(position, property, label, attr.IgnoreComponents, attr.EnforcedType);
            else
                Draw(position, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        static void UpdateProperty(object data) {
            if (data is Tuple<string, Component, AnimatorControllerParameterType, SerializedProperty, SerializedProperty, SerializedProperty> tuple)
                UpdateProperty(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5, tuple.Item6);
        }

        static void UpdateProperty(
            string propertyName,
            Component source,
            AnimatorControllerParameterType type,
            SerializedProperty nameProp,
            SerializedProperty typeProp,
            SerializedProperty sourceProp
        ) {
            if (nameProp == null || typeProp == null || sourceProp == null) return;
            nameProp.stringValue = propertyName;
            typeProp.intValue = (int)type;
            sourceProp.objectReferenceValue = source;
            nameProp.serializedObject.ApplyModifiedProperties();
        }
    }
}