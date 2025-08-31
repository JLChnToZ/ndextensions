using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;
using System.Runtime.CompilerServices;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    [ParameterProviderFor(typeof(ParameterCompressor))]
    internal class ParameterCompressorProvider : IParameterProvider {
        readonly ParameterCompressor parameterCompressor;

        public ParameterCompressorProvider(ParameterCompressor parameterCompressor) =>
            this.parameterCompressor = parameterCompressor;

        public IEnumerable<ProvidedParameter> GetSuppliedParameters(BuildContext context = null) {
            yield return new(
                "__CompParam/Value",
                ParameterNamespace.Animator,
                parameterCompressor,
                NDExtensionPlugin.Instance,
                ACParameterType.Int
            ) {
                IsHidden = true,
                WantSynced = true,
            };
            int parameterCount = 0, boolsCount = 0;
            if (parameterCompressor.parameters != null)
                for (int i = 0; i < parameterCompressor.parameters.Length; i++) {
                    var p = parameterCompressor.parameters[i];
                    if (p.type == ACParameterType.Bool) boolsCount++;
                    else parameterCount++;
                }
            for (int i = 0, count = ParameterCompressorContext.CountRequiredParameterBits(parameterCount, boolsCount); i < count; i++)
                yield return new(
                    $"__CompParam/Ref{i}",
                    ParameterNamespace.Animator,
                    parameterCompressor,
                    NDExtensionPlugin.Instance,
                    ACParameterType.Bool
                ) {
                    IsHidden = true,
                    WantSynced = true,
                };
        }
    }

    [CustomEditor(typeof(ParameterCompressor))]
    public class ParameterCompressorEditor : TagComponentEditor {
#if VRC_SDK_VRCSDK3
        const int maxParameterCost = VRCExpressionParameters.MAX_PARAMETER_COST;
#else
        const int maxParameterCost = 0;
#endif
        readonly ConditionalWeakTable<Component, ComponentMenu> componentMenus = new();
        SerializedProperty parametersProperty, thresholdProperty;
        readonly HashSet<AnimatorParameterRef> enabledParameters = new();
        readonly HashSet<Component> expandedComponents = new();
        readonly Dictionary<Component, HashSet<Parameter>> allParameters = new();
        int currentParameterCost = 0, savedParameterCost = 0, savedParameterCount = 0, savedBoolParameterCount = 0, parameterBitCount = 0;

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
#if VRC_SDK_VRCSDK3
            int diff = parameterBitCount - savedParameterCost;
            int bits = currentParameterCost + diff;
            int exceedBits = bits - maxParameterCost;
            if (exceedBits > 0)
                EditorGUILayout.HelpBox(string.Format(
                    i18n["ParameterCompressor:exceed"],
                    currentParameterCost, bits, maxParameterCost, exceedBits
                ), MessageType.Warning);
            else if (diff > 0)
                EditorGUILayout.HelpBox(string.Format(
                    i18n["ParameterCompressor:cost"],
                    currentParameterCost, bits, maxParameterCost, diff
                ), MessageType.Info);
            else if (diff < 0)
                EditorGUILayout.HelpBox(string.Format(
                    i18n["ParameterCompressor:saved"],
                    currentParameterCost, bits, maxParameterCost, -diff
                ), MessageType.None);
            else
                EditorGUILayout.HelpBox(string.Format(
                    i18n["ParameterCompressor:unchanged"],
                    currentParameterCost, bits, maxParameterCost
                ), MessageType.Info);
#else
            EditorGUILayout.HelpBox(i18n["ParameterCompressor:nosdk"], MessageType.Warning);
#endif
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
                }
                if (folded)
                    foreach (var parameter in parameters) {
                        bool enabled = enabledParameters.Contains(parameter.reference);
                        bool newEnabled = EditorGUILayout.ToggleLeft(parameter.ToString(), enabled);
                        if (newEnabled != enabled) {
                            LoadEnabledParameters();
                            if (newEnabled) AddParameter(parameter.reference);
                            else RemoveParameter(parameter.reference);
                            UpdateBitCount();
                            SaveEnabledParameters();
                        }
                    }
                EditorGUILayout.EndFoldoutHeaderGroup();
            }
            serializedObject.ApplyModifiedProperties();
        }

        void LoadEnabledParameters() {
            enabledParameters.Clear();
            savedParameterCost = 0;
            savedParameterCount = 0;
            savedBoolParameterCount = 0;
            foreach (SerializedProperty parameter in parametersProperty) {
                var name = parameter.FindPropertyRelative(nameof(AnimatorParameterRef.name)).stringValue;
                var type = (ACParameterType)parameter.FindPropertyRelative(nameof(AnimatorParameterRef.type)).intValue;
                var source = parameter.FindPropertyRelative(nameof(AnimatorParameterRef.source)).objectReferenceValue as Component;
                var entry = new AnimatorParameterRef(name, type, source);
                enabledParameters.Add(entry);
                if (allParameters.TryGetValue(source, out var parameters) &&
                    parameters.Contains(new(entry))) {
                    savedParameterCost += type switch {
                        ACParameterType.Float => 8,
                        ACParameterType.Int => 8,
                        ACParameterType.Bool => 1,
                        _ => 0,
                    };
                    if (type == ACParameterType.Bool)
                        savedBoolParameterCount++;
                    else
                        savedParameterCount++;
                }
            }
            UpdateBitCount();
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
            var animator = target.GetComponentInParent<Animator>(true);
            if (animator != null) target = animator;
#if VRC_SDK_VRCSDK3
            var descriptor = target.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor != null) target = descriptor;
