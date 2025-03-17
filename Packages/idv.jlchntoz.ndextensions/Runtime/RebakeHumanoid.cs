using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Rebake Humanoid Avatar")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class RebakeHumanoid : MonoBehaviour, IBoneTransformProvider {
        [Tooltip("Manually adjust the avatar's offset.")]
        public Vector3 manualOffset;
        [Tooltip("Automatically calculates the avatar's foot offset by detecting the sole of the model (slow).")]
        public bool autoCalculateFootOffset;
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
    }

#if VRC_SDK_VRCSDK3
    public partial class RebakeHumanoid : VRC.SDKBase.IEditorOnly {
        [Tooltip("Auto adjusts view point to match the avatar's eye level.")]
        public bool adjustViewpoint;
    }
#endif
}
