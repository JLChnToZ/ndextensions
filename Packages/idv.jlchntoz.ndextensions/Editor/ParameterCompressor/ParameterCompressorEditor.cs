using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using UnityEngine.Pool;
using nadena.dev.ndmf;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;
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
            for (int i = 0, count = ParameterCompressorContext.CountRequiredParameterBits(parameterCount, boolsCount, parameterCompressor.bitsPerParameter); i < count; i++)
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
        static GUIContent warningIconContent;
        readonly ConditionalWeakTable<Component, ComponentMenu> componentMenus = new();
        SerializedProperty parametersProperty, thresholdProperty, bitsPerParameterProperty;
        readonly HashSet<AnimatorParameterRef> enabledParameters = new();
        readonly HashSet<Component> expandedComponents = new();
        readonly Dictionary<Component, Dictionary<AnimatorParameterRef, Parameter>> allParameters = new();
        int currentParameterCost = 0, savedParameterCost = 0, savedParameterCount = 0, savedBoolParameterCount = 0, parameterBitCount = 0;
        bool expandedDangledParameters = true;

        protected override void OnEnable() {
            base.OnEnable();
            parametersProperty = serializedObject.FindProperty(nameof(ParameterCompressor.parameters));
            thresholdProperty = serializedObject.FindProperty(nameof(ParameterCompressor.threshold));
            bitsPerParameterProperty = serializedObject.FindProperty(nameof(ParameterCompressor.bitsPerParameter));
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
            using (var changed = new EditorGUI.ChangeCheckScope()) {
                EditorGUILayout.PropertyField(bitsPerParameterProperty, i18n.GetContent("ParameterCompressor.bitsPerParameter"));
                if (changed.changed) LoadEnabledParameters();
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(i18n["ParameterCompressor:selectParameters"], EditorStyles.boldLabel);
            using (HashSetPool<AnimatorParameterRef>.Get(out var dangledParameters)) {
                dangledParameters.UnionWith(enabledParameters);
                foreach (var (component, parameters) in allParameters) {
                    if (component == null || parameters.Count == 0) continue;
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
                        foreach (var (reference, parameter) in parameters) {
                            dangledParameters.Remove(reference);
                            bool enabled = enabledParameters.Contains(reference);
                            bool newEnabled = EditorGUILayout.ToggleLeft(parameter.ToString(), enabled);
                            if (newEnabled != enabled) {
                                LoadEnabledParameters();
                                if (newEnabled) AddParameter(parameter.reference);
                                else RemoveParameter(parameter.reference);
                                UpdateBitCount();
                                SaveEnabledParameters();
                            }
                        }
                    else
                        dangledParameters.ExceptWith(parameters.Keys);
                    EditorGUILayout.EndFoldoutHeaderGroup();
                }
                if (dangledParameters.Count > 0) {
                    EditorGUILayout.Space();
                    warningIconContent ??= EditorGUIUtility.IconContent("Error");
                    var temp = i18n.GetContent("ParameterCompressor.dangled");
                    temp.image = warningIconContent.image;
                    expandedDangledParameters = EditorGUILayout.BeginFoldoutHeaderGroup(
                        expandedDangledParameters,
                        temp,
                        menuAction: DangleParametersFoldupMenu
                    );
                    if (expandedDangledParameters)
                        foreach (var parameter in dangledParameters)
                            if (!EditorGUILayout.ToggleLeft(new Parameter(parameter).ToString(), true)) {
                                LoadEnabledParameters();
                                RemoveParameter(parameter);
                                UpdateBitCount();
                                SaveEnabledParameters();
                            }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        void DangleParametersFoldupMenu(Rect rect) {
            var menu = new GenericMenu();
            menu.AddItem(new(i18n.GetContent("ParameterCompressor.cleanDangled")), false, CleanDangled);
            menu.DropDown(rect);
        }

        void CleanDangled() {
            serializedObject.Update();
            LoadEnabledParameters();
            using (ListPool<AnimatorParameterRef>.Get(out var toRemove)) {
                foreach (var parameter in enabledParameters)
                    if (parameter.source == null)
                        toRemove.Add(parameter);
                foreach (var parameter in toRemove)
                    RemoveParameter(parameter);
            }
            UpdateBitCount();
            SaveEnabledParameters();
            serializedObject.ApplyModifiedProperties();
        }

        Component ResolveParameterRoot(Component component) {
            if (component == null) return null;
            var animator = component.GetComponentInParent<Animator>(true);
            if (animator != null) component = animator;
#if VRC_SDK_VRCSDK3
            var descriptor = component.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor != null) component = descriptor;
#endif
            return component;
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
                if (source == null) continue;
                if (allParameters.TryGetValue(source, out var parameters) &&
                    parameters.ContainsKey(entry)) {
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
            var target = ResolveParameterRoot(this.target as Component);
            if (target == null) return;
            currentParameterCost = 0;
            foreach (var parameters in allParameters.Values) parameters.Clear();
            foreach (var entry in ParameterInfo.ForUI.GetParametersForObject(target.gameObject)) {
                if (!entry.WantSynced) continue;
                if (!entry.IsHidden) {
                    var source = entry.Source;
                    if (source == null) source = target;
                    if (!allParameters.TryGetValue(source, out var parameters)) {
                        parameters = new();
                        allParameters[source] = parameters;
                    }
                    if (entry.Namespace == ParameterNamespace.Animator) {
                        var reference = new AnimatorParameterRef(entry.OriginalName, entry.ParameterType ?? ACParameterType.Float, source);
                        parameters.Add(reference, new(reference));
                    }
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
            ParameterCompressorContext.CountRequiredParameterBits(savedParameterCount, savedBoolParameterCount, bitsPerParameterProperty.intValue) + 8 :
            0;

        readonly struct Parameter : IEquatable<Parameter> {
            public readonly AnimatorParameterRef reference;
            public readonly string effectiveName;

            public Parameter(AnimatorParameterRef reference, string effectiveName = null) {
                this.reference = reference;
                this.effectiveName = effectiveName ?? reference.name;
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
                if (component == null) return;
                parent.serializedObject.Update();
                parent.LoadEnabledParameters();
                if (!parent.allParameters.TryGetValue(component, out var parameters))
                    return;
                foreach (var parameter in parameters.Keys)
                    parent.AddParameter(parameter);
                parent.UpdateBitCount();
                parent.SaveEnabledParameters();
                parent.serializedObject.ApplyModifiedProperties();
            }

            void DeselectAll() {
                if (component == null) return;
                parent.serializedObject.Update();
                parent.LoadEnabledParameters();
                if (!parent.allParameters.TryGetValue(component, out var parameters))
                    return;
                foreach (var parameter in parameters.Keys)
                    parent.RemoveParameter(parameter);
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