using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Rebake Humanoid Avatar")]
    [HelpURL("https://github.com/JLChnToZ/ndextensions/?tab=readme-ov-file#humanoid-avatar-rebaker")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class RebakeHumanoid : MonoBehaviour, IBoneTransformProvider {
        [Tooltip("Manually adjust the avatar's offset.")]
        public Vector3 manualOffset;
        [Obsolete, SerializeField, HideInInspector] bool autoCalculateFootOffset, fixHoverFeet;
        public FloorAdjustmentMode floorAdjustment;
        [Tooltip("The skinned mesh renderer that contains the bare feet, this is for measuring the offset.")]
        public SkinnedMeshRenderer rendererWithBareFeet;
        [Tooltip("Rotates the armature bones potentially fixes compatibility to mecanim systems such as IK, gestures, etc. cause by bad rigging.")]
        public bool fixBoneOrientation;
        [Tooltip("Attempt sightly adjusts leg bones rest pose to fix cross-legs issue cause by bad rigging.")]
        public bool fixCrossLegs;
        [Tooltip("Use other transforms as bones instead of the original bones defined in the avatar.")]
        public bool @override;
        [FormerlySerializedAs("overrideBones")] public Transform[] boneMapping;
        public OverrideHumanDescription overrideHuman;
        Animator animator;
        [NonSerialized] Transform[] fetchedBones;

        public Animator Animator {
            get {
                if (animator == null) TryGetComponent(out animator);
                return animator;
            }
        }

        public void RefetchBones() =>
            fetchedBones = MecanimUtils.FetchHumanoidBodyBones(Animator.avatar, transform);

        public Transform GetBoneTransform(HumanBodyBones bone) {
            if (@override && boneMapping != null && boneMapping.Length > 0) return boneMapping[(int)bone];
            if (fetchedBones == null || fetchedBones.Length == 0) RefetchBones();
            return fetchedBones[(int)bone];
        }

        void OnValidate() {
#pragma warning disable 0612
            if (fixHoverFeet) {
                floorAdjustment = FloorAdjustmentMode.FixHoveringFeet;
                fixHoverFeet = false;
                autoCalculateFootOffset = false;
            }
            if (autoCalculateFootOffset) {
                floorAdjustment = FloorAdjustmentMode.FixSolesStuck;
                autoCalculateFootOffset = false;
            }
#pragma warning restore 0612
        }
    }

    public enum FloorAdjustmentMode {
        [InspectorName("Disabled")]
        NoChange,
        [InspectorName("Bare feet snap to ground (FBT recommendation)")]
        BareFeetToGround,
        [InspectorName("Ensure soles on ground")]
        FixSolesStuck,
        [InspectorName("Ensure soles on ground and avoid hovering")]
        FixHoveringFeet,
    }

#if VRC_SDK_VRCSDK3
    public partial class RebakeHumanoid : VRC.SDKBase.IEditorOnly {
        [Tooltip("Auto adjusts view point to match the avatar's eye level.")]
        public bool adjustViewpoint;
    }
#endif
}
