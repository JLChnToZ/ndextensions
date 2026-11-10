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
            multilineTextField ??= new GUIStyle(EditorStyles.textField) {
                wordWrap = true,
                richText = false,
            };
            codeProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.code));
            extraReferencesProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.extraReferences));
#if VRC_SDK_VRCSDK3
            layerProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.layer));
#endif
            priorityProperty = serializedObject.FindProperty(nameof(AnimatorSimplePatcher.priority));
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
                        using var parser = new Parser(patcher.code);
                        using var program = parser.Parse();
                        using var ctx = new CompileContext(ac, refs, asc.ObjectPathRemapper, dummyClips, patcher.transform, writeDefaults);
                        ctx.Validate(program);
                        ctx.Compile(program, ac.AddLayer(new LayerPriority(patcher.priority), patcher.name));
                    }
                    UnityObject.DestroyImmediate(patcher);
                }
            }
        }
    }
}
