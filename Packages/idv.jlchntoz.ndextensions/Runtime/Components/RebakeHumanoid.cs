using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Rebake Humanoid Avatar")]
    [HelpURL("https://github.com/JLChnToZ/ndextensions/?tab=readme-ov-file#humanoid-avatar-rebaker")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class RebakeHumanoid : TagComponent, IBoneTransformProvider {
        [Obsolete, SerializeField, HideInInspector] bool autoCalculateFootOffset, fixHoverFeet, fixPose;
        public FixPoseMode fixPoseMode;
        public Vector3 manualOffset;
        public FloorAdjustmentMode floorAdjustment;
        public SkinnedMeshRenderer rendererWithBareFeet;
        public bool fixBoneOrientation;
        public bool fixCrossLegs;
        public bool @override;
        [FormerlySerializedAs("overrideBones")] public Transform[] boneMapping;
        public OverrideHumanDescription overrideHuman;
        Animator animator;
        [NonSerialized] Transform[] fetchedBones;
        [HideInInspector] public Avatar generatedAvatar;

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
            if (fixPose) {
                fixPoseMode = FixPoseMode.FixTPose;
                fixPose = false;
            }
#pragma warning restore 0612
        }
    }

    public enum FixPoseMode {
        NoFix,
        FixTPose,
        FromExistingAvatar,
        FromExistingAvatarWithScale,
        FromExistingAvatarAggressive,
        FromExistingAvatarAggressiveWithScale,
    }

    public enum FloorAdjustmentMode {
        NoChange,
        BareFeetToGround,
        FixSolesStuck,
        FixHoveringFeet,
    }

#if VRC_SDK_VRCSDK3
    public partial class RebakeHumanoid {
        public bool adjustViewpoint;
    }
#endif
}
