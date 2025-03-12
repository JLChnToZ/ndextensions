using UnityEngine;
using nadena.dev.ndmf;
using JLChnToZ.NDExtensions.Editors;
using static UnityEngine.Object;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {

        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        protected override void Configure() => InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar").Run("Rebake Humanoid", Run);

        static void Run(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration)) return;
            var transform = ctx.AvatarRootObject.transform;
            transform.GetPositionAndRotation(out var orgPos, out var orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Make sure the avatar is at origin
            var animator = declaration.GetComponent<Animator>();
            var avatar = animator.avatar;
            if (avatar == null) return;
            #if VRC_SDK_VRCSDK3
            var eyePosition = declaration.adjustViewpoint ? MeasureEyePosition(animator) : Vector3.zero;
            #endif
            var hips = declaration.@override && declaration.overrideBones != null && declaration.overrideBones.Length > 0 ?
                declaration.overrideBones[(int)HumanBodyBones.Hips] :
                animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) {
                if (declaration.autoCalculateFootOffset) {
                    var offset = SoleResolver.FindOffset(animator);
                    if (offset < 0) hips.position += new Vector3(0, -offset, 0);
                }
                hips.position += declaration.manualOffset;
            }
            HumanoidAvatarProcessor.Process(
                animator,
                ctx.AssetContainer,
                declaration.fixBoneOrientation,
                declaration.fixCrossLegs,
                declaration.@override ? declaration.overrideBones : null
            );
            #if VRC_SDK_VRCSDK3
            if (declaration.adjustViewpoint &&
                declaration.TryGetComponent(out VRC.SDK3.Avatars.Components.VRCAvatarDescriptor vrcaDesc))
                vrcaDesc.ViewPosition += MeasureEyePosition(animator) - eyePosition;
            #endif
            DestroyImmediate(declaration);
            transform.SetPositionAndRotation(orgPos, orgRot);
        }

        static Vector3 MeasureEyePosition(Animator animator) {
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye != null) {
                if (rightEye != null) return (leftEye.position + rightEye.position) * 0.5f;
                return leftEye.position;
            } else if (rightEye != null) return rightEye.position;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head.position;
            return Vector3.zero;
        }
    }
}