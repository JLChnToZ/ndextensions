using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed partial class HumanoidAvatarProcessor {
        static readonly int[] remapChildBones;
        readonly Dictionary<Transform, HumanBodyBones> boneToHumanBone = new();
        readonly HashSet<Transform> skeletonBoneTransforms = new();
        readonly UnityObject assetRoot;
        readonly Animator animator;
        readonly Transform root;
        readonly Transform[] bones;
        readonly OverrideHumanDescription overrideHuman;

        static HumanoidAvatarProcessor() {
            remapChildBones = new int[(int)HumanBodyBones.LastBone];
            for (int bone = 0; bone < remapChildBones.Length; bone++) {
                remapChildBones[bone] = (int)HumanBodyBones.LastBone;
                var parentBone = HumanTrait.GetParentBone(bone);
                if (parentBone >= 0)
                    remapChildBones[parentBone] = remapChildBones[parentBone] == (int)HumanBodyBones.LastBone ? bone : -1;
            }
        }

        public static bool Process(
            Animator animator,
            UnityObject assetRoot,
            bool normalize = false,
            bool fixCrossLeg = false,
            Transform[] bones = null,
            OverrideHumanDescription? overrideHuman = null,
            AnimationRelocator animationRelocator = null
        ) {
            var processor = new HumanoidAvatarProcessor(animator, bones, assetRoot, overrideHuman) {
                animationRelocator = animationRelocator,
            };
            processor.ScanAffectedComponents();
            if (normalize) processor.Normalize();
            else if (fixCrossLeg) processor.NormalizeLeg();
            processor.FixArmatureRoot();
            processor.UpdateBindposes();
            if (fixCrossLeg) processor.FixCrossLeg();
            processor.FixSiblings();
            return processor.RegenerateAvatar();
        }

        HumanoidAvatarProcessor(Animator animator, Transform[] bones, UnityObject assetRoot, OverrideHumanDescription? overrideHuman) :
            this(animator.transform, bones, animator.avatar, assetRoot, overrideHuman) {
            this.animator = animator;
        }

        HumanoidAvatarProcessor(Transform root, Transform[] bones, Avatar avatar, UnityObject assetRoot, OverrideHumanDescription? overrideHuman) {
            this.root = root;
            this.assetRoot = assetRoot;
            this.overrideHuman = overrideHuman.Resolve(avatar);
            if (bones == null || bones.Length != (int)HumanBodyBones.LastBone) {
                bones = MecanimUtils.GuessHumanoidBodyBones(root);
                if (bones == null) throw new InvalidOperationException("Can not find humanoid bone mapping.");
            }
            for (var i = HumanBodyBones.Hips; i < HumanBodyBones.LastBone; i++) {
                var bone = bones[(int)i];
                if (bone != null) boneToHumanBone[bone] = i;
            }
            this.bones = bones;
        }

        bool RegenerateAvatar() {
            foreach (var bone in bones)
                for (var parent = bone; parent != null && parent != root; parent = parent.parent) {
                    if (!skeletonBoneTransforms.Add(parent)) break;
                    boneNames.Add(parent.name);
                }
            if (bones[0].parent == root) {
                skeletonBoneTransforms.Add(root);
                boneNames.Add(root.name);
            }
            var stack = new Stack<(int, Transform)>();
            stack.Push((0, root));
            while (stack.TryPop(out var entry)) {
                var (index, bone) = entry;
                if (index >= bone.childCount) continue;
                var child = bone.GetChild(index);
                stack.Push((index + 1, bone));
                stack.Push((0, child));
                if (!skeletonBoneTransforms.Contains(child))
                    TryAvoidAmbiguousBoneName(child);
                CachePosition(child, true);
            }
            var transformsToAdd = new Transform[skeletonBoneTransforms.Count];
            skeletonBoneTransforms.CopyTo(transformsToAdd);
            Array.Sort(transformsToAdd, new HierarchyComparer(true));
            var humanBoneNames = MecanimUtils.HumanBoneNames;
            var desc = new HumanDescription {
                armStretch = overrideHuman.armStretch,
                upperArmTwist = overrideHuman.upperArmTwist,
                lowerArmTwist = overrideHuman.lowerArmTwist,
                legStretch = overrideHuman.legStretch,
                lowerLegTwist = overrideHuman.lowerLegTwist,
                upperLegTwist = overrideHuman.upperLegTwist,
                feetSpacing = overrideHuman.feetSpacing,
                hasTranslationDoF = overrideHuman.hasTranslationDoF,
                human = new HumanBone[boneToHumanBone.Count],
                skeleton = Array.ConvertAll(transformsToAdd, ToSkeletonBone),
            };
            for (int i = 0, bone = 0; bone < (int)HumanBodyBones.LastBone; bone++) {
                var boneTransform = bones[bone];
                if (boneTransform == null) continue;
                ref var limit = ref overrideHuman.humanLimits[bone];
                desc.human[i++] = new HumanBone {
                    boneName = boneTransform.name,
                    humanName = humanBoneNames[bone],
                    limit = new HumanLimit {
                        useDefaultValues = limit.mode != OverrideMode.Override,
                        min = limit.min,
                        max = limit.max,
                        center = limit.center,
                    },
                };
            }
            if (animator != null) animator.avatar = null;
            RestoreCachedPositions(false);
            var rebuiltAvatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, desc);
            bool success = rebuiltAvatar.isValid;
            if (success) {
                rebuiltAvatar.name = $"{root.name} Avatar (Generated)";
                if (animator != null) animator.avatar = rebuiltAvatar;
                if (assetRoot != null) AssetDatabase.AddObjectToAsset(rebuiltAvatar, assetRoot);
            } else
                DestroyImmediate(rebuiltAvatar);
            RestoreAmbiguousBoneNames();
            return success;
        }

        static SkeletonBone ToSkeletonBone(Transform bone) {
#if UNITY_2021_3_OR_NEWER
            bone.GetLocalPositionAndRotation(out var position, out var rotation);
#else
            var position = transform.localPosition;
            var rotation = transform.localRotation;
#endif
            return new SkeletonBone {
                name = bone.name,
                position = position,
                rotation = rotation,
                scale = bone.localScale,
            };
        }
    }
}