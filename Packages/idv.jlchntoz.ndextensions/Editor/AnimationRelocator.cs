using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public class AnimationRelocator {
        readonly Transform root;
        readonly Dictionary<RuntimeAnimatorController, AnimatorOverrideController> controllerOverrides = new();
        readonly Dictionary<Component, HashSet<AnimationClip>> component2Anim = new();
        readonly Dictionary<AnimationClip, HashSet<AnimatorController>> dependencies = new();
        readonly Dictionary<AnimationClip, AnimationClip> clonedClips = new();

        public RuntimeAnimatorController this[RuntimeAnimatorController controller] =>
            controller != null &&
            controllerOverrides.TryGetValue(controller, out var overrideController) &&
            overrideController != null ? overrideController : controller;

        public AnimationClip this[AnimationClip clip] =>
            clip != null && clonedClips.TryGetValue(clip, out var clone) ? clone : clip;

        public AnimationRelocator(Transform root) => this.root = root;

        public void AddController(RuntimeAnimatorController baseController) {
            if (baseController == null || controllerOverrides.ContainsKey(baseController)) return;
            controllerOverrides[baseController] = null;
            var pending = new Stack<(RuntimeAnimatorController controller, UnityObject target)>();
            Stack<AnimatorOverrideController> overrideStack = null;
            HashSet<AnimationClip> allClips = null;
            AnimatorController rootController = null;
            pending.Push((baseController, baseController));
            while (pending.TryPop(out var entry)) {
                if (entry.target == null) continue;
                if (entry.target is AnimatorController controller) {
                    rootController = controller;
                    foreach (var layer in controller.layers)
                        pending.Push((controller, layer.stateMachine));
                    continue;
                }
                if (entry.target is AnimatorOverrideController overrideController) {
                    pending.Push((overrideController, overrideController.runtimeAnimatorController));
                    if (overrideStack == null) overrideStack = new();
                    if (allClips == null) allClips = new();
                    overrideStack.Push(overrideController);
                    continue;
                }
                if (entry.target is AnimatorStateMachine stateMachine) {
                    foreach (var subState in stateMachine.states)
                        pending.Push((entry.controller, subState.state));
                    foreach (var subStateMachine in stateMachine.stateMachines)
                        pending.Push((entry.controller, subStateMachine.stateMachine));
                    continue;
                }
                if (entry.target is AnimatorState state) {
                    pending.Push((entry.controller, state.motion));
                    continue;
                }
                if (entry.target is BlendTree blendTree) {
                    foreach (var childMotion in blendTree.children)
                        pending.Push((entry.controller, childMotion.motion));
                    continue;
                }
                if (entry.target is AnimationClip clip) {
                    allClips?.Add(clip);
                    if (!dependencies.TryGetValue(clip, out var controllers))
                        dependencies[clip] = controllers = new();
                    controllers.Add(entry.controller as AnimatorController);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        RecordClipUsage(clip, binding);
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        RecordClipUsage(clip, binding);
                    continue;
                }
            }
            if (overrideStack == null) return;
            var clipsStack = new Stack<AnimationClip>();
            while (overrideStack.TryPop(out var controller)) {
                foreach (var clip in allClips)
                    clipsStack.Push(clip);
                while (clipsStack.TryPop(out var clip)) {
                    var overrided = controller[clip];
                    if (overrided == null || overrided == clip) continue;
                    allClips.Add(overrided);
                    if (!dependencies.TryGetValue(overrided, out var controllers))
                        dependencies[overrided] = controllers = new();
                    controllers.Add(rootController);
                }
            }
        }

        void RecordClipUsage(AnimationClip clip, EditorCurveBinding binding) {
            var path = binding.path;
            if (string.IsNullOrEmpty(path)) return;
            var bone = root.Find(path);
            if (bone == null) return;
            var type = binding.type;
            if (!type.IsSubclassOf(typeof(Component))) return;
            var target = bone.GetComponent(type);
            if (target == null) return;
            if (!component2Anim.TryGetValue(target, out var clips))
                component2Anim[bone] = clips = new();
            clips.Add(clip);
        }

        public IEnumerable<AnimationClip> GetRelaventClipsForEdit(Component component) {
            if (component == null || !component2Anim.TryGetValue(component, out var animSet))
                yield break;
            foreach (var clip in animSet) {
                if (!clonedClips.TryGetValue(clip, out var clone)) {
                    clonedClips[clip] = clone = Instantiate(clip);
                    if (dependencies.TryGetValue(clip, out var depdControllers))
                        foreach (var controller in depdControllers) {
                            if (controller == null) continue;
                            if (!controllerOverrides.TryGetValue(controller, out var overrideController) || overrideController == null)
                                controllerOverrides[controller] = overrideController = new AnimatorOverrideController {
                                    name = $"{controller.name} (Override)",
                                    runtimeAnimatorController = controller,
                                };
                            overrideController[clip] = clone;
                        }
                }
                yield return clone;
            }
        }

        public ICollection<AnimationClip> GetAllCloneClips() => clonedClips.Values;

        public ICollection<AnimatorOverrideController> GetAllOverrideControllers() => controllerOverrides.Values;
    }

    public static class TransformAnimationRelocator {
        public static void SetTransformPositionAndRotation(
            this AnimationRelocator relocator,
            Transform transform,
            Vector3 newPos,
            Quaternion newRot,
            bool isLocal = true
        ) {
            transform.GetLocalPositionAndRotation(out var oldPos, out var oldRot);
            if (isLocal)
                transform.SetLocalPositionAndRotation(newPos, newRot);
            else {
                transform.SetPositionAndRotation(newPos, newRot);
                var parent = transform.parent;
                if (parent != null) {
                    newPos = parent.InverseTransformPoint(newPos);
                    newRot = Quaternion.Inverse(parent.rotation) * newRot;
                }
            }
            var deltaPos = newPos - oldPos;
            var deltaRot = newRot * Quaternion.Inverse(oldRot);
            foreach (var clip in relocator.GetRelaventClipsForEdit(transform)) {
                EditorCurveBinding? qX = null, qY = null, qZ = null, qW = null, eulerX = null, eulerY = null, eulerZ = null;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    switch (binding.propertyName) {
                        case "m_LocalPosition.x":
                            OffsetSingle(clip, binding, deltaPos.x);
                            break;
                        case "m_LocalPosition.y":
                            OffsetSingle(clip, binding, deltaPos.y);
                            break;
                        case "m_LocalPosition.z":
                            OffsetSingle(clip, binding, deltaPos.z);
                            break;
                        case "localRotation.x":
                        case "m_LocalRotation.x":
                            qX = binding;
                            break;
                        case "localRotation.y":
                        case "m_LocalRotation.y":
                            qY = binding;
                            break;
                        case "localRotation.z":
                        case "m_LocalRotation.z":
                            qZ = binding;
                            break;
                        case "localRotation.w":
                        case "m_LocalRotation.w":
                            qW = binding;
                            break;
                        case "localEulerAngles.x":
                        case "m_LocalEulerAngles.x":
                        case "localEulerAnglesBaked.x":
                        case "m_LocalEulerAnglesBaked.x":
                        case "localEulerAnglesRaw.x":
                        case "m_LocalEulerAnglesRaw.x":
                            eulerX = binding;
                            break;
                        case "localEulerAngles.y":
                        case "m_LocalEulerAngles.y":
                        case "localEulerAnglesBaked.y":
                        case "m_LocalEulerAnglesBaked.y":
                        case "localEulerAnglesRaw.y":
                        case "m_LocalEulerAnglesRaw.y":
                            eulerY = binding;
                            break;
                        case "localEulerAngles.z":
                        case "m_LocalEulerAngles.z":
                        case "localEulerAnglesBaked.z":
                        case "m_LocalEulerAnglesBaked.z":
                        case "localEulerAnglesRaw.z":
                        case "m_LocalEulerAnglesRaw.z":
                            eulerZ = binding;
                            break;
                    }
                if (qX.HasValue && qY.HasValue && qZ.HasValue && qW.HasValue)
                    OffsetRotation(clip, qX.Value, qY.Value, qZ.Value, qW.Value, deltaRot);
                else if (eulerX.HasValue && eulerY.HasValue && eulerZ.HasValue)
                    OffsetRotation(clip, eulerX.Value, eulerY.Value, eulerZ.Value, deltaRot);
            }
        }

        static void OffsetSingle(AnimationClip clip, EditorCurveBinding binding, float offset) {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) return;
            for (var i = 0; i < curve.length; i++) {
                var key = curve[i];
                key.value += offset;
                curve.MoveKey(i, key);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        static void OffsetRotation(
            AnimationClip clip,
            EditorCurveBinding qx,
            EditorCurveBinding qy,
            EditorCurveBinding qz,
            EditorCurveBinding qw,
            Quaternion offset
        ) {
            var xCurve = AnimationUtility.GetEditorCurve(clip, qx);
            if (xCurve == null) return;
            var yCurve = AnimationUtility.GetEditorCurve(clip, qy);
            if (yCurve == null) return;
            var zCurve = AnimationUtility.GetEditorCurve(clip, qz);
            if (zCurve == null) return;
            var wCurve = AnimationUtility.GetEditorCurve(clip, qw);
            if (wCurve == null) return;
            var times = new HashSet<float>();
            GatherTime(xCurve, times);
            GatherTime(yCurve, times);
            GatherTime(zCurve, times);
            GatherTime(wCurve, times);
            var timesArray = new float[times.Count];
            times.CopyTo(timesArray);
            Array.Sort(timesArray);
            AnimationCurve newXCurve = new(), newYCurve = new(), newZCurve = new(), newWCurve = new();
            for (var i = 0; i < timesArray.Length; i++) {
                var time = timesArray[i];
                var q = offset * new Quaternion(
                    xCurve.Evaluate(time),
                    yCurve.Evaluate(time),
                    zCurve.Evaluate(time),
                    wCurve.Evaluate(time)
                ).normalized;
                newXCurve.AddKey(time, q.x);
                newYCurve.AddKey(time, q.y);
                newZCurve.AddKey(time, q.z);
                newWCurve.AddKey(time, q.w);
            }
            for (var i = 0; i < timesArray.Length; i++) {
                newXCurve.SmoothTangents(i, 0);
                newYCurve.SmoothTangents(i, 0);
                newZCurve.SmoothTangents(i, 0);
                newWCurve.SmoothTangents(i, 0);
            }
            clip.SetCurve(qx.path, typeof(Transform), qx.propertyName, newXCurve);
            clip.SetCurve(qy.path, typeof(Transform), qy.propertyName, newYCurve);
            clip.SetCurve(qz.path, typeof(Transform), qz.propertyName, newZCurve);
            clip.SetCurve(qw.path, typeof(Transform), qw.propertyName, newWCurve);
        }
        static void OffsetRotation(
            AnimationClip clip,
            EditorCurveBinding eulerX,
            EditorCurveBinding eulerY,
            EditorCurveBinding eulerZ,
            Quaternion offset
        ) {
            var xCurve = AnimationUtility.GetEditorCurve(clip, eulerX);
            if (xCurve == null) return;
            var yCurve = AnimationUtility.GetEditorCurve(clip, eulerY);
            if (yCurve == null) return;
            var zCurve = AnimationUtility.GetEditorCurve(clip, eulerZ);
            if (zCurve == null) return;
            var times = new HashSet<float>();
            GatherTime(xCurve, times);
            GatherTime(yCurve, times);
            GatherTime(zCurve, times);
            var timesArray = new float[times.Count];
            times.CopyTo(timesArray);
            Array.Sort(timesArray);
            AnimationCurve newXCurve = new(), newYCurve = new(), newZCurve = new(), newWCurve = new();
            for (var i = 0; i < timesArray.Length; i++) {
                var time = timesArray[i];
                var q = offset * Quaternion.Euler(
                    xCurve.Evaluate(time),
                    yCurve.Evaluate(time),
                    zCurve.Evaluate(time)
                ).normalized;
                newXCurve.AddKey(time, q.x);
                newYCurve.AddKey(time, q.y);
                newZCurve.AddKey(time, q.z);
                newWCurve.AddKey(time, q.w);
            }
            for (var i = 0; i < timesArray.Length; i++) {
                newXCurve.SmoothTangents(i, 0);
                newYCurve.SmoothTangents(i, 0);
                newZCurve.SmoothTangents(i, 0);
                newWCurve.SmoothTangents(i, 0);
            }
            AnimationUtility.SetEditorCurve(clip, eulerX, null);
            AnimationUtility.SetEditorCurve(clip, eulerY, null);
            AnimationUtility.SetEditorCurve(clip, eulerZ, null);
            clip.SetCurve(eulerX.path, typeof(Transform), "localRotation.x", newXCurve);
            clip.SetCurve(eulerX.path, typeof(Transform), "localRotation.y", newYCurve);
            clip.SetCurve(eulerX.path, typeof(Transform), "localRotation.z", newZCurve);
            clip.SetCurve(eulerX.path, typeof(Transform), "localRotation.w", newWCurve);
        }

        static void GatherTime(AnimationCurve curve, HashSet<float> times) {
            for (var i = 0; i < curve.length; i++)
                times.Add(curve[i].time);
        }
    }
}