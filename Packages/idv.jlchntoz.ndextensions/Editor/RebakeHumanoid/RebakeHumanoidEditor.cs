using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(RebakeHumanoid))]
    public class RebakeHumanoidEditor : Editor {
        static readonly GUILayoutOption[]
            defaultLayout = null,
            width20 = new[] { GUILayout.Width(20) },
            width50 = new[] { GUILayout.Width(50) },
            singleLineHeight = new[] { GUILayout.Height(EditorGUIUtility.singleLineHeight) };
        static I18N i18n;
        static string tempAvatarPath;
        static readonly GUIContent tempContent = new();
        static readonly Vector3Int emptyMuscleIndex = new(-1, -1, -1);
        static GUIContent errorIconContent, customLimitsIconContent;
        static Vector3Int[] muscleIndeces;
        static HumanLimit[] defaultHumanLimits;
        static string[] boneNames;
        SerializedProperty fixPoseProp;
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
        SerializedProperty generatedAvatarProp;
        Animator animator;
        [NonSerialized] static Transform[] boneCache;

        void OnEnable() {
            fixPoseProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixPose));
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
            generatedAvatarProp = serializedObject.FindProperty(nameof(RebakeHumanoid.generatedAvatar));
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
            if (i18n == null) i18n = I18N.Instance;
            if (customLimitsIconContent == null) customLimitsIconContent = new GUIContent(EditorGUIUtility.IconContent("JointAngularLimits"));
            customLimitsIconContent.tooltip = i18n["RebakeHumanoid.boneMapping:customLimits"];
        }

        public override void OnInspectorGUI() {
            if (i18n == null) i18n = I18N.Instance;
            I18NEditor.DrawLocaleField();
            EditorGUILayout.Space();
            serializedObject.Update();
            EditorGUILayout.HelpBox(i18n["RebakeHumanoid:note"], MessageType.Info);
            EditorGUILayout.PropertyField(fixPoseProp, i18n.GetContent("RebakeHumanoid.fixPose"), defaultLayout);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(i18n.GetContent("RebakeHumanoid.HEIGHT_ADJUST"), EditorStyles.boldLabel, defaultLayout);
#if VRC_SDK_VRCSDK3
            EditorGUILayout.PropertyField(adjustViewpointProp, i18n.GetContent("RebakeHumanoid.adjustViewpoint"), defaultLayout);
            if (adjustViewpointProp.boolValue)
                EditorGUILayout.HelpBox(i18n["RebakeHumanoid.adjustViewpoint:note"], MessageType.Info);
#endif
            i18n.EnumFieldLayout(floorAdjustmentProp, "RebakeHumanoid.floorAdjustment");
            var floorAdjustmentMode = (FloorAdjustmentMode)floorAdjustmentProp.intValue;
            if (floorAdjustmentMode == FloorAdjustmentMode.BareFeetToGround)
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.PropertyField(rendererWithBareFeetProp, i18n.GetContent("RebakeHumanoid.rendererWithBareFeet"));
#if VRC_SDK_VRCSDK3
            if (!adjustViewpointProp.boolValue && (floorAdjustmentMode > FloorAdjustmentMode.BareFeetToGround || manualOffsetProp.vector3Value.y != 0))
                EditorGUILayout.HelpBox(i18n["FloorAdjustmentMode.FixSolesStuck:note"], MessageType.Info);
#endif
            if (floorAdjustmentMode == FloorAdjustmentMode.FixHoveringFeet)
                EditorGUILayout.HelpBox(i18n["FloorAdjustmentMode.FixHoveringFeet:note"], MessageType.Info);
            EditorGUILayout.PropertyField(manualOffsetProp, i18n.GetContent("RebakeHumanoid.manualOffset"), defaultLayout);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(i18n.GetContent("RebakeHumanoid.BAD_RIGGING_FIXES"), EditorStyles.boldLabel, defaultLayout);
            EditorGUILayout.PropertyField(fixBoneOrientationProp, i18n.GetContent("RebakeHumanoid.fixBoneOrientation"), defaultLayout);
            EditorGUILayout.PropertyField(fixCrossLegsProp, i18n.GetContent("RebakeHumanoid.fixCrossLegs"), defaultLayout);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(i18n.GetContent("RebakeHumanoid.ADVANCED"), EditorStyles.boldLabel, defaultLayout);
            Avatar avatar = null;
            bool hasValidAvatar = (!animator && !(target as Component).TryGetComponent(out animator)) ||
                ((avatar = animator.avatar) != null && avatar.isHuman);
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                i18n.EnumFieldLayout(overrideHumanModeProp, "OverrideHumanDescription.mode");
                if (changeCheck.changed) {
                    if (!hasValidAvatar && overrideHumanModeProp.intValue == (int)OverrideMode.Inherit)
                        overrideHumanModeProp.intValue = (int)OverrideMode.Default;
                    FetchHumanProperties(null);
                }
            }
            if (overrideHumanModeProp.intValue == (int)OverrideMode.Override)
                using (new EditorGUI.IndentLevelScope()) {
                    using (new EditorGUILayout.HorizontalScope(defaultLayout)) {
                        using (new EditorGUI.DisabledScope(!hasValidAvatar))
                            if (GUILayout.Button(i18n.GetContent("RebakeHumanoid:copy"), defaultLayout)) FetchHumanProperties(avatar);
                        if (GUILayout.Button(i18n.GetContent("RebakeHumanoid:default"), defaultLayout)) FetchHumanProperties(null);
                    }
                    EditorGUILayout.PropertyField(armStretchProp, i18n.GetContent("OverrideHumanDescription.armStretch"), defaultLayout);
                    EditorGUILayout.PropertyField(upperArmTwistProp, i18n.GetContent("OverrideHumanDescription.upperArmTwist"), defaultLayout);
                    EditorGUILayout.PropertyField(lowerArmTwistProp, i18n.GetContent("OverrideHumanDescription.lowerArmTwist"), defaultLayout);
                    EditorGUILayout.PropertyField(legStretchProp, i18n.GetContent("OverrideHumanDescription.legStretch"), defaultLayout);
                    EditorGUILayout.PropertyField(lowerLegTwistProp, i18n.GetContent("OverrideHumanDescription.lowerLegTwist"), defaultLayout);
                    EditorGUILayout.PropertyField(upperLegTwistProp, i18n.GetContent("OverrideHumanDescription.upperLegTwist"), defaultLayout);
                    EditorGUILayout.PropertyField(feetSpacingProp, i18n.GetContent("OverrideHumanDescription.feetSpacing"), defaultLayout);
                    EditorGUILayout.PropertyField(hasTranslationDoFProp, i18n.GetContent("OverrideHumanDescription.hasTranslationDoF"), defaultLayout);
                }
            if (!hasValidAvatar && !overrideProp.boolValue) {
                overrideProp.boolValue = true;
                boneMappingProp.isExpanded = true;
            }
            using (new EditorGUI.DisabledScope(!hasValidAvatar))
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                EditorGUILayout.PropertyField(overrideProp, i18n.GetContent("RebakeHumanoid.override"), defaultLayout);
                if (changeCheck.changed && overrideProp.boolValue) boneMappingProp.isExpanded = true;
            }
            if (!hasValidAvatar)
                EditorGUILayout.HelpBox(i18n["RebakeHumanoid.override:forced"], MessageType.Warning);
            if (overrideProp.boolValue) {
                FetchBones();
                if (GUILayout.Button(i18n.GetContent("RebakeHumanoid.boneMapping:generateTemp"), defaultLayout) &&
                    (animator != null || (target as Component).TryGetComponent(out animator)))
                    GenerateTemporaryAvatar();
                EditorGUILayout.HelpBox(i18n["RebakeHumanoid.boneMapping:generateTemp:note"], MessageType.Info);
                bool expaanded;
                using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                    expaanded = EditorGUILayout.Foldout(boneMappingProp.isExpanded, i18n["RebakeHumanoid.boneMapping"], true);
                    if (changeCheck.changed) boneMappingProp.isExpanded = expaanded;
                }
                if (expaanded)
                    using (new EditorGUI.IndentLevelScope())
                        DrawBones(hasValidAvatar);
            }
            serializedObject.ApplyModifiedProperties();
        }

        void DrawBones(bool hasValidAvatar) {
            using (new EditorGUILayout.HorizontalScope(defaultLayout)) {
                using (new EditorGUI.DisabledScope(!hasValidAvatar))
                    if (GUILayout.Button(i18n.GetContent("RebakeHumanoid:copy"), defaultLayout)) FetchBones(true);
                if (GUILayout.Button(i18n.GetContent("RebakeHumanoid:guess"), defaultLayout)) FetchBones(true, true);
            }
            if (customLimitsProp.arraySize != HumanTrait.BoneCount)
                customLimitsProp.arraySize = HumanTrait.BoneCount;
            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++) {
                bool hasCustomLimit = false, shouldSetDefaults = false;
                var currentLimitProp = customLimitsProp.GetArrayElementAtIndex(bone);
                var overrideStateValue = currentLimitProp.FindPropertyRelative(nameof(OverrideHumanLimits.mode));
                using (new EditorGUILayout.HorizontalScope(singleLineHeight)) {
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
                            if (HumanTrait.RequiredBone(bone)) errorMessage = i18n["RebakeHumanoid.boneMapping:error_required"];
                        } else if (bone > 0) {
                            for (int bi = HumanTrait.GetParentBone(bone); bi >= 0; bi = HumanTrait.GetParentBone(bi)) {
                                if (boneCache[bi] == null) continue;
                                if (!boneCache[bone].IsChildOf(boneCache[bi]))
                                    errorMessage = string.Format(i18n["RebakeHumanoid.boneMapping:error_childof"], boneNames[bi], boneCache[bi].name);
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
                            GUILayout.Toggle(false, customLimitsIconContent, EditorStyles.miniButton, width20);
                    } else
                        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                            hasCustomLimit = overrideStateValue.intValue != (int)OverrideMode.Inherit;
                            hasCustomLimit = GUILayout.Toggle(hasCustomLimit, customLimitsIconContent, EditorStyles.miniButton, width20);
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
                hasOverride = EditorGUILayout.Toggle(i18n.GetContent("OverrideHumanDescription.humanLimits"), hasOverride, defaultLayout);
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
                        min = EditorGUILayout.FloatField(min, width50);
                        EditorGUILayout.MinMaxSlider(ref min, ref max, -180F, 180F);
                        max = EditorGUILayout.FloatField(max, width50);
                        EditorGUIUtility.labelWidth = prefixSize * 0.5F;
                        restValues[i] = EditorGUILayout.Slider(i18n.GetContent("OverrideHumanLimits.center"), restValues[i], min, max, defaultLayout);
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

        void GenerateTemporaryAvatar() {
            var previousGeneratedAvatar = generatedAvatarProp.objectReferenceValue as Avatar;
            if (previousGeneratedAvatar != null)
                foreach (var other in FindObjectsOfType<RebakeHumanoid>(true)) {
                    if (other == target) continue;
                    if (other.generatedAvatar == previousGeneratedAvatar) {
                        previousGeneratedAvatar = null;
                        break;
                    }
                }
            Undo.RecordObject(animator, "Generate Temporary Avatar");
            if (HumanoidAvatarProcessor.GenerateTemporaryAvatar(animator, boneCache)) {
                var generatedAvatar = animator.avatar;
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(generatedAvatar))) {
                    string path = null;
                    if (previousGeneratedAvatar != null) {
                        path = AssetDatabase.GetAssetPath(previousGeneratedAvatar);
                        if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
                    }
                    if (string.IsNullOrEmpty(path)) {
                        if (string.IsNullOrEmpty(tempAvatarPath)) {
                            var packageInfo = PackageManagerPackageInfo.FindForAssembly(typeof(RebakeHumanoid).Assembly);
                            var dirPath = $"{packageInfo.resolvedPath}/Temp/";
                            if (!Directory.Exists(dirPath)) {
                                Directory.CreateDirectory(dirPath);
                                AssetDatabase.Refresh();
                            }
                            tempAvatarPath = $"{packageInfo.assetPath}/Temp/";
                        }
                        path = AssetDatabase.GenerateUniqueAssetPath($"{tempAvatarPath}{animator.name} Temp Avatar.asset");
                    }
                    AssetDatabase.CreateAsset(generatedAvatar, path);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                generatedAvatarProp.objectReferenceValue = generatedAvatar;
            }
        }
    }
}