#endif
            currentParameterCost = 0;
            foreach (var entry in ParameterInfo.ForUI.GetParametersForObject(target.gameObject)) {
                if (!entry.WantSynced) continue;
                if (!entry.IsHidden) {
                    var source = entry.Source;
                    if (source == null) source = target;
                    if (!allParameters.TryGetValue(source, out var parameters)) {
                        parameters = new();
                        allParameters[source] = parameters;
                    }
                    if (entry.Namespace == ParameterNamespace.Animator)
                        parameters.Add(new(entry));
                }
                if (entry.Namespace == ParameterNamespace.Animator)
                    currentParameterCost += entry.ParameterType switch {
                        ACParameterType.Float => 8,
                        ACParameterType.Int => 8,
                        ACParameterType.Bool => 1,
                        _ => 0,
                    };
            }
        }

        void AddParameter(in AnimatorParameterRef parameter) {
            if (!enabledParameters.Add(parameter)) return;
            savedParameterCost += parameter.type switch {
                ACParameterType.Float => 8,
                ACParameterType.Int => 8,
                ACParameterType.Bool => 1,
                _ => 0,
            };
            if (parameter.type == ACParameterType.Bool)
                savedBoolParameterCount++;
            else
                savedParameterCount++;
        }

        void RemoveParameter(in AnimatorParameterRef parameter) {
            if (!enabledParameters.Remove(parameter)) return;
            savedParameterCost -= parameter.type switch {
                ACParameterType.Float => 8,
                ACParameterType.Int => 8,
                ACParameterType.Bool => 1,
                _ => 0,
            };
            if (parameter.type == ACParameterType.Bool)
                savedBoolParameterCount--;
            else
                savedParameterCount--;
        }

        void UpdateBitCount() => parameterBitCount = savedParameterCount > 0 || savedBoolParameterCount > 0 ?
            ParameterCompressorContext.CountRequiredParameterBits(savedParameterCount, savedBoolParameterCount) + 8 :
            0;

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

            public Parameter(AnimatorParameterRef reference) {
                this.reference = reference;
                effectiveName = reference.name;
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
                if (!parent.allParameters.TryGetValue(component, out var parameters))
                    return;
                foreach (var parameter in parameters)
                    parent.AddParameter(parameter.reference);
                parent.UpdateBitCount();
                parent.SaveEnabledParameters();
                parent.serializedObject.ApplyModifiedProperties();
            }

            void DeselectAll() {
                parent.serializedObject.Update();
                parent.LoadEnabledParameters();
                if (!parent.allParameters.TryGetValue(component, out var parameters))
                    return;
                foreach (var parameter in parameters)
                    parent.RemoveParameter(parameter.reference);
                parent.UpdateBitCount();
                parent.SaveEnabledParameters();
                parent.serializedObject.ApplyModifiedProperties();
            }

            void Select() => Selection.activeObject = component;

            void Ping() => EditorGUIUtility.PingObject(component);

            void Properties() => EditorUtility.OpenPropertyEditor(component);
        }
    }
}