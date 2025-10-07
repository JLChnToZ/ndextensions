using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf.animator;

namespace JLChnToZ.NDExtensions.Editors {
    public static class TransformAnimationRelocator {
        public static void SetTransformPositionAndRotation(
            this AnimationIndex src,
            Transform root,
            Transform transform,
            Vector3 newPos,
            Quaternion newRot,
            Vector3? scale = null,
            bool isLocal = true
        ) {
            transform.GetLocalPositionAndRotation(out var oldPos, out var oldRot);
            var deltaScale = Vector3.one;
            if (isLocal) {
                transform.SetLocalPositionAndRotation(newPos, newRot);
                var oldScale = transform.localScale;
                if (scale.HasValue) {
                    transform.localScale = scale.Value;
                    deltaScale = new Vector3(
                        scale.Value.x / oldScale.x,
                        scale.Value.y / oldScale.y,
                        scale.Value.z / oldScale.z
                    );
                }
            } else {
                transform.SetPositionAndRotation(newPos, newRot);
                var oldScale = transform.lossyScale;
                var parent = transform.parent;
                if (parent != null) {
                    newPos = parent.InverseTransformPoint(newPos);
                    newRot = Quaternion.Inverse(parent.rotation) * newRot;
                    if (scale.HasValue) {
                        var newScale = new Vector3(
                            scale.Value.x / oldScale.x,
                            scale.Value.y / oldScale.y,
                            scale.Value.z / oldScale.z
                        );
                        oldScale = parent.localScale;
                        transform.localScale = newScale;
                        deltaScale = new Vector3(
                            newScale.x / oldScale.x,
                            newScale.y / oldScale.y,
                            newScale.z / oldScale.z
                        );
                    }
                } else if (scale.HasValue) {
                    transform.localScale = scale.Value;
                    deltaScale = new Vector3(
                        scale.Value.x / oldScale.x,
                        scale.Value.y / oldScale.y,
                        scale.Value.z / oldScale.z
                    );
                }
            }
            var deltaPos = newPos - oldPos;
            var deltaRot = newRot * Quaternion.Inverse(oldRot);
            var path = transform.GetPath(root);
            foreach (var clip in src.GetClipsForObjectPath(path)) {
                EditorCurveBinding? qX = null, qY = null, qZ = null, qW = null, eulerX = null, eulerY = null, eulerZ = null;
                foreach (var binding in clip.GetFloatCurveBindings()) {
                    if (binding.type != typeof(Transform) && binding.path != path) continue;
                    switch (binding.propertyName) {
                        case "m_LocalPosition.x":
                            OffsetSingle(clip, binding, deltaPos.x, 1);
                            break;
                        case "m_LocalPosition.y":
                            OffsetSingle(clip, binding, deltaPos.y, 1);
                            break;
                        case "m_LocalPosition.z":
                            OffsetSingle(clip, binding, deltaPos.z, 1);
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
                        case "m_LocalScale.x":
                            OffsetSingle(clip, binding, 0, deltaScale.x);
                            break;
                        case "m_LocalScale.y":
                            OffsetSingle(clip, binding, 0, deltaScale.y);
                            break;
                        case "m_LocalScale.z":
                            OffsetSingle(clip, binding, 0, deltaScale.z);
                            break;
                    }
                    if (qX.HasValue && qY.HasValue && qZ.HasValue && qW.HasValue) {
                        OffsetRotation(clip, qX.Value, qY.Value, qZ.Value, qW.Value, deltaRot);
                        qX = qY = qZ = qW = null;
                    } else if (eulerX.HasValue && eulerY.HasValue && eulerZ.HasValue) {
                        OffsetRotation(clip, eulerX.Value, eulerY.Value, eulerZ.Value, deltaRot);
                        eulerX = eulerY = eulerZ = null;
                    }
                }
            }
        }

        static void OffsetSingle(VirtualClip clip, EditorCurveBinding binding, float offset, float scale) {
            var curve = clip.GetFloatCurve(binding);
            if (curve == null) return;
            for (var i = 0; i < curve.length; i++) {
                var key = curve[i];
                key.value = key.value * scale + offset;
                curve.MoveKey(i, key);
            }
            clip.SetFloatCurve(binding, curve);
        }

        static void OffsetRotation(
            VirtualClip clip,
            EditorCurveBinding qx,
            EditorCurveBinding qy,
            EditorCurveBinding qz,
            EditorCurveBinding qw,
            Quaternion offset
        ) {
            var xCurve = clip.GetFloatCurve(qx);
            if (xCurve == null) return;
            var yCurve = clip.GetFloatCurve(qy);
            if (yCurve == null) return;
            var zCurve = clip.GetFloatCurve(qz);
            if (zCurve == null) return;
            var wCurve = clip.GetFloatCurve(qw);
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
            clip.SetFloatCurve(qx.path, typeof(Transform), qx.propertyName, newXCurve);
            clip.SetFloatCurve(qy.path, typeof(Transform), qy.propertyName, newYCurve);
            clip.SetFloatCurve(qz.path, typeof(Transform), qz.propertyName, newZCurve);
            clip.SetFloatCurve(qw.path, typeof(Transform), qw.propertyName, newWCurve);
        }

        static void OffsetRotation(
            VirtualClip clip,
            EditorCurveBinding eulerX,
            EditorCurveBinding eulerY,
            EditorCurveBinding eulerZ,
            Quaternion offset
        ) {
            var xCurve = clip.GetFloatCurve(eulerX);
            if (xCurve == null) return;
            var yCurve = clip.GetFloatCurve(eulerY);
            if (yCurve == null) return;
            var zCurve = clip.GetFloatCurve(eulerZ);
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
            clip.SetFloatCurve(eulerX, null);
            clip.SetFloatCurve(eulerY, null);
            clip.SetFloatCurve(eulerZ, null);
            clip.SetFloatCurve(eulerX.path, typeof(Transform), "localRotation.x", newXCurve);
            clip.SetFloatCurve(eulerX.path, typeof(Transform), "localRotation.y", newYCurve);
            clip.SetFloatCurve(eulerX.path, typeof(Transform), "localRotation.z", newZCurve);
            clip.SetFloatCurve(eulerX.path, typeof(Transform), "localRotation.w", newWCurve);
        }

        static void GatherTime(AnimationCurve curve, HashSet<float> times) {
            for (var i = 0; i < curve.length; i++)
                times.Add(curve[i].time);
        }
    }
}