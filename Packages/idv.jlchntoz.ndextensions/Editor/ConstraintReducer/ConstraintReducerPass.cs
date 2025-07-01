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

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public class ConstraintReducerPass : Pass<ConstraintReducerPass> {
        const Axis ALL_AXES = Axis.X | Axis.Y | Axis.Z;
        static readonly HashSet<Transform> tempTransforms = new HashSet<Transform>();
        static readonly List<Component> tempComponents = new List<Component>();
#if VRC_SDK_VRCSDK3
        static readonly List<VRCConstraintBase> tempVRCConstraints = new List<VRCConstraintBase>();
        static readonly Dictionary<Transform, VRCConstraintBase> scaleConstraintTargets = new Dictionary<Transform, VRCConstraintBase>();
#endif
        static readonly Dictionary<string, PathType> pathOfInterests = new Dictionary<string, PathType>(StringComparer.Ordinal);
        static readonly Queue<Transform> tempTransformQueue = new Queue<Transform>();

        public override string DisplayName => "Constraint Reducer";

        protected override void Execute(BuildContext context) {
            var rootTransform = context.AvatarRootTransform;
            if (!rootTransform.TryGetComponent(out ConstraintReducer tag)) return;
            UnityObject.DestroyImmediate(tag, true);

            var relocatorContext = context.Extension<AnimationRelocatorContext>();

            try {
#if VRC_SDK_VRCSDK3
                rootTransform.GetComponentsInChildren(true, tempVRCConstraints);
                foreach (var c in tempVRCConstraints)
                    scaleConstraintTargets[c.GetEffectiveTargetTransform()] = c;
#endif
                var relocator = relocatorContext.Relocator;
                foreach (var c in rootTransform.GetComponentsInChildren<ParentConstraint>(true)) {
                    var transform = Process(c, relocator, rootTransform);
                    if (transform != null) tempTransforms.Add(transform);
                }
#if VRC_SDK_VRCSDK3
                foreach (var c in tempVRCConstraints)
                    if (c != null && c is VRCParentConstraintBase pc) {
                        var transform = Process(pc, relocator, rootTransform);
                        if (transform != null) tempTransforms.Add(transform);
                    }
                foreach (var pb in rootTransform.GetComponentsInChildren<VRCPhysBoneBase>(true)) {
                    var pbRootTransform = pb.GetRootTransform();
                    foreach (var transform in tempTransforms)
                        if (transform.IsChildOf(pbRootTransform)) {
                            if (pb.ignoreTransforms == null) pb.ignoreTransforms = new List<Transform>();
                            pb.ignoreTransforms.Add(transform);
                        }
                }
#endif
            } finally {
                tempTransforms.Clear();
                tempComponents.Clear();
#if VRC_SDK_VRCSDK3
                tempVRCConstraints.Clear();
                scaleConstraintTargets.Clear();
#endif
            }
        }

        static Transform Process(ParentConstraint c, AnimationRelocator relocator, Transform rootTransform) {
            if (!c.isActiveAndEnabled || !c.constraintActive ||
                (c.rotationAxis & ALL_AXES) != ALL_AXES || (c.translationAxis & ALL_AXES) != ALL_AXES ||
                c.sourceCount != 1 || !CheckIfOnlyComponent(c))
                return null;
            var source = c.GetSource(0);
            if (source.weight != 1F) return null;
            var sourceTransform = source.sourceTransform;
            if (sourceTransform == null ||
                !sourceTransform.IsChildOf(rootTransform) ||
                !sourceTransform.gameObject.activeInHierarchy)
                return null;
            var targetTransform = c.transform;
            if (!CheckFulfillAndGetDrivenPath(relocator, c.GetType(), sourceTransform, targetTransform, rootTransform, out var drivenActivePath))
                return null;
            UnityObject.DestroyImmediate(c, true);
            MoveUnderSource(relocator, targetTransform, sourceTransform, rootTransform, drivenActivePath);
            return targetTransform;
        }

#if VRC_SDK_VRCSDK3
        static Transform Process(VRCParentConstraintBase c, AnimationRelocator relocator, Transform rootTransform) {
            if (!c.isActiveAndEnabled || !c.IsActive || c.FreezeToWorld ||
                !c.AffectsPositionX || !c.AffectsPositionY || !c.AffectsPositionZ ||
                !c.AffectsRotationX || !c.AffectsRotationY || !c.AffectsRotationZ ||
                c.Sources.Count != 1 || !CheckIfOnlyComponent(c))
                return null;
            var source = c.Sources[0];
            if (source.Weight != 1F) return null;
            var sourceTransform = source.SourceTransform;
            if (sourceTransform == null ||
                !sourceTransform.IsChildOf(rootTransform) ||
                !sourceTransform.gameObject.activeInHierarchy)
                return null;
            if (!CheckFulfillAndGetDrivenPath(relocator, c.GetType(), sourceTransform, c.transform, rootTransform, out var drivenActivePath))
                return null;
            var targetTransform = c.GetEffectiveTargetTransform();
            UnityObject.DestroyImmediate(c, true);
            MoveUnderSource(relocator, targetTransform, sourceTransform, rootTransform, drivenActivePath);
            return targetTransform;
        }
#endif

        static bool CheckIfOnlyComponent(Component component) {
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

        static bool CheckFulfillAndGetDrivenPath(
            AnimationRelocator relocator,
            Type driverType,
            Transform source,
            Transform target,
            Transform root,
            out string drivenActivePath
        ) {
            try {
                drivenActivePath = null;
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
                foreach (var clip in relocator.OriginalClips)
                    foreach (var binding in AnimationUtility.GetCurveBindings(relocator[clip])) {
                        var path = binding.path;
                        if (!pathOfInterests.TryGetValue(path, out var pathType)) continue;
                        var type = binding.type;
                        var propName = binding.propertyName;
                        switch (pathType) {
                            case PathType.Target:
                                if (type.IsAssignableFrom(driverType)) return false;
                                goto case PathType.TargetPath;
                            case PathType.TargetPath:
                                if (type == typeof(GameObject) && propName == "m_IsActive") {
                                    if (drivenActivePath == null)
                                        drivenActivePath = path;
                                    else if (drivenActivePath != path) {
                                        drivenActivePath = null;
                                        return false;
                                    }
                                }
                                break;
                            case PathType.SourceAndPath:
                                if (type == typeof(GameObject) && propName == "m_IsActive")
                                    return false;
                                goto case PathType.ScaleDriver;
                            case PathType.ScaleDriver:
                                if (type.IsSubclassOf(typeof(Transform)) && propName.StartsWith("m_LocalScale"))
                                    return false;
                                break;
                        }
                    }
                return true;
            } finally {
                pathOfInterests.Clear();
            }
        }

        static void EnqueueScaleDriver(Transform transform, Transform root) {
            if (transform.TryGetComponent(out ScaleConstraint sc))
                for (int i = 0, sourceCount = sc.sourceCount; i < sourceCount; i++) {
                    var sourceTransform = sc.GetSource(i).sourceTransform;
                    if (sourceTransform != null && sourceTransform.IsChildOf(root))
                        tempTransformQueue.Enqueue(sourceTransform);
                }
#if VRC_SDK_VRCSDK3
            if (scaleConstraintTargets.TryGetValue(transform, out var vsc))
                foreach (var s in vsc.Sources) {
                    var sourceTransform = s.SourceTransform;
                    if (sourceTransform != null && sourceTransform.IsChildOf(root))
                        tempTransformQueue.Enqueue(sourceTransform);
                }
#endif
        }

        static void MoveUnderSource(
            AnimationRelocator relocator,
            Transform target,
            Transform dest,
            Transform root,
            string drivenActivePath
        ) {
            if (target == null || dest == null || target == dest || target.parent == dest) return;
            var oldPath = target.GetPath(root);
            target.name = GameObjectUtility.GetUniqueNameForSibling(dest, target.name);
            target.SetParent(dest, true);
            var newPath = target.GetPath(root);
            foreach (var clip in relocator.OriginalClips) {
                var newClip = relocator[clip];
                bool isCloned = newClip != clip;
                foreach (var binding in AnimationUtility.GetCurveBindings(newClip)) {
                    if (!ShouldRemapBinding(in binding, out var newBinding, oldPath, newPath, drivenActivePath, out var keepOld)) continue;
                    if (!isCloned) {
                        newClip = relocator.GetClone(newClip);
                        isCloned = true;
                    }
                    var curve = AnimationUtility.GetEditorCurve(newClip, binding);
                    if (!keepOld) AnimationUtility.SetEditorCurve(newClip, binding, null);
                    if (curve != null) AnimationUtility.SetEditorCurve(newClip, newBinding, curve);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(newClip)) {
                    if (!ShouldRemapBinding(in binding, out var newBinding, oldPath, newPath, drivenActivePath, out var keepOld)) continue;
                    if (!isCloned) {
                        newClip = relocator.GetClone(newClip);
                        isCloned = true;
                    }
                    var keyframes = AnimationUtility.GetObjectReferenceCurve(newClip, binding);
                    if (!keepOld) AnimationUtility.SetObjectReferenceCurve(newClip, binding, null);
                    if (keyframes != null) AnimationUtility.SetObjectReferenceCurve(newClip, newBinding, keyframes);
                }
            }
        }

        static bool ShouldRemapBinding(
            in EditorCurveBinding binding,
            out EditorCurveBinding newBinding,
            string oldPath,
            string newPath,
            string drivenActivePath,
            out bool keepOld
        ) {
            newBinding = binding;
            var path = binding.path;
            if (path == drivenActivePath && binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive") {
                newBinding.path = newPath;
                keepOld = true;
                return true;
            }
            if (path == oldPath) {
                newBinding.path = newPath;
                keepOld = false;
                return true;
            }
            if (path.Length > oldPath.Length && path.StartsWith(oldPath) && path[oldPath.Length] == '/') {
                newBinding.path = newPath + path[oldPath.Length..];
                keepOld = false;
                return true;
            }
            keepOld = true;
            return false;
        }

        enum PathType {
            ScaleDriver,
            SourceAndPath,
            TargetPath,
            Target,
        }
    }
}
