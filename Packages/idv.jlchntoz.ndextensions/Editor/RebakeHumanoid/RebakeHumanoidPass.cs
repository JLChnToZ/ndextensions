using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(RebakeHumanoidContext))]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    class RebakeHumanoidPass : Pass<RebakeHumanoidPass> {
        public override string DisplayName => "Rebake Humanoid";

        protected override void Execute(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration)) return;
            if (!declaration.@override) declaration.RefetchBones(); // Force refetch bones
            var rebakeContext = ctx.Extension<RebakeHumanoidContext>();
            var animContext = ctx.Extension<AnimatorServicesContext>();
            var animator = declaration.Animator;
#if VRC_SDK_VRCSDK3
            var eyePosition = declaration.adjustViewpoint ? MeasureEyePosition(declaration) : Vector3.zero;
#endif
            var hips = declaration.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) {
                float yOffset = 0;
                switch (declaration.floorAdjustment) {
                    case FloorAdjustmentMode.BareFeetToGround:
                        yOffset = rebakeContext.BareFeetOffset;
                        break;
                    case FloorAdjustmentMode.FixSolesStuck:
                    case FloorAdjustmentMode.FixHoveringFeet:
                        yOffset = SoleResolver.FindOffset(declaration, ctx.AvatarRootObject);
                        if (declaration.floorAdjustment == FloorAdjustmentMode.FixSolesStuck && yOffset >= 0)
                            yOffset = 0;
                        break;
                }
                hips.position += declaration.manualOffset - new Vector3(0, yOffset, 0);
            }
            HumanoidAvatarProcessor.Process(
                animator,
                ctx.AssetContainer,
                declaration.fixBoneOrientation,
                declaration.fixCrossLegs,
                declaration.fixPoseMode,
                declaration.@override ? declaration.boneMapping : null,
                declaration.overrideHuman,
                animContext.AnimationIndex
            );
#if VRC_SDK_VRCSDK3
            if (declaration.TryGetComponent(out VRCAvatarDescriptor vrcaDesc)) {
                if (declaration.adjustViewpoint)
                    vrcaDesc.ViewPosition += MeasureEyePosition(declaration) - eyePosition;
                if (declaration.fixBoneOrientation)
                    FixEyeRotation(vrcaDesc, declaration);
            }
#endif
            DestroyImmediate(declaration);
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

#if VRC_SDK_VRCSDK3
        static void FixEyeRotation(VRCAvatarDescriptor vrcaDesc, RebakeHumanoid declaration) {
            ref var eyeSettings = ref vrcaDesc.customEyeLookSettings;
            if (vrcaDesc.enableEyeLook) {
                bool isLeftEye = eyeSettings.leftEye == declaration.GetBoneTransform(HumanBodyBones.LeftEye);
                bool isRightEye = eyeSettings.rightEye == declaration.GetBoneTransform(HumanBodyBones.RightEye);
                if (isLeftEye) {
                    var cancel = Quaternion.Inverse(eyeSettings.eyesLookingStraight.left);
                    eyeSettings.eyesLookingStraight.left = Quaternion.identity;
                    eyeSettings.eyesLookingDown.left = cancel * eyeSettings.eyesLookingDown.left;
                    eyeSettings.eyesLookingUp.left = cancel * eyeSettings.eyesLookingUp.left;
                    eyeSettings.eyesLookingLeft.left = cancel * eyeSettings.eyesLookingLeft.left;
                    eyeSettings.eyesLookingRight.left = cancel * eyeSettings.eyesLookingRight.left;
                }
                if (isRightEye) {
                    var cancel = Quaternion.Inverse(eyeSettings.eyesLookingStraight.right);
                    eyeSettings.eyesLookingStraight.right = Quaternion.identity;
                    eyeSettings.eyesLookingDown.right = cancel * eyeSettings.eyesLookingDown.right;
                    eyeSettings.eyesLookingUp.right = cancel * eyeSettings.eyesLookingUp.right;
                    eyeSettings.eyesLookingLeft.right = cancel * eyeSettings.eyesLookingLeft.right;
                    eyeSettings.eyesLookingRight.right = cancel * eyeSettings.eyesLookingRight.right;
                }
                if (isRightEye && isLeftEye) {
                    eyeSettings.eyesLookingStraight.linked = true;
                    DetectLookStraight(ref eyeSettings.eyesLookingDown);
                    DetectLookStraight(ref eyeSettings.eyesLookingUp);
                    DetectLookStraight(ref eyeSettings.eyesLookingLeft);
                    DetectLookStraight(ref eyeSettings.eyesLookingRight);
                }
            }
        }

        static void DetectLookStraight(ref VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations eyeRotations) =>
            eyeRotations.linked = eyeRotations.left == eyeRotations.right;
#endif 
    }
}