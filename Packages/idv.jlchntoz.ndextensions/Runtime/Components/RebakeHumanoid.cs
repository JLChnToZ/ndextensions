using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Rebake Humanoid Avatar")]
    [HelpURL("https://github.com/JLChnToZ/ndextensions/?tab=readme-ov-file#humanoid-avatar-rebaker")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class RebakeHumanoid : MonoBehaviour, IBoneTransformProvider {
        public Vector3 manualOffset;
        [Obsolete, SerializeField, HideInInspector] bool autoCalculateFootOffset, fixHoverFeet;
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
#pragma warning restore 0612
        }
    }

    public enum FloorAdjustmentMode {
        NoChange,
        BareFeetToGround,
        FixSolesStuck,
        FixHoveringFeet,
    }

#if VRC_SDK_VRCSDK3
    public partial class RebakeHumanoid : VRC.SDKBase.IEditorOnly {
        public bool adjustViewpoint;
    }
#endif
}
