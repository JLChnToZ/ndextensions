using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    public static class MecanimUtils {
        delegate Dictionary<int, Transform> MapBones(Transform root, Dictionary<Transform, bool> validBones);
        static readonly MapBones mapBones;

        static MecanimUtils() {
            var type = Type.GetType("UnityEditor.AvatarAutoMapper, UnityEditor", false);
            if (type != null) {
                var delegateType = typeof(MapBones);
                var method = type.GetMethod(
                    "MapBones",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Array.ConvertAll(delegateType.GetMethod(nameof(MapBones.Invoke)).GetParameters(), p => p.ParameterType),
                    null
                );
                if (method != null) mapBones = (MapBones)Delegate.CreateDelegate(delegateType, method, false);
            }
        }

        public static Transform[] GuessHumanoidBodyBones(Transform root, IEnumerable<Transform> validBones = null, bool ignoreAvatar = false) {
            var animator = root.GetComponentInParent<Animator>(true);
            var result = new Transform[HumanTrait.BoneCount];
            if (!ignoreAvatar && animator != null && animator.avatar != null && animator.avatar.isHuman) {
                var rootGameObject = animator.gameObject;
                bool wasEnabled = animator.enabled, wasActive = rootGameObject.activeSelf;
                if (!wasEnabled) animator.enabled = true;
                if (!wasActive) rootGameObject.SetActive(true);
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++) {
                    var boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null) result[(int)bone] = boneTransform;
                }
                if (!wasEnabled) animator.enabled = wasEnabled;
                if (!wasActive) rootGameObject.SetActive(wasActive);
                return result;
            }
            var pending = new Queue<Transform>();
            pending.Enqueue(root);
            while (pending.TryDequeue(out var current)) {
                if (current.name.Equals("hips", StringComparison.OrdinalIgnoreCase)) {
                    root = current.parent;
                    break;
                }
                foreach (Transform child in current) pending.Enqueue(child);
            }
            if (mapBones == null) {
                Debug.LogWarning("Can not find AvatarAutoMapper, humanoid bone mapping is not available.");
                return null;
            }
            var vaildBoneMap = new Dictionary<Transform, bool>();
            if (validBones != null)
                foreach (var bone in validBones)
                    vaildBoneMap[bone] = true;
            else {
                pending.Clear();
                pending.Enqueue(root);
                while (pending.TryDequeue(out var current)) {
                    vaildBoneMap[current] = true;
                    foreach (Transform child in current) pending.Enqueue(child);
                }
            }
            var rawResult = mapBones(root, vaildBoneMap);
            foreach (var kv in rawResult) result[kv.Key] = kv.Value;
            return result;
        }
    }
}