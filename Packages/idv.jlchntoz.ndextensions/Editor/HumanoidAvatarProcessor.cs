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
        readonly Dictionary<Transform, Matrix4x4> movedBones = new();
        readonly HashSet<Transform> skeletonBoneTransforms = new();
        readonly UnityObject assetRoot;
        readonly Animator animator;
        readonly Transform root;
        readonly Transform[] bones;
        readonly OverrideHumanDescription overrideHuman;
        Avatar avatar;

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
            OverrideHumanDescription? overrideHuman = null
        ) {
            var processor = new HumanoidAvatarProcessor(animator, bones, assetRoot, overrideHuman);
            processor.ScanAffectedComponents();
            if (normalize) processor.Normalize();
            processor.FixArmatureRoot();
            processor.UpdateBindposes();
            if (fixCrossLeg) processor.FixCrossLeg();
            processor.FixSiblings();
            return processor.RegenerateAvatar();
        }

        static OverrideHumanDescription ResolveOverrideHumanDescription(OverrideHumanDescription? overrideHuman, Avatar reference = null) {
            var desc = overrideHuman ?? new OverrideHumanDescription {
                humanLimits = new OverrideHumanLimits[(int)HumanBodyBones.LastBone],
                armStretch = 0.05F, upperArmTwist = 0.5F, lowerArmTwist = 0.5F,
                legStretch = 0.05F, lowerLegTwist = 0.5F, upperLegTwist = 0.5F, feetSpacing = 0.0F,
                hasTranslationDoF = false,
            };
            if (reference == null) return desc;
            var srcDesc = reference.humanDescription;
            switch (desc.mode) {
                case OverrideMode.Inherit:
                    desc.armStretch = srcDesc.armStretch;
                    desc.upperArmTwist = srcDesc.upperArmTwist;
                    desc.lowerArmTwist = srcDesc.lowerArmTwist;
                    desc.legStretch = srcDesc.legStretch;
                    desc.lowerLegTwist = srcDesc.lowerLegTwist;
                    desc.upperLegTwist = srcDesc.upperLegTwist;
                    desc.feetSpacing = srcDesc.feetSpacing;
                    desc.hasTranslationDoF = srcDesc.hasTranslationDoF;
                    break;
                case OverrideMode.Default:
                    desc.armStretch = 0.05F;
                    desc.upperArmTwist = 0.5F;
                    desc.lowerArmTwist = 0.5F;
                    desc.legStretch = 0.05F;
                    desc.lowerLegTwist = 0.5F;
                    desc.upperLegTwist = 0.5F;
                    desc.feetSpacing = 0.0F;
                    desc.hasTranslationDoF = false;
                    break;
            }
            if (desc.humanLimits == null)
                desc.humanLimits = new OverrideHumanLimits[(int)HumanBodyBones.LastBone];
            for (int i = 0; i < desc.humanLimits.Length; i++) {
                ref var limit = ref desc.humanLimits[i];
                if (limit.mode != OverrideMode.Inherit) continue;
                var humanName = HumanTrait.BoneName[i];
                bool matched = false;
                if (srcDesc.human != null)
                    foreach (var otherHuman in srcDesc.human)
                        if (otherHuman.humanName == humanName) {
                            limit = new OverrideHumanLimits {
                                mode = otherHuman.limit.useDefaultValues ?
                                    OverrideMode.Default :
                                    OverrideMode.Override,
                                min = otherHuman.limit.min,
                                max = otherHuman.limit.max,
                                center = otherHuman.limit.center,
                            };
                            matched = true;
                            break;
                        }
                if (!matched) limit.mode = OverrideMode.Default;
            }
            return desc;
        }

        HumanoidAvatarProcessor(Animator animator, Transform[] bones, UnityObject assetRoot, OverrideHumanDescription? overrideHuman) :
            this(animator.transform, bones, animator.avatar, assetRoot, overrideHuman) {
            this.animator = animator;
        }

        HumanoidAvatarProcessor(Transform root, Transform[] bones, Avatar avatar, UnityObject assetRoot, OverrideHumanDescription? overrideHuman) {
            this.root = root;
            this.avatar = avatar;
            this.assetRoot = assetRoot;
            this.overrideHuman = ResolveOverrideHumanDescription(overrideHuman, avatar);
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

        void UpdateBindposes() {
            foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var orgMesh = skinnedMeshRenderer.sharedMesh;
                if (orgMesh == null) continue;
                var bones = skinnedMeshRenderer.bones;
                if (bones == null || bones.Length == 0) continue;
                Matrix4x4[] bindposes = null;
                for (int i = 0; i < bones.Length; i++) {
                    var bone = bones[i];
                    if (bone != null && movedBones.TryGetValue(bone, out var deltaMatrix)) {
                        if (bindposes == null) bindposes = orgMesh.bindposes;
                        bindposes[i] = deltaMatrix * bindposes[i];
                    }
                }
                if (bindposes == null) continue;
                var clonedMesh = Instantiate(orgMesh);
                clonedMesh.name = $"{orgMesh.name} (Tweaked)";
                clonedMesh.bindposes = bindposes;
                if (assetRoot != null) AssetDatabase.AddObjectToAsset(clonedMesh, assetRoot);
                skinnedMeshRenderer.sharedMesh = clonedMesh;
            }
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
            var humanBoneNames = HumanTrait.BoneName;
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
            RestoreCachedPositions();
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