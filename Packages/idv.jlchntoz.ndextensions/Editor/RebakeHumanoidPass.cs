using UnityEngine;
using System.Collections.Generic;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using static UnityEngine.Object;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidPass : Pass<RebakeHumanoidPass> {
        readonly Dictionary<UnityObject, UnityObject> cache = new();
        public override string DisplayName => "Rebake Humanoid";

        protected override void Execute(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration)) return;
            if (!declaration.@override) declaration.RefetchBones(); // Force refetch bones
            var transform = ctx.AvatarRootObject.transform;
            transform.GetPositionAndRotation(out var orgPos, out var orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Make sure the avatar is at origin
            var animator = declaration.Animator;
            var relocator = new AnimationRelocator(transform);
            relocator.AddController(animator.runtimeAnimatorController);
#if VRC_SDK_VRCSDK3
            var eyePosition = declaration.adjustViewpoint ? MeasureEyePosition(declaration) : Vector3.zero;
            if (declaration.TryGetComponent(out VRCAvatarDescriptor vrcaDesc)) {
                GetAnimationControllers(vrcaDesc.baseAnimationLayers, relocator);
                GetAnimationControllers(vrcaDesc.specialAnimationLayers, relocator);
            }
#endif
            var hips = declaration.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) {
                if (declaration.autoCalculateFootOffset) {
                    var offset = SoleResolver.FindOffset(declaration, ctx.AvatarRootObject);
                    if (declaration.fixHoverFeet || offset < 0) hips.position += new Vector3(0, -offset, 0);
                }
                hips.position += declaration.manualOffset;
            }
            HumanoidAvatarProcessor.Process(
                animator,
                ctx.AssetContainer,
                declaration.fixBoneOrientation,
                declaration.fixCrossLegs,
                declaration.@override ? declaration.boneMapping : null,
                declaration.overrideHuman,
                relocator
            );
#if VRC_SDK_VRCSDK3
            if (vrcaDesc != null) {
                if (declaration.adjustViewpoint)
                    vrcaDesc.ViewPosition += MeasureEyePosition(declaration) - eyePosition;
                if (declaration.fixBoneOrientation)
                    FixEyeRotation(vrcaDesc, declaration);
                AssignAnimationControllers(vrcaDesc.baseAnimationLayers, relocator, ctx.AssetContainer);
                AssignAnimationControllers(vrcaDesc.specialAnimationLayers, relocator, ctx.AssetContainer);
            }
#endif
            animator.runtimeAnimatorController = GetRelocatedController(animator.runtimeAnimatorController, relocator, ctx.AssetContainer);
            DestroyImmediate(declaration);
            transform.SetPositionAndRotation(orgPos, orgRot);
            cache.Clear();
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

        RuntimeAnimatorController GetRelocatedController(RuntimeAnimatorController src, AnimationRelocator relocator, UnityObject assetRoot) {
            var controller = relocator[src];
            if (controller != src && controller is AnimatorOverrideController overrideController) {
                if (cache.TryGetValue(src, out var cachedController) && cachedController != null)
                    return cachedController as RuntimeAnimatorController;
                var baker = new AnimatorOverrideControllerBaker(overrideController);
                controller = baker.Bake();
                baker.SaveToAsset(assetRoot);
                cache.Add(src, controller);
            }
            return controller;
        }

#if VRC_SDK_VRCSDK3
        static void GetAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers, AnimationRelocator relocator) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                relocator.AddController(layers[i].animatorController);
        }

        void AssignAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers, AnimationRelocator relocator, UnityObject assetRoot) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                layers[i].animatorController = GetRelocatedController(layers[i].animatorController, relocator, assetRoot);
        }
        
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