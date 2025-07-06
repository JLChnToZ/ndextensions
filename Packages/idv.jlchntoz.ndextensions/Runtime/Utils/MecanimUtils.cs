using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JLChnToZ.NDExtensions {
    public static class MecanimUtils {
        delegate Dictionary<int, Transform> MapBones(Transform root, Dictionary<Transform, bool> validBones);
        static readonly FieldInfo parentNameField = typeof(SkeletonBone).GetField("parentName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static string[] humanNames, muscleNames;
        static MapBones mapBones;

        public static string[] HumanBoneNames => humanNames;

        public static string[] MuscleNames => muscleNames;

        public static readonly HumanDescription defaultHumanDescription = new() {
            human = Array.Empty<HumanBone>(),
            skeleton = Array.Empty<SkeletonBone>(),
            armStretch = 0.05F,
            upperArmTwist = 0.5F,
            lowerArmTwist = 0.5F,
            legStretch = 0.05F,
            lowerLegTwist = 0.5F,
            upperLegTwist = 0.5F,
            feetSpacing = 0.0F,
            hasTranslationDoF = false,
        };

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        static void Init() {
            humanNames = HumanTrait.BoneName;
            muscleNames = HumanTrait.MuscleName;
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
            var result = new Transform[HumanTrait.BoneCount];
            var hw = new Queue<Transform>();
            if (!ignoreAvatar) {
                var animator = root.GetComponentInParent<Animator>(true);
                if (animator != null && FetchHumanoidBodyBones(animator.avatar, root, result, hw)) return result;
            }
            if (mapBones == null) {
                Debug.LogWarning("Cannot find AvatarAutoMapper, humanoid bone mapping is not available.");
                return result;
            }
            // We already have hips when we try to fetch humanoid bones from avatar
            if (result[0] != null)
                root = result[0].parent;
            else {
                hw.Enqueue(root);
                while (hw.TryDequeue(out var current)) {
                    if (current.name.Equals("hips", StringComparison.OrdinalIgnoreCase)) {
                        root = current.parent;
                        break;
                    }
                    foreach (Transform child in current) hw.Enqueue(child);
                }
                hw.Clear();
            }
            var vaildBoneMap = new Dictionary<Transform, bool>();
            if (validBones != null)
                foreach (var bone in validBones)
                    vaildBoneMap[bone] = true;
            else {
                hw.Enqueue(root);
                while (hw.TryDequeue(out var current)) {
                    vaildBoneMap[current] = true;
                    foreach (Transform child in current) hw.Enqueue(child);
                }
            }
            var rawResult = mapBones(root, vaildBoneMap);
            foreach (var kv in rawResult)
                if (result[kv.Key] == null)
                    result[kv.Key] = kv.Value;
            return result;
        }

        // This is slower but less strict than using Animator.GetBoneTransform
        static bool FetchHumanoidBodyBones(Avatar avatar, Transform root, Transform[] result, Queue<Transform> hw) {
            if (avatar == null || root == null) return false;
            var desc = avatar.humanDescription;
            if (desc.human == null && desc.human.Length == 0) return false;
            var boneNames = new string[humanNames.Length];
            foreach (var bone in desc.human) {
                if (string.IsNullOrEmpty(bone.humanName) || string.IsNullOrEmpty(bone.boneName)) continue;
                int i = Array.IndexOf(humanNames, bone.humanName);
                if (i < 0) continue;
                boneNames[i] = bone.boneName;
            }
            for (int i = 0; i < boneNames.Length; i++) {
                if (boneNames[i] == null) continue;
                int p = i;
                while (true) {
                    p = HumanTrait.GetParentBone(p);
                    // Negative parent index means it is root.
                    if (p < 0) {
                        hw.Enqueue(root);
                        break;
                    }
                    if (result[p] != null) {
                        hw.Enqueue(result[p]);
                        break;
                    }
                    // If it is a required bone and we can't find it, the hierarchy is already broken.
                    if (HumanTrait.RequiredBone(p)) break;
                }
                while (hw.TryDequeue(out var current)) {
                    if (boneNames[i] == current.name) {
                        result[i] = current;
                        break;
                    }
                    foreach (Transform child in current) hw.Enqueue(child);
                }
                hw.Clear();
            }
            return avatar.isHuman;
        }

        public static Transform[] FetchHumanoidBodyBones(Avatar avatar, Transform root) {
            var result = new Transform[HumanTrait.BoneCount];
            FetchHumanoidBodyBones(avatar, root, result, new());
            return result;
        }

        public static HumanDescription GetHumanDescriptionOrDefault(this Avatar avatar) =>
            avatar == null ? defaultHumanDescription : avatar.humanDescription;

        public static void ApplyTPose(this Avatar avatar, Transform root, bool humanBoneOnly = true, bool applyScale = true, bool undo = false) {
#if UNITY_EDITOR
            if (undo && EditorApplication.isPlayingOrWillChangePlaymode) undo = false;
#endif
            if (avatar == null || root == null) return;
            var walker = new Queue<Transform>();
            HashSet<Transform> whiteList = null;
            if (humanBoneOnly && avatar.isHuman) {
                var bones = new Transform[HumanTrait.BoneCount];
                FetchHumanoidBodyBones(avatar, root, bones, walker);
                whiteList = new HashSet<Transform>(bones.Length);
                foreach (var bone in bones)
                    if (bone != null)
                        whiteList.Add(bone);
            }
            var humanDesc = avatar.humanDescription;
            var skeletonMapping = new Dictionary<(string bone, string parent), SkeletonBone>();
            foreach (var skeleton in humanDesc.skeleton) {
                if (string.IsNullOrEmpty(skeleton.name)) continue;
                skeletonMapping[(skeleton.name, parentNameField.GetValue(skeleton) as string ?? "")] = skeleton;
            }
            foreach (Transform child in root) walker.Enqueue(child);
            while (walker.TryDequeue(out var current)) {
                foreach (Transform child in current) walker.Enqueue(child);
                if ((whiteList != null && !whiteList.Contains(current)) ||
                    !(skeletonMapping.TryGetValue((current.name, current.parent.name), out var skeletonBone) ||
                    skeletonMapping.TryGetValue((current.name, ""), out skeletonBone)))
                    continue;
#if UNITY_EDITOR
                if (undo) Undo.RecordObject(current, "Apply T-Pose");
#endif
                current.SetLocalPositionAndRotation(skeletonBone.position, skeletonBone.rotation);
                if (applyScale) current.localScale = skeletonBone.scale;
            }
#if UNITY_EDITOR
            if (undo) Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
#endif
        }
    }
}