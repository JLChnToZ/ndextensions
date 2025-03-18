using UnityEngine;
using UnityEditor;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidPass : Pass<RebakeHumanoidPass> {
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
            foreach (var clip in relocator.GetAllCloneClips())
                AssetDatabase.AddObjectToAsset(clip, ctx.AssetContainer);
            foreach (var controller in relocator.GetAllOverrideControllers())
                if (controller != null)
                    AssetDatabase.AddObjectToAsset(controller, ctx.AssetContainer);
#if VRC_SDK_VRCSDK3
            if (vrcaDesc != null) {
                if (declaration.adjustViewpoint)
                    vrcaDesc.ViewPosition += MeasureEyePosition(declaration) - eyePosition;
                AssignAnimationControllers(vrcaDesc.baseAnimationLayers, relocator);
                AssignAnimationControllers(vrcaDesc.specialAnimationLayers, relocator);
            }
#endif
            animator.runtimeAnimatorController = relocator[animator.runtimeAnimatorController];
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

#if VRC_SDK_VRCSDK3
        static void GetAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers, AnimationRelocator relocator) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                relocator.AddController(layers[i].animatorController);
        }

        static void AssignAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers, AnimationRelocator relocator) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                layers[i].animatorController = relocator[layers[i].animatorController];
        }
#endif 
    }
}