using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class FeatureMaintainerPass : Pass<FeatureMaintainerPass> {
        public override string DisplayName => "Feature Maintainer";

        VirtualClip fixerClip;

        protected override void Execute(BuildContext context) {
#if VRC_SDK_VRCSDK3
            if (!context.AvatarRootObject.TryGetComponent(out FeatureMaintainer tag)) return;
            DestroyImmediate(tag);
            var extContext = context.Extension<AnimatorServicesContext>();
            FixParticleSystems(context, extContext);
            FixCameraEnable(context, extContext);
            if (fixerClip != null && extContext.ControllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.FX, out var fx))
                fx.AddLayer(LayerPriority.Default, fixerClip.Name).StateMachine.AddState(fixerClip.Name, fixerClip);
            fixerClip = null;
#endif
        }

        void FixParticleSystems(BuildContext context, AnimatorServicesContext animContext) {
            var bindings = new List<(EditorCurveBinding, float)>();
            foreach (var ps in context.AvatarRootTransform.GetBuildableComponentsInChildren<ParticleSystem>()) {
                var path = ps.GetPath(context.AvatarRootTransform);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(ParticleSystem), "CollisionModule.colliderForce");
                if (animContext.AnimationIndex.GetClipsForBinding(binding).Any()) continue;
                var collisionModule = ps.collision;
                if (!collisionModule.enabled && !animContext.AnimationIndex.GetClipsForBinding(
                    EditorCurveBinding.FloatCurve(path, typeof(ParticleSystem), "CollisionModule.enabled")
                ).Any()) continue;
                var colliderForce = collisionModule.colliderForce;
                if (colliderForce == 0) continue;
                bindings.Add((binding, colliderForce));
            }
            if (bindings.Count == 0) return;
            fixerClip ??= VirtualClip.Create("Fixer");
            foreach (var (binding, value) in bindings)
                fixerClip.SetFloatCurve(binding, AnimationCurve.Constant(0, 0.1F, value));
        }

        void FixCameraEnable(BuildContext context, AnimatorServicesContext animContext) {
            var bindings = new List<EditorCurveBinding>();
            foreach (var cam in context.AvatarRootTransform.GetBuildableComponentsInChildren<Camera>()) {
                var path = cam.GetPath(context.AvatarRootTransform);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(Camera), "enabled");
                if (animContext.AnimationIndex.GetClipsForBinding(binding).Any()) continue;
                bindings.Add(binding);
            }
            if (bindings.Count == 0) return;
            fixerClip ??= VirtualClip.Create("Fixer");
            foreach (var binding in bindings)
                fixerClip.SetFloatCurve(binding, AnimationCurve.Constant(0, 0.1F, 1));
        }
    }
}