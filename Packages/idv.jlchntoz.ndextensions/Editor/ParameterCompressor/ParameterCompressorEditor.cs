using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;
using System.Runtime.CompilerServices;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(ParameterCompressor))]
    public class ParameterCompressorEditor : TagComponentEditor {
        readonly ConditionalWeakTable<Component, ComponentMenu> componentMenus = new();
        SerializedProperty parametersProperty, thresholdProperty;
        readonly HashSet<AnimatorParameterRef> enabledParameters = new();
        readonly HashSet<Component> expandedComponents = new();
        readonly Dictionary<Component, HashSet<Parameter>> allParameters = new();

        protected override void OnEnable() {
            base.OnEnable();
            parametersProperty = serializedObject.FindProperty(nameof(ParameterCompressor.parameters));
            thresholdProperty = serializedObject.FindProperty(nameof(ParameterCompressor.threshold));
            RefreshAllParameters();
            LoadEnabledParameters();
        }

        protected override void DrawFields() {
            EditorGUILayout.HelpBox(i18n["ParameterCompressor:note"], MessageType.Info);
            serializedObject.Update();
            if (GUILayout.Button(i18n.GetContent("ParameterCompressor.refresh"))) {
                RefreshAllParameters();
                LoadEnabledParameters();
            }
            EditorGUILayout.PropertyField(thresholdProperty, i18n.GetContent("ParameterCompressor.threshold"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(i18n["ParameterCompressor:selectParameters"], EditorStyles.boldLabel);
            foreach (var (component, parameters) in allParameters) {
                bool folded = expandedComponents.Contains(component);
                using (var changed = new EditorGUI.ChangeCheckScope()) {
                    if (!componentMenus.TryGetValue(component, out var menu)) {
                        menu = new ComponentMenu(this, component);
                        componentMenus.Add(component, menu);
                    }
                    folded = EditorGUILayout.BeginFoldoutHeaderGroup(
                        folded,
                        EditorGUIUtility.ObjectContent(component, component.GetType()),
                        menuAction: menu.ShowMenu
                    );
                    if (changed.changed) {
                        if (folded) expandedComponents.Add(component);
                        else expandedComponents.Remove(component);
                    }
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
                if (!folded) continue;
                using (new EditorGUI.IndentLevelScope())
                    foreach (var parameter in parameters) {
                        bool enabled = enabledParameters.Contains(parameter.reference);
                        bool newEnabled = EditorGUILayout.ToggleLeft(parameter.ToString(), enabled);
                        if (newEnabled != enabled) {
                            LoadEnabledParameters();
                            if (newEnabled) enabledParameters.Add(parameter.reference);
                            else enabledParameters.Remove(parameter.reference);
                            SaveEnabledParameters();
                        }
                    }
            }
            serializedObject.ApplyModifiedProperties();
        }

        void LoadEnabledParameters() {
            enabledParameters.Clear();
            foreach (SerializedProperty parameter in parametersProperty) {
                var nameProp = parameter.FindPropertyRelative(nameof(AnimatorParameterRef.name));
                var typeProp = parameter.FindPropertyRelative(nameof(AnimatorParameterRef.type));
                var sourceProp = parameter.FindPropertyRelative(nameof(AnimatorParameterRef.source));
                enabledParameters.Add(new(
                    nameProp.stringValue,
                    (ACParameterType)typeProp.intValue,
                    sourceProp.objectReferenceValue as Component
                ));
            }
        }

        void SaveEnabledParameters() {
            parametersProperty.arraySize = enabledParameters.Count;
            int i = 0;
            foreach (var parameter in enabledParameters) {
                var element = parametersProperty.GetArrayElementAtIndex(i++);
                element.FindPropertyRelative(nameof(AnimatorParameterRef.name)).stringValue = parameter.name;
                element.FindPropertyRelative(nameof(AnimatorParameterRef.type)).intValue = (int)parameter.type;
                element.FindPropertyRelative(nameof(AnimatorParameterRef.source)).objectReferenceValue = parameter.source;
            }
        }

        void RefreshAllParameters() {
            var target = this.target as Component;
            if (target == null) return;
#if VRC_SDK_VRCSDK3
            var descriptor = target.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
            if (descriptor != null) target = descriptor;
#endif
            var animator = target.GetComponentInParent<Animator>(true);
            if (animator != null) target = animator;
            foreach (var entry in ParameterInfo.ForUI.GetParametersForObject(target.gameObject)) {
                if (!entry.WantSynced) continue;
                var source = entry.Source;
                if (source == null) source = target;
                if (!allParameters.TryGetValue(source, out var parameters)) {
                    parameters = new();
                    allParameters[source] = parameters;
                }
                switch (entry.Namespace) {
                    case ParameterNamespace.Animator:
                        parameters.Add(new(entry));
                        break;
                }
            }
        }

        readonly struct Parameter : IEquatable<Parameter> {
            public readonly AnimatorParameterRef reference;
            public readonly string effectiveName;

            public Parameter(ProvidedParameter reference) {
                this.reference = new(
                    reference.OriginalName,
                    reference.ParameterType ?? ACParameterType.Float,
                    reference.Source
                );
                effectiveName = reference.EffectiveName;
            }

            public bool Equals(Parameter other) => reference.Equals(other.reference);

            public override bool Equals(object obj) => obj is Parameter other && Equals(other);

            public override int GetHashCode() => reference.GetHashCode();

            public override string ToString() => effectiveName == reference.name ?
                $"{reference.name} ({reference.type})" :
                $"{reference.name} -> {effectiveName} ({reference.type})";
        }

        class ComponentMenu {
            public readonly Component component;
            readonly ParameterCompressorEditor parent;

            public ComponentMenu(ParameterCompressorEditor parent, Component component) {
                this.parent = parent;
                this.component = component;
            }

            public void ShowMenu(Rect rect) {
                var menu = new GenericMenu();
                menu.AddItem(new(i18n.GetContent("ParameterCompressor.selectAll")), false, SelectAll);
                menu.AddItem(new(i18n.GetContent("ParameterCompressor.deselectAll")), false, DeselectAll);
                menu.AddSeparator("");
                menu.AddItem(new(i18n.GetContent("ParameterCompressor.ping")), false, Ping);
                menu.AddItem(new(i18n.GetContent("ParameterCompressor.select")), false, Select);
                menu.AddItem(new(i18n.GetContent("ParameterCompressor.properties")), false, Properties);
                menu.DropDown(rect);
            }

            void SelectAll() {
                parent.serializedObject.Update();
                parent.LoadEnabledParameters();
                foreach (var parameter in parent.allParameters[component])
                    parent.enabledParameters.Add(parameter.reference);
                parent.SaveEnabledParameters();
                parent.serializedObject.ApplyModifiedProperties();
            }

            void DeselectAll() {
                parent.serializedObject.Update();
                parent.LoadEnabledParameters();
                foreach (var parameter in parent.allParameters[component])
                    parent.enabledParameters.Remove(parameter.reference);
                parent.SaveEnabledParameters();
                parent.serializedObject.ApplyModifiedProperties();
            }

            void Select() => Selection.activeObject = component;

            void Ping() => EditorGUIUtility.PingObject(component);

            void Properties() => EditorUtility.OpenPropertyEditor(component);
        }
    }
}