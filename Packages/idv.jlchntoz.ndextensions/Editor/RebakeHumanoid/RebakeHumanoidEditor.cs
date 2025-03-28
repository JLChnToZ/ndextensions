using System;
using UnityEditor;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(RebakeHumanoid))]
    public class RebakeHumanoidEditor : Editor {
        static readonly GUIContent tempContent = new();
        static readonly Vector3Int emptyMuscleIndex = new(-1, -1, -1);
        static GUIContent errorIconContent, customLimitsIconContent;
        static Vector3Int[] muscleIndeces;
        static HumanLimit[] defaultHumanLimits;
        static string[] boneNames;
        SerializedProperty fixBoneOrientationProp;
        SerializedProperty fixCrossLegsProp;
        SerializedProperty floorAdjustmentProp;
        SerializedProperty rendererWithBareFeetProp;
        SerializedProperty manualOffsetProp;
        SerializedProperty overrideProp;
        SerializedProperty boneMappingProp;
        SerializedProperty overrideHumanModeProp;
        SerializedProperty armStretchProp;
        SerializedProperty upperArmTwistProp;
        SerializedProperty lowerArmTwistProp;
        SerializedProperty legStretchProp;
        SerializedProperty lowerLegTwistProp;
        SerializedProperty upperLegTwistProp;
        SerializedProperty feetSpacingProp;
        SerializedProperty hasTranslationDoFProp;
        SerializedProperty customLimitsProp;
#if VRC_SDK_VRCSDK3
        SerializedProperty adjustViewpointProp;
#endif
        Animator animator;
        [NonSerialized] static Transform[] boneCache;

        void OnEnable() {
            fixBoneOrientationProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixBoneOrientation));
            fixCrossLegsProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixCrossLegs));
            floorAdjustmentProp = serializedObject.FindProperty(nameof(RebakeHumanoid.floorAdjustment));
            rendererWithBareFeetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.rendererWithBareFeet));
            manualOffsetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.manualOffset));
#if VRC_SDK_VRCSDK3
            adjustViewpointProp = serializedObject.FindProperty(nameof(RebakeHumanoid.adjustViewpoint));
#endif
            overrideProp = serializedObject.FindProperty(nameof(RebakeHumanoid.@override));
            boneMappingProp = serializedObject.FindProperty(nameof(RebakeHumanoid.boneMapping));
            var overrideHumanProp = serializedObject.FindProperty(nameof(RebakeHumanoid.overrideHuman));
            overrideHumanModeProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.mode));
            armStretchProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.armStretch));
            upperArmTwistProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.upperArmTwist));
            lowerArmTwistProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.lowerArmTwist));
            legStretchProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.legStretch));
            lowerLegTwistProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.lowerLegTwist));
            upperLegTwistProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.upperLegTwist));
            feetSpacingProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.feetSpacing));
            hasTranslationDoFProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.hasTranslationDoF));
            customLimitsProp = overrideHumanProp.FindPropertyRelative(nameof(OverrideHumanDescription.humanLimits));
            if (boneNames == null) boneNames = Array.ConvertAll(MecanimUtils.HumanBoneNames, ObjectNames.NicifyVariableName);
            if (muscleIndeces == null) {
                muscleIndeces = new Vector3Int[HumanTrait.MuscleCount];
                for (int i = 0; i < muscleIndeces.Length; i++)
                    muscleIndeces[i] = new Vector3Int(
                        HumanTrait.MuscleFromBone(i, 0),
                        HumanTrait.MuscleFromBone(i, 1),
                        HumanTrait.MuscleFromBone(i, 2)
                    );
            }
            if (boneCache == null || boneCache.Length == 0) boneCache = new Transform[HumanTrait.BoneCount];
            if (defaultHumanLimits == null) {
                defaultHumanLimits = new HumanLimit[HumanTrait.BoneCount];
                for (int i = 0; i < defaultHumanLimits.Length; i++) {
                    Vector3 min = Vector3.zero, max = Vector3.zero;
                    for (int x = 0; x < 3; x++) {
                        int muscleId = muscleIndeces[i][x];
                        min[x] = HumanTrait.GetMuscleDefaultMin(muscleId);
                        max[x] = HumanTrait.GetMuscleDefaultMax(muscleId);
                    }
                    defaultHumanLimits[i] = new HumanLimit {
                        useDefaultValues = true,
                        min = min,
                        max = max,
                    };
                }
            }
            if (customLimitsIconContent == null)
                customLimitsIconContent = new GUIContent(EditorGUIUtility.IconContent("JointAngularLimits")) {
                    tooltip = "Custom human limits",
                };
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "Please make sure your avatar is in T-pose.\nAny adjustments to the human bones in edit mode will be apply (bake) to the avatar on build.",
                MessageType.Info
            );
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Height Adjustments", EditorStyles.boldLabel);
#if VRC_SDK_VRCSDK3
            EditorGUILayout.PropertyField(adjustViewpointProp);
            if (adjustViewpointProp.boolValue)
                EditorGUILayout.HelpBox(
                    "Eye level adjustment will change the IK measurements of your avatar.\nYou may want to use \"arm span\" measurement mode in VRChat when wearing this avatar to avoid arms shorten issue.",
                    MessageType.Info
                );
