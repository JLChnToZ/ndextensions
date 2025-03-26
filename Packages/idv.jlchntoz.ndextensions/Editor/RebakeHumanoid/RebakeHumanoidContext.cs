using UnityEngine;
using System.Runtime.CompilerServices;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidContext : IExtensionContext {
        static readonly ConditionalWeakTable<BuildContext, Context> innerContexts = new();
        readonly ConditionalWeakTable<UnityObject, UnityObject> cache = new();
        UnityObject assetRoot;
        AnimationRelocator relocator;
        Vector3 orgPos;
        Quaternion orgRot;
        Context innerContext;

        public float BareFeetOffset {
            get => innerContext.bareFeetOffset;
            set => innerContext.bareFeetOffset = value;
        }

        public AnimationRelocator Relocator => relocator;

        public void OnActivate(BuildContext context) {
            innerContext = innerContexts.GetOrCreateValue(context);
            assetRoot = context.AssetContainer;
            var transform = context.AvatarRootTransform;
            relocator = new AnimationRelocator(transform);
            transform.GetPositionAndRotation(out orgPos, out orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public void OnDeactivate(BuildContext context) {
            cache.Clear();
            assetRoot = null;
            relocator = null;
            innerContext = null;
            context.AvatarRootTransform.SetPositionAndRotation(orgPos, orgRot);
        }

        public RuntimeAnimatorController GetRelocatedController(RuntimeAnimatorController src) {
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

        class Context {
            public float bareFeetOffset;
        }
    }
}