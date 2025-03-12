using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Rebake Humanoid Avatar")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class RebakeHumanoid : MonoBehaviour {
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
        public Transform[] overrideBones;
    }

    #if VRC_SDK_VRCSDK3
    public partial class RebakeHumanoid : VRC.SDKBase.IEditorOnly {
        [Tooltip("Auto adjusts view point to match the avatar's eye level.")]
        public bool adjustViewpoint;
    }
    #endif
}