#endif
            EditorGUILayout.PropertyField(floorAdjustmentProp);
            var floorAdjustmentMode = (FloorAdjustmentMode)floorAdjustmentProp.intValue;
            if (floorAdjustmentMode == FloorAdjustmentMode.BareFeetToGround)
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.PropertyField(rendererWithBareFeetProp);
#if VRC_SDK_VRCSDK3
            if (!adjustViewpointProp.boolValue && (floorAdjustmentMode > FloorAdjustmentMode.BareFeetToGround || manualOffsetProp.vector3Value.y != 0))
                EditorGUILayout.HelpBox(
                    "You may need to enable \"Auto Adjust Viewpoint\" to match the avatar's eye level.",
                    MessageType.Info
                );
#endif
            if (floorAdjustmentMode == FloorAdjustmentMode.FixHoveringFeet)
                EditorGUILayout.HelpBox(
                    "\"Fix Hover Feet\" can only try the best to ensure the avatar is standing on/above the ground in all scenarios.\nIf you have seletable shoes with different offsets, your avatar will still hover in some cases.",
                    MessageType.Info
                );
            EditorGUILayout.PropertyField(manualOffsetProp);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bad Rigging Ad-Hoc Fixes", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fixBoneOrientationProp);
            EditorGUILayout.PropertyField(fixCrossLegsProp);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            Avatar avatar = null;
            bool hasValidAvatar = (!animator && !(target as Component).TryGetComponent(out animator)) ||
                ((avatar = animator.avatar) != null && avatar.isHuman);
            tempContent.text = "Human Description";
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                EditorGUILayout.PropertyField(overrideHumanModeProp, tempContent);
                if (changeCheck.changed) {
                    if (!hasValidAvatar && overrideHumanModeProp.intValue == (int)OverrideMode.Inherit)
                        overrideHumanModeProp.intValue = (int)OverrideMode.Default;
                    FetchHumanProperties(null);
                }
            }
            if (overrideHumanModeProp.intValue == (int)OverrideMode.Override)
                using (new EditorGUI.IndentLevelScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        using (new EditorGUI.DisabledScope(!hasValidAvatar))
                            if (GUILayout.Button("Copy")) FetchHumanProperties(avatar);
                        if (GUILayout.Button("Default")) FetchHumanProperties(null);
                    }
                    EditorGUILayout.PropertyField(armStretchProp);
                    EditorGUILayout.PropertyField(upperArmTwistProp);
                    EditorGUILayout.PropertyField(lowerArmTwistProp);
                    EditorGUILayout.PropertyField(legStretchProp);
                    EditorGUILayout.PropertyField(lowerLegTwistProp);
                    EditorGUILayout.PropertyField(upperLegTwistProp);
                    EditorGUILayout.PropertyField(feetSpacingProp);
                    EditorGUILayout.PropertyField(hasTranslationDoFProp);
                }
            if (!hasValidAvatar && !overrideProp.boolValue) {
                overrideProp.boolValue = true;
                boneMappingProp.isExpanded = true;
            }
            using (new EditorGUI.DisabledScope(!hasValidAvatar))
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                tempContent.text = "Override Bone Mapping";
                EditorGUILayout.PropertyField(overrideProp, tempContent);
                if (changeCheck.changed && overrideProp.boolValue) boneMappingProp.isExpanded = true;
            }
            if (!hasValidAvatar)
                EditorGUILayout.HelpBox("You don't have a valid humanoid avatar, manual bone mapping is required.", MessageType.Warning);
            if (overrideProp.boolValue) {
                FetchBones();
                bool expaanded;
                using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                    expaanded = EditorGUILayout.Foldout(boneMappingProp.isExpanded, boneMappingProp.displayName, true);
                    if (changeCheck.changed) boneMappingProp.isExpanded = expaanded;
                }
                if (expaanded)
                    using (new EditorGUI.IndentLevelScope())
                        DrawBones(hasValidAvatar);
            }
            serializedObject.ApplyModifiedProperties();
        }

        void DrawBones(bool hasValidAvatar) {
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(!hasValidAvatar))
                    if (GUILayout.Button("Copy")) FetchBones(true);
                if (GUILayout.Button("Guess")) FetchBones(true, true);
            }
            if (customLimitsProp.arraySize != HumanTrait.BoneCount)
                customLimitsProp.arraySize = HumanTrait.BoneCount;
            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++) {
                bool hasCustomLimit = false, shouldSetDefaults = false;
                var currentLimitProp = customLimitsProp.GetArrayElementAtIndex(bone);
                var overrideStateValue = currentLimitProp.FindPropertyRelative(nameof(OverrideHumanLimits.mode));
                using (new EditorGUILayout.HorizontalScope()) {
                    var element = boneMappingProp.GetArrayElementAtIndex(bone);
                    var rect = EditorGUILayout.GetControlRect();
                    tempContent.text = boneNames[bone];
                    using (var property = new EditorGUI.PropertyScope(rect, tempContent, element)) {
                        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                            boneCache[bone] = EditorGUI.ObjectField(rect, property.content, element.objectReferenceValue, typeof(Transform), true) as Transform;
                            if (changeCheck.changed) element.objectReferenceValue = boneCache[bone];
                        }
                        string errorMessage = null;
                        if (boneCache[bone] == null) {
                            if (HumanTrait.RequiredBone(bone)) errorMessage = "This bone is required for humanoid rig.";
                        } else if (bone > 0) {
                            for (int bi = HumanTrait.GetParentBone(bone); bi >= 0; bi = HumanTrait.GetParentBone(bi)) {
                                if (boneCache[bi] == null) continue;
                                if (!boneCache[bone].IsChildOf(boneCache[bi]))
                                    errorMessage = $"This bone must be a child of {boneNames[bi]} ({boneCache[bi].name}).";
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(errorMessage)) {
                            if (errorIconContent == null) errorIconContent = new GUIContent(EditorGUIUtility.IconContent("Error"));
                            errorIconContent.tooltip = errorMessage;
                            var errorRect = rect;
                            errorRect.width = errorRect.height;
                            errorRect.x = rect.xMin;
                            GUI.Label(errorRect, errorIconContent);
                        }
                    }
                    if (muscleIndeces[bone] == emptyMuscleIndex || boneCache[bone] == null) {
                        if (overrideStateValue.intValue != (int)OverrideMode.Inherit) {
                            overrideStateValue.intValue = (int)OverrideMode.Inherit;
                            shouldSetDefaults = true;
                        }
                        using (new EditorGUI.DisabledScope(true))
                            GUILayout.Toggle(false, customLimitsIconContent, EditorStyles.miniButton, GUILayout.Width(20));
                    } else
                        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                            hasCustomLimit = overrideStateValue.intValue != (int)OverrideMode.Inherit;
                            hasCustomLimit = GUILayout.Toggle(hasCustomLimit, customLimitsIconContent, EditorStyles.miniButton, GUILayout.Width(20));
                            if (changeCheck.changed) {
                                overrideStateValue.intValue = hasCustomLimit ? hasValidAvatar ? (int)OverrideMode.Default : (int)OverrideMode.Override : (int)OverrideMode.Inherit;
                                shouldSetDefaults = true;
                            }
                        }
                }
                if (hasCustomLimit)
                    using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                        DrawOverrideProperty(bone, currentLimitProp, overrideStateValue, hasValidAvatar, shouldSetDefaults);
            }
        }

        void DrawOverrideProperty(int boneIndex, SerializedProperty prop, SerializedProperty stateProp, bool hasValidAvatar, bool shouldSetDefaults) {
            bool hasOverride;
            var minProp = prop.FindPropertyRelative(nameof(OverrideHumanLimits.min));
            var maxProp = prop.FindPropertyRelative(nameof(OverrideHumanLimits.max));
            var centerProp = prop.FindPropertyRelative(nameof(OverrideHumanLimits.center));
            using (new EditorGUI.DisabledScope(!hasValidAvatar))
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                hasOverride = stateProp.intValue == (int)OverrideMode.Override;
                hasOverride = EditorGUILayout.Toggle("Override Human Limits", hasOverride);
                if (changeCheck.changed) {
                    stateProp.intValue = hasOverride ? (int)OverrideMode.Override : (int)OverrideMode.Default;
                    if (hasOverride) shouldSetDefaults = true;
                }
            }
            if (!hasOverride) return;
            if (shouldSetDefaults) {
                var defaultHumanLimit = defaultHumanLimits[boneIndex];
                minProp.vector3Value = defaultHumanLimit.min;
                maxProp.vector3Value = defaultHumanLimit.max;
                centerProp.vector3Value = Vector3.zero;
            }
            var muscleNames = MecanimUtils.MuscleNames;
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                var minValues = minProp.vector3Value;
                var maxValues = maxProp.vector3Value;
                var restValues = centerProp.vector3Value;
                for (int i = 0; i < 3; i++)
                    using (new EditorGUILayout.HorizontalScope()) {
                        int index = muscleIndeces[boneIndex][i];
                        if (index < 0) continue;
                        float prefixSize = EditorGUIUtility.labelWidth;
                        EditorGUIUtility.labelWidth = prefixSize - 12;
                        float min = minValues[i], max = maxValues[i];
                        EditorGUILayout.PrefixLabel(muscleNames[index]);
                        min = EditorGUILayout.FloatField(min, GUILayout.Width(50));
                        EditorGUILayout.MinMaxSlider(ref min, ref max, -180F, 180F);
                        max = EditorGUILayout.FloatField(max, GUILayout.Width(50));
                        EditorGUIUtility.labelWidth = prefixSize * 0.5F;
                        restValues[i] = EditorGUILayout.Slider("Ref. Angle", restValues[i], min, max);
                        minValues[i] = min;
                        maxValues[i] = max;
                        EditorGUIUtility.labelWidth = prefixSize;
                    }
                if (changeCheck.changed) {
                    minProp.vector3Value = minValues;
                    maxProp.vector3Value = maxValues;
                    centerProp.vector3Value = restValues;
                }
            }
        }

        void FetchBones(bool forced = false, bool ignoreAvatar = false) {
            if (boneMappingProp.arraySize != (int)HumanBodyBones.LastBone) {
                boneMappingProp.arraySize = (int)HumanBodyBones.LastBone;
                forced = true;
            }
            if (!forced) return;
            var guessedBones = MecanimUtils.GuessHumanoidBodyBones((target as Component).transform, ignoreAvatar: ignoreAvatar);
            if (guessedBones != null)
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                    boneMappingProp.GetArrayElementAtIndex((int)bone).objectReferenceValue = guessedBones[(int)bone];
        }

        void FetchHumanProperties(Avatar avatar) {
            var human = avatar.GetHumanDescriptionOrDefault();
            armStretchProp.floatValue = human.armStretch;
            upperArmTwistProp.floatValue = human.upperArmTwist;
            lowerArmTwistProp.floatValue = human.lowerArmTwist;
            legStretchProp.floatValue = human.legStretch;
            lowerLegTwistProp.floatValue = human.lowerLegTwist;
            upperLegTwistProp.floatValue = human.upperLegTwist;
            feetSpacingProp.floatValue = human.feetSpacing;
            hasTranslationDoFProp.boolValue = human.hasTranslationDoF;
        }
    }
}