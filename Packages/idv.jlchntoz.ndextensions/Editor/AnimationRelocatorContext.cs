using UnityEngine;
using System.Runtime.CompilerServices;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    class AnimationRelocatorContext : IExtensionContext {
        readonly ConditionalWeakTable<RuntimeAnimatorController, RuntimeAnimatorController> cache = new();
        UnityObject assetRoot;
        AnimationRelocator relocator;
        Animator animator;
#if VRC_SDK_VRCSDK3
        VRCAvatarDescriptor vrcaDesc;
#endif

        public AnimationRelocator Relocator => relocator;

        public void OnActivate(BuildContext context) {
            assetRoot = context.AssetContainer;
            relocator = new AnimationRelocator(context.AvatarRootTransform);
            var rootObject = context.AvatarRootObject;
            if (rootObject.TryGetComponent(out animator))
                GetAnimationController(animator.runtimeAnimatorController);
#if VRC_SDK_VRCSDK3
            if (rootObject.TryGetComponent(out vrcaDesc)) {
                GetAnimationControllers(vrcaDesc.baseAnimationLayers);
                GetAnimationControllers(vrcaDesc.specialAnimationLayers);
            }
#endif
        }

        public void OnDeactivate(BuildContext context) {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.runtimeAnimatorController = GetRelocatedController(animator.runtimeAnimatorController);
            animator = null;
#if VRC_SDK_VRCSDK3
            if (vrcaDesc != null) {
                AssignAnimationControllers(vrcaDesc.baseAnimationLayers);
                AssignAnimationControllers(vrcaDesc.specialAnimationLayers);
            }
            vrcaDesc = null;
#endif
            cache.Clear();
            assetRoot = null;
            relocator = null;
        }

        public RuntimeAnimatorController GetRelocatedController(RuntimeAnimatorController src) {
            var controller = relocator[src];
            if (controller != src && controller is AnimatorOverrideController overrideController) {
                if (cache.TryGetValue(src, out var cachedController) && cachedController != null)
                    return cachedController;
                var baker = new AnimatorOverrideControllerBaker(overrideController);
                controller = baker.Bake();
                baker.SaveToAsset(assetRoot);
                cache.Add(src, controller);
            }
            return controller;
        }

        public void GetAnimationController(RuntimeAnimatorController controller) =>
            relocator.AddController(controller);

#if VRC_SDK_VRCSDK3
        public void GetAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                relocator.AddController(layers[i].animatorController);
        }

        public void AssignAnimationControllers(VRCAvatarDescriptor.CustomAnimLayer[] layers) {
            if (layers == null) return;
            for (int i = 0, count = layers.Length; i < count; i++)
                layers[i].animatorController = GetRelocatedController(layers[i].animatorController);
        }
#endif
    }
}