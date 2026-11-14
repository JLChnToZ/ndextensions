using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEditor;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    [ParameterProviderFor(typeof(AnimatorSimplePatcher))]
    internal class AnimatorSimplePatcherProvider : IParameterProvider, IEnumerable<ProvidedParameter> {
        readonly AnimatorSimplePatcher patcher;

        static AnimatorControllerParameterType GetValueType(Value value) => value.type switch {
            AnimatorSimplePatcherConverter.ValueType.Boolean => AnimatorControllerParameterType.Bool,
            AnimatorSimplePatcherConverter.ValueType.Integer => AnimatorControllerParameterType.Int,
            _ => AnimatorControllerParameterType.Float,
        };

        public AnimatorSimplePatcherProvider(AnimatorSimplePatcher patcher) => this.patcher = patcher;

        public IEnumerable<ProvidedParameter> GetSuppliedParameters(BuildContext context = null) => this;

        public IEnumerator<ProvidedParameter> GetEnumerator() => new Enumerator(patcher);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        sealed class Enumerator : IEnumerator<ProvidedParameter> {
            readonly AnimatorSimplePatcher patcher;
            Program program;
            Dictionary<string, AnimatorControllerParameterType?> parameters;
            Dictionary<string, AnimatorControllerParameterType?>.Enumerator enumerator;
            ProvidedParameter providedParameter;
            byte state;

            public ProvidedParameter Current {
                get {
                    if (providedParameter == null) {
                        var (name, type) = enumerator.Current;
                        providedParameter = new ProvidedParameter(name, ParameterNamespace.Animator, patcher, NDExtensionPlugin.Instance, type) {
                            IsAnimatorOnly = true,
                        };
                    }
                    return providedParameter;
                }
            }

            object IEnumerator.Current => Current;

            public Enumerator(AnimatorSimplePatcher patcher) => this.patcher = patcher;

            public bool MoveNext() {
                switch (state) {
                    case 0:
                        try {
                            if (string.IsNullOrWhiteSpace(patcher.code)) {
                                state = 2;
                                return false;
                            }
                            program = Parser.Parse(patcher.code);
                            parameters = DictionaryPool<string, AnimatorControllerParameterType?>.Get();
                            CollectBlock(program.root);
                            enumerator = parameters.GetEnumerator();
                            state = 1;
                            goto case 1;
                        } catch (Exception ex) {
                            if (patcher.lastException == null)
                                Debug.LogException(ex);
                            patcher.lastException = ex;
                            state = 2;
                            return false;
                        }
                    case 1:
                        providedParameter = null;
                        return enumerator.MoveNext();
                }
                return false;
            }

            void AddParameter(string name, AnimatorControllerParameterType? type) {
                if (string.IsNullOrEmpty(name)) return;
                if (!parameters.TryGetValue(name, out var existing) || existing == null)
                    parameters[name] = type;
                else if (type != null)
                    parameters[name] = ParameterTypeFallback.Merge(existing.Value, type.Value);
            }

            void CollectBlock(Block block) {
                foreach (var statement in block.statements)
                    switch (statement) {
                        case SetStatement set:
                            AddParameter(set.parameter, GetValueType(set.value));
                            if (set.value.type == AnimatorSimplePatcherConverter.ValueType.Copy)
                                AddParameter(set.value.source, null);
                            break;
                        case IfStatement conditional:
                            CollectExpression(conditional.condition);
                            CollectBlock(conditional.thenBlock);
                            if (conditional.elseBlock != null) CollectBlock(conditional.elseBlock);
                            break;
                        case TreeStatement tree:
                            CollectTree(tree);
                            break;
                    }
            }

            void CollectExpression(Expression expression) {
                switch (expression) {
                    case BooleanParameterExpression parameter:
                        AddParameter(parameter.parameter, null);
                        break;
                    case ComparisonExpression comparison:
                        AddParameter(comparison.parameter, GetValueType(comparison.value));
                        break;
                    case NotExpression not:
                        CollectExpression(not.operand);
                        break;
                    case BinaryExpression binary:
                        CollectExpression(binary.left);
                        CollectExpression(binary.right);
                        break;
                }
            }

            void CollectTree(TreeStatement tree) {
                AddParameter(tree.parameterX, AnimatorControllerParameterType.Float);
                AddParameter(tree.parameterY, AnimatorControllerParameterType.Float);
                foreach (var child in tree.children) {
                    AddParameter(child.parameter, AnimatorControllerParameterType.Float);
                    if (child.tree != null) CollectTree(child.tree);
                }
            }

            public void Dispose() {
                enumerator.Dispose();
                enumerator = default;
                program?.Dispose();
                program = null;
                if (parameters != null) {
                    DictionaryPool<string, AnimatorControllerParameterType?>.Release(parameters);
                    parameters = null;
                }
                state = 2;
            }

            void IEnumerator.Reset() => throw new NotSupportedException();
        }
    }

    [CustomEditor(typeof(AnimatorSimplePatcher))]
    public class AnimatorSimplePatcherEditor : TagComponentEditor {
        static GUIStyle multilineTextField;
        static readonly GUIContent textFieldContent = new GUIContent();

        SerializedProperty codeProperty;
        SerializedProperty extraReferencesProperty;
#if VRC_SDK_VRCSDK3
        SerializedProperty layerProperty;
#endif
        SerializedProperty priorityProperty;

        protected override void OnEnable() {
            base.OnEnable();
            codeProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.code));
            extraReferencesProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.extraReferences));
#if VRC_SDK_VRCSDK3
            layerProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.layer));
