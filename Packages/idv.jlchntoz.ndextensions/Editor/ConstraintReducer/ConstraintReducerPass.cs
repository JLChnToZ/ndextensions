using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.Dynamics.ManagedTypes;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class ConstraintReducerPass : Pass<ConstraintReducerPass> {
        const Axis ALL_AXES = Axis.X | Axis.Y | Axis.Z;
        readonly Queue<Transform> tempTransformQueue = new();
        readonly List<Component> tempComponents = new();
#if VRC_SDK_VRCSDK3
        readonly List<VRCConstraintBase> tempVRCConstraints = new();
        readonly Dictionary<Transform, LinkedList<VRCConstraintBase>> vrcConstraintSources = new();
#endif
        readonly Dictionary<string, PathType> pathOfInterests = new(StringComparer.Ordinal);
        readonly Dictionary<Transform, Transform> transferParents = new();

        public override string DisplayName => "Constraint Reducer";

        protected override void Execute(BuildContext context) {
            var rootTransform = context.AvatarRootTransform;
            if (!rootTransform.TryGetComponent(out ConstraintReducer tag)) return;
            UnityObject.DestroyImmediate(tag, true);

            var animContext = context.Extension<AnimatorServicesContext>();

            try {
#if VRC_SDK_VRCSDK3
                rootTransform.GetBuildableComponentsInChildren(tempVRCConstraints);
                foreach (var c in tempVRCConstraints) {
                    var transform = c.GetEffectiveTargetTransform();
                    if (!vrcConstraintSources.TryGetValue(transform, out var list))
                        vrcConstraintSources[transform] = list = new LinkedList<VRCConstraintBase>();
                    list.AddLast(c);
                }
#endif
                foreach (var c in rootTransform.GetBuildableComponentsInChildren<ParentConstraint>())
                    Process(animContext, c, rootTransform);
#if VRC_SDK_VRCSDK3
                foreach (var c in tempVRCConstraints)
                    if (c != null && c is VRCParentConstraintBase pc)
                        Process(animContext, pc, rootTransform);
                foreach (var pb in rootTransform.GetBuildableComponentsInChildren<VRCPhysBoneBase>()) {
                    var pbRootTransform = pb.GetRootTransform();
                    foreach (var kv in transferParents)
                        if (kv.Value == pbRootTransform || kv.Value.IsChildOf(pbRootTransform)) {
                            pb.ignoreTransforms ??= new List<Transform>();
                            pb.ignoreTransforms.Add(kv.Key);
                        }
                }
#endif
                foreach (var kv in transferParents)
                    kv.Key.SetParent(kv.Value, true);
            } finally {
                tempComponents.Clear();
#if VRC_SDK_VRCSDK3
                tempVRCConstraints.Clear();
                vrcConstraintSources.Clear();
#endif
                transferParents.Clear();
            }
        }

        void Process(AnimatorServicesContext context, ParentConstraint c, Transform rootTransform) {
            if (!c.isActiveAndEnabled || !c.constraintActive ||
                (c.rotationAxis & ALL_AXES) != ALL_AXES || (c.translationAxis & ALL_AXES) != ALL_AXES ||
                c.sourceCount != 1 || !CheckIfOnlyComponent(c))
                return;
            var source = c.GetSource(0);
            if (source.weight != 1F) return;
            var sourceTransform = source.sourceTransform;
            if (sourceTransform == null ||
                !sourceTransform.IsChildOf(rootTransform) ||
                !sourceTransform.gameObject.activeInHierarchy)
                return;
            var targetTransform = c.transform;
            if (!CheckFulfillAndGetDrivenPath(context, c.GetType(), sourceTransform, targetTransform, rootTransform, out var drivenActiveBinding))
                return;
            UnityObject.DestroyImmediate(c, true);
            transferParents[targetTransform] = sourceTransform;
            if (drivenActiveBinding.HasValue)
                CopyBinding(context, drivenActiveBinding.Value, rootTransform, targetTransform);
        }

#if VRC_SDK_VRCSDK3
        void Process(AnimatorServicesContext context, VRCParentConstraintBase c, Transform rootTransform) {
            if (!c.isActiveAndEnabled || !c.IsActive || c.FreezeToWorld ||
                !c.AffectsPositionX || !c.AffectsPositionY || !c.AffectsPositionZ ||
                !c.AffectsRotationX || !c.AffectsRotationY || !c.AffectsRotationZ ||
                c.Sources.Count != 1 || !CheckIfOnlyComponent(c))
                return;
            var source = c.Sources[0];
            if (source.Weight != 1F) return;
            var sourceTransform = source.SourceTransform;
            if (sourceTransform == null ||
                !sourceTransform.IsChildOf(rootTransform) ||
                !sourceTransform.gameObject.activeInHierarchy ||
                !CheckFulfillAndGetDrivenPath(context, c.GetType(), sourceTransform, c.transform, rootTransform, out var drivenActiveBinding))
                return;
            var targetTransform = c.GetEffectiveTargetTransform();
            UnityObject.DestroyImmediate(c, true);
            transferParents[targetTransform] = sourceTransform;
            if (drivenActiveBinding.HasValue)
                CopyBinding(context, drivenActiveBinding.Value, rootTransform, targetTransform);
        }
#endif

        bool CheckIfOnlyComponent(Component component) {
            component.GetComponents(tempComponents);
            foreach (var c in tempComponents) {
                if (c == component) continue;
                if (c is IConstraint) return false;
            }
#if VRC_SDK_VRCSDK3
            var transform = component.transform;
            foreach (var c in tempVRCConstraints)
                if (c != null && c != component && c.GetEffectiveTargetTransform() == transform)
                    return false;
#endif
            return true;
        }

        bool CheckFulfillAndGetDrivenPath(
            AnimatorServicesContext context,
            Type driverType,
            Transform source,
            Transform target,
            Transform root,
            out EditorCurveBinding? drivenActiveBinding
        ) {
            try {
                drivenActiveBinding = null;
                pathOfInterests[target.GetPath(root)] = PathType.Target;
                var commonParent = root;
                for (var c = target; c != root && c != null; c = c.parent)
                    if (source.IsChildOf(c)) {
                        commonParent = c;
                        break;
                    }
                for (var c = target.parent; c != commonParent && c != null; c = c.parent)
                    pathOfInterests[c.GetPath(root)] = PathType.TargetPath;
                for (var c = source; c != commonParent && c != null; c = c.parent) {
                    pathOfInterests[c.GetPath(root)] = PathType.SourceAndPath;
                    EnqueueScaleDriver(c, root);
                }
                while (tempTransformQueue.TryDequeue(out var transform))
                    for (; transform != commonParent && transform != root && transform != null; transform = transform.parent) {
                        var path = transform.GetPath(root);
                        if (pathOfInterests.ContainsKey(path)) break;
                        pathOfInterests[path] = PathType.ScaleDriver;
                        EnqueueScaleDriver(transform, root);
                    }
                foreach (var kv in pathOfInterests)
                    foreach (var clip in context.AnimationIndex.GetClipsForObjectPath(kv.Key))
                        foreach (var binding in clip.GetFloatCurveBindings()) {
                            if (binding.path != kv.Key) continue;
                            switch (kv.Value) {
                                case PathType.Target:
                                    if (binding.type.IsAssignableFrom(driverType))
                                        return false;
                                    goto case PathType.TargetPath;
                                case PathType.TargetPath:
                                    if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") {
                                        if (drivenActiveBinding.HasValue && drivenActiveBinding.Value.path != kv.Key) {
                                            drivenActiveBinding = null;
                                            return false;
                                        }
                                        drivenActiveBinding = binding;
                                    }
                                    break;
                                case PathType.SourceAndPath:
                                    if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                                        return false;
                                    goto case PathType.ScaleDriver;
                                case PathType.ScaleDriver:
                                    if (binding.type.IsSubclassOf(typeof(Transform)) && binding.propertyName.StartsWith("m_LocalScale"))
                                        return false;
                                    break;
                            }
                        }
                return true;
            } finally {
                pathOfInterests.Clear();
            }
        }

        void EnqueueScaleDriver(Transform transform, Transform root) {
            if (transform.TryGetComponent(out ScaleConstraint sc))
                for (int i = 0, sourceCount = sc.sourceCount; i < sourceCount; i++) {
                    var sourceTransform = sc.GetSource(i).sourceTransform;
                    if (sourceTransform != null && sourceTransform.IsChildOf(root))
                        tempTransformQueue.Enqueue(sourceTransform);
                }
#if VRC_SDK_VRCSDK3
            if (vrcConstraintSources.TryGetValue(transform, out var vcs))
                foreach (var vc in vcs)
                    if (vc is VRCScaleConstraintBase vsc)
                        foreach (var source in vsc.Sources) {
                            var sourceTransform = source.SourceTransform;
                            if (sourceTransform != null && sourceTransform.IsChildOf(root))
                                tempTransformQueue.Enqueue(sourceTransform);
                        }
#endif
        }

        void CopyBinding(AnimatorServicesContext context, EditorCurveBinding drivenActiveBinding, Transform root, Transform target) {
            var targetBinding = drivenActiveBinding;
            targetBinding.path = target.GetPath(root);
            foreach (var clip in context.AnimationIndex.GetClipsForBinding(drivenActiveBinding)) {
                var curve = clip.GetFloatCurve(drivenActiveBinding);
                if (curve != null) clip.SetFloatCurve(targetBinding, curve);
            }
        }

        enum PathType {
            ScaleDriver,
            SourceAndPath,
            TargetPath,
            Target,
        }
    }
}
