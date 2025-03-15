using UnityEngine;
using nadena.dev.ndmf;
using JLChnToZ.NDExtensions.Editors;
using static UnityEngine.Object;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {

        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        protected override void Configure() {
            InPhase(BuildPhase.Generating).BeforePlugin("nadena.dev.modular-avatar").Run("Check and Assign Dummy Avatar", CheckAndAssignDummyAvatar);
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar").Run("Rebake Humanoid", RebakeHumanoid);
        }

        static void CheckAndAssignDummyAvatar(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration)) return;
            var animator = ctx.AvatarRootObject.GetComponent<Animator>();
            var avatar = animator.avatar;
            if (declaration.@override && (avatar == null || !avatar.isHuman)) {
                var transform = ctx.AvatarRootObject.transform;
                transform.GetPositionAndRotation(out var orgPos, out var orgRot);
                transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                HumanoidAvatarProcessor.Process(animator, ctx.AssetContainer, bones: declaration.boneMapping);
                transform.SetPositionAndRotation(orgPos, orgRot);
            }
        }

        static void RebakeHumanoid(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration)) return;
            if (!declaration.@override) declaration.RefetchBones(); // Force refetch bones
            var transform = ctx.AvatarRootObject.transform;
            transform.GetPositionAndRotation(out var orgPos, out var orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Make sure the avatar is at origin
            var animator = declaration.Animator;
            var avatar = animator.avatar;
            if (avatar == null) return;
            #if VRC_SDK_VRCSDK3
            var eyePosition = declaration.adjustViewpoint ? MeasureEyePosition(declaration) : Vector3.zero;
            #endif
            var hips = declaration.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) {
                if (declaration.autoCalculateFootOffset) {
                    var offset = SoleResolver.FindOffset(declaration, ctx.AvatarRootObject);
                    if (offset < 0) hips.position += new Vector3(0, -offset, 0);
                }
                hips.position += declaration.manualOffset;
            }
            HumanoidAvatarProcessor.Process(
                animator,
                ctx.AssetContainer,
                declaration.fixBoneOrientation,
                declaration.fixCrossLegs,
                declaration.@override ? declaration.boneMapping : null
            );
            #if VRC_SDK_VRCSDK3
            if (declaration.adjustViewpoint &&
                declaration.TryGetComponent(out VRC.SDK3.Avatars.Components.VRCAvatarDescriptor vrcaDesc))
                vrcaDesc.ViewPosition += MeasureEyePosition(declaration) - eyePosition;
            #endif
            DestroyImmediate(declaration);
            transform.SetPositionAndRotation(orgPos, orgRot);
        }

        static Vector3 MeasureEyePosition(RebakeHumanoid declaration) {
            var leftEye = declaration.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = declaration.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye != null) {
                if (rightEye != null) return (leftEye.position + rightEye.position) * 0.5f;
                return leftEye.position;
            } else if (rightEye != null) return rightEye.position;
            var head = declaration.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head.position;
            return Vector3.zero;
        }
    }
}