using UnityEditor;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    [CustomEditor(typeof(RebakeHumanoid))]
    public class RebakeHumanoidEditor : Editor {
        static readonly GUIContent tempContent = new();
        static string[] boneNames;
        SerializedProperty fixBoneOrientationProp, fixCrossLegsProp, autoCalculateFootOffsetProp, manualOffsetProp, overrideProp, overrideBonesProp;
        #if VRC_SDK_VRCSDK3
        SerializedProperty adjustViewpointProp;
        #endif

        void OnEnable() {
            fixBoneOrientationProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixBoneOrientation));
            fixCrossLegsProp = serializedObject.FindProperty(nameof(RebakeHumanoid.fixCrossLegs));
            autoCalculateFootOffsetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.autoCalculateFootOffset));
            manualOffsetProp = serializedObject.FindProperty(nameof(RebakeHumanoid.manualOffset));
            #if VRC_SDK_VRCSDK3
            adjustViewpointProp = serializedObject.FindProperty(nameof(RebakeHumanoid.adjustViewpoint));
            #endif
            overrideProp = serializedObject.FindProperty(nameof(RebakeHumanoid.@override));
            overrideBonesProp = serializedObject.FindProperty(nameof(RebakeHumanoid.overrideBones));
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
            EditorGUILayout.PropertyField(overrideProp);
            if (overrideProp.boolValue) {
                FetchBones();
                bool expaanded;
                using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
                    expaanded = EditorGUILayout.Foldout(overrideBonesProp.isExpanded, overrideBonesProp.displayName, true);
                    if (changeCheck.changed) overrideBonesProp.isExpanded = expaanded;
                }
                if (expaanded)
                    using (new EditorGUI.IndentLevelScope()) {
                        for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++) {
                            tempContent.text = boneNames[(int)bone];
                            EditorGUILayout.PropertyField(overrideBonesProp.GetArrayElementAtIndex((int)bone), tempContent);
                        }
                    if (GUILayout.Button("Refresh")) FetchBones(true);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        void FetchBones(bool forced = false) {
            if (overrideBonesProp.arraySize != (int)HumanBodyBones.LastBone) {
                overrideBonesProp.arraySize = (int)HumanBodyBones.LastBone;
                forced = true;
            }
            if (forced && (target as Component).TryGetComponent(out Animator animator))
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                    overrideBonesProp.GetArrayElementAtIndex((int)bone).objectReferenceValue = animator.GetBoneTransform(bone);
        }
    }
}