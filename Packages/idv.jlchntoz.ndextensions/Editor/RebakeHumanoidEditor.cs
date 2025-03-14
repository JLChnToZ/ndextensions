using UnityEditor;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(RebakeHumanoid))]
    public class RebakeHumanoidEditor : Editor {
        static readonly GUIContent tempContent = new();
        static string[] boneNames;
        SerializedProperty fixBoneOrientationProp, fixCrossLegsProp, autoCalculateFootOffsetProp, manualOffsetProp, overrideProp, boneMappingProp;
        #if VRC_SDK_VRCSDK3
        SerializedProperty adjustViewpointProp;
        #endif
        Animator animator;

        void OnEnable() {
            fixBoneOrientationProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixBoneOrientation));
            fixCrossLegsProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixCrossLegs));
            autoCalculateFootOffsetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.autoCalculateFootOffset));
            manualOffsetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.manualOffset));
            #if VRC_SDK_VRCSDK3
            adjustViewpointProp = serializedObject.FindProperty(nameof(RebakeHumanoid.adjustViewpoint));
            #endif
            overrideProp = serializedObject.FindProperty(nameof(RebakeHumanoid.@override));
            boneMappingProp = serializedObject.FindProperty(nameof(RebakeHumanoid.boneMapping));
            if (boneNames == null) {
                boneNames = new string[(int)HumanBodyBones.LastBone];
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                    boneNames[(int)bone] = ObjectNames.NicifyVariableName(bone.ToString());
            }
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Please make sure your avatar is in T-pose.\nAny adjustments to the humanoid rig such as feets offset will be applied to the avatar on build.", MessageType.Info);
            #if VRC_SDK_VRCSDK3
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(adjustViewpointProp);
            #endif
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(manualOffsetProp);
            EditorGUILayout.PropertyField(autoCalculateFootOffsetProp);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("These options are for attempting to fix issues caused by bad rigging.", MessageType.None);
            EditorGUILayout.PropertyField(fixBoneOrientationProp);
            EditorGUILayout.PropertyField(fixCrossLegsProp);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            Avatar avatar;
            bool hasValidAvatar = (!animator && !(target as Component).TryGetComponent(out animator)) ||
                ((avatar = animator.avatar) != null && avatar.isValid && avatar.isHuman);
            if (!hasValidAvatar && !overrideProp.boolValue) {
                overrideProp.boolValue = true;
                boneMappingProp.isExpanded = true;
            }
            using (new EditorGUI.DisabledScope(!hasValidAvatar))
            using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                EditorGUILayout.PropertyField(overrideProp);
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
                    using (new EditorGUI.IndentLevelScope()) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            using (new EditorGUI.DisabledScope(!hasValidAvatar))
                                if (GUILayout.Button("Fetch")) FetchBones(true);
                            if (GUILayout.Button("Guess")) FetchBones(true, true);
                        }
                        for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++) {
                            tempContent.text = boneNames[(int)bone];
                            EditorGUILayout.PropertyField(boneMappingProp.GetArrayElementAtIndex((int)bone), tempContent);
                        }
                    }
            }
            serializedObject.ApplyModifiedProperties();
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
    }
}