#endif
            priorityProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.priority));
            if (target is AnimatorSimplePatcher patcher &&
                patcher.lastException != null &&
                !string.IsNullOrWhiteSpace(patcher.code))
                try {
                    Parser.Parse(patcher.code);
                } catch (Exception ex) {
                    patcher.lastException = ex;
                }
        }

        protected override void DrawFields() {
            serializedObject.Update();
            var note = i18n["AnimatorSimplePatcher:note"];
            if (!string.IsNullOrEmpty(note))
                EditorGUILayout.HelpBox(note, MessageType.Info);
#if VRC_SDK_VRCSDK3
            EditorGUILayout.PropertyField(layerProperty, i18n.GetContent("AnimatorSimplePatcher.layer"));
#endif
            EditorGUILayout.PropertyField(priorityProperty, i18n.GetContent("AnimatorSimplePatcher.priority"));
            EditorGUILayout.Space();
            textFieldContent.text = codeProperty.stringValue;
            multilineTextField ??= new GUIStyle(EditorStyles.textField) {
                wordWrap = true,
                richText = false,
            };
            var rect = EditorGUILayout.GetControlRect(false, multilineTextField.CalcHeight(textFieldContent, EditorGUIUtility.currentViewWidth) + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            using (var prop = new EditorGUI.PropertyScope(rect, i18n.GetContent("AnimatorSimplePatcher.code"), codeProperty))
            using (var change = new EditorGUI.ChangeCheckScope()) {
                var prefixLabelRect = new Rect(rect) {
                    height = EditorGUIUtility.singleLineHeight,
                };
                var helpButtonWidth = EditorGUIUtility.singleLineHeight;
                EditorGUI.PrefixLabel(prefixLabelRect, prop.content);
                rect.yMin = prefixLabelRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                textFieldContent.text = EditorGUI.TextArea(rect, textFieldContent.text);
                if (change.changed) codeProperty.stringValue = textFieldContent.text;
                if (GUI.Button(new Rect(prefixLabelRect) {
                    xMin = prefixLabelRect.xMax - helpButtonWidth,
                }, EditorGUIUtility.IconContent("_Help"), EditorStyles.iconButton))
                    Help.ShowHelpForObject(target);
            }
            if (target is AnimatorSimplePatcher patcher) {
                var isEmpty = string.IsNullOrWhiteSpace(patcher.code);
                if (patcher.lastException != null) {
                    if (isEmpty)
                        patcher.lastException = null;
                    else
                        EditorGUILayout.HelpBox(patcher.lastException.Message, MessageType.Error);
                }
                using (new EditorGUI.DisabledScope(isEmpty)) if (GUILayout.Button(i18n.GetContent("AnimatorSimplePatcher.validate")))
                    try {
                        patcher.lastException = null;
                        Parser.Parse(patcher.code);
                    } catch (Exception ex) {
                        patcher.lastException = ex;
                    }
            }
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(extraReferencesProperty, i18n.GetContent("AnimatorSimplePatcher.extraReferences"));
            serializedObject.ApplyModifiedProperties();
        }
    }

    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class AnimatorSimplePatcherPass : Pass<AnimatorSimplePatcherPass> {
        protected override void Execute(BuildContext context) {
            var asc = context.Extension<AnimatorServicesContext>();
            var dummyClips = DummyClips.For(context);
            using (ListPool<AnimatorSimplePatcher>.Get(out var patchers)) {
                context.AvatarRootObject.GetComponentsInChildren(true, patchers);
                var controllers = asc.ControllerContext.Controllers;
#if !VRC_SDK_VRCSDK3
                if (!controllers.TryGetValue(context.AvatarRootObject.GetComponent<Animator>(), out var ac)) {
                    foreach (var patcher in patchers) UnityObject.DestroyImmediate(patcher);
                    return;
                }
                var writeDefaults = ac.DetermineWriteDefaults();
#else
                using (DictionaryPool<VRCAvatarDescriptor.AnimLayerType, bool>.Get(out var wds))
#endif
                    foreach (var patcher in patchers) {
                        try {
#if VRC_SDK_VRCSDK3
                            if (!controllers.TryGetValue(patcher.layer, out var ac)) {
                                Debug.LogWarning($"Controller for layer {patcher.layer} not found.");
                                UnityObject.DestroyImmediate(patcher);
                                continue;
                            }
                            if (!wds.TryGetValue(patcher.layer, out var writeDefaults))
                                writeDefaults = ac.DetermineWriteDefaults();
                            wds[patcher.layer] = writeDefaults;
#endif
                            using (DictionaryPool<string, UnityObject>.Get(out var refs)) {
                                if (patcher.extraReferences != null)
                                    foreach (var reference in patcher.extraReferences) {
                                        var refName = reference.name;
                                        if (string.IsNullOrEmpty(refName)) {
                                            if (reference.reference == null) continue;
                                            refName = reference.reference.name;
                                        }
                                        refs.TryAdd(refName, reference.reference);
                                    }
                                using var program = Parser.Parse(patcher.code);
                                var aap = AAPContext.ForController(context, ac);
                                using var ctx = new CompileContext(ac, refs, asc.ObjectPathRemapper, dummyClips, patcher.transform, writeDefaults, aap);
                                ctx.Validate(program);
                                ctx.Compile(program, patcher.priority, patcher.name);
                            }
                        } catch (Exception e) {
                            ErrorReport.ReportError(new NDMFWarn(e.Message, ErrorSeverity.Error));
                            Debug.LogException(e);
                        } finally {
                            UnityObject.DestroyImmediate(patcher);
                        }
                    }
            }
        }
    }
}
