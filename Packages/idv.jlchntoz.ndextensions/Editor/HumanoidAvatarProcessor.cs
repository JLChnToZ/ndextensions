using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class HumanoidAvatarProcessor {
        static readonly Dictionary<HumanBodyBones, (HumanBodyBones childBone, PointingDirection direction)> remapChildBones = new() {
            [HumanBodyBones.LeftUpperLeg] = (HumanBodyBones.LeftLowerLeg, PointingDirection.Up),
            [HumanBodyBones.LeftLowerLeg] = (HumanBodyBones.LeftFoot, PointingDirection.Up),
            [HumanBodyBones.LeftFoot] = (HumanBodyBones.LeftToes, PointingDirection.Up),
            [HumanBodyBones.RightUpperLeg] = (HumanBodyBones.RightLowerLeg, PointingDirection.Up),
            [HumanBodyBones.RightLowerLeg] = (HumanBodyBones.RightFoot, PointingDirection.Up),
            [HumanBodyBones.RightFoot] = (HumanBodyBones.RightToes, PointingDirection.Up),
            [HumanBodyBones.LeftShoulder] = (HumanBodyBones.LeftUpperArm, PointingDirection.Left),
            [HumanBodyBones.LeftUpperArm] = (HumanBodyBones.LeftLowerArm, PointingDirection.Left),
            [HumanBodyBones.LeftLowerArm] = (HumanBodyBones.LeftHand, PointingDirection.Left),
            [HumanBodyBones.LeftHand] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.LeftThumbProximal] = (HumanBodyBones.LeftThumbIntermediate, PointingDirection.Left),
            [HumanBodyBones.LeftThumbIntermediate] = (HumanBodyBones.LeftThumbDistal, PointingDirection.Left),
            [HumanBodyBones.LeftThumbDistal] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.LeftIndexProximal] = (HumanBodyBones.LeftIndexIntermediate, PointingDirection.Left),
            [HumanBodyBones.LeftIndexIntermediate] = (HumanBodyBones.LeftIndexDistal, PointingDirection.Left),
            [HumanBodyBones.LeftIndexDistal] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.LeftMiddleProximal] = (HumanBodyBones.LeftMiddleIntermediate, PointingDirection.Left),
            [HumanBodyBones.LeftMiddleIntermediate] = (HumanBodyBones.LeftMiddleDistal, PointingDirection.Left),
            [HumanBodyBones.LeftMiddleDistal] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.LeftRingProximal] = (HumanBodyBones.LeftRingIntermediate, PointingDirection.Left),
            [HumanBodyBones.LeftRingIntermediate] = (HumanBodyBones.LeftRingDistal, PointingDirection.Left),
            [HumanBodyBones.LeftRingDistal] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.LeftLittleProximal] = (HumanBodyBones.LeftLittleIntermediate, PointingDirection.Left),
            [HumanBodyBones.LeftLittleIntermediate] = (HumanBodyBones.LeftLittleDistal, PointingDirection.Left),
            [HumanBodyBones.LeftLittleDistal] = (HumanBodyBones.LastBone, PointingDirection.Left),
            [HumanBodyBones.RightShoulder] = (HumanBodyBones.RightUpperArm, PointingDirection.Right),
            [HumanBodyBones.RightUpperArm] = (HumanBodyBones.RightLowerArm, PointingDirection.Right),
            [HumanBodyBones.RightLowerArm] = (HumanBodyBones.RightHand, PointingDirection.Right),
            [HumanBodyBones.RightHand] = (HumanBodyBones.LastBone, PointingDirection.Right),
            [HumanBodyBones.RightThumbProximal] = (HumanBodyBones.RightThumbIntermediate, PointingDirection.Right),
            [HumanBodyBones.RightThumbIntermediate] = (HumanBodyBones.RightThumbDistal, PointingDirection.Right),
            [HumanBodyBones.RightThumbDistal] = (HumanBodyBones.LastBone, PointingDirection.Right),
            [HumanBodyBones.RightIndexProximal] = (HumanBodyBones.RightIndexIntermediate, PointingDirection.Right),
            [HumanBodyBones.RightIndexIntermediate] = (HumanBodyBones.RightIndexDistal, PointingDirection.Right),
            [HumanBodyBones.RightIndexDistal] = (HumanBodyBones.LastBone, PointingDirection.Right),
            [HumanBodyBones.RightMiddleProximal] = (HumanBodyBones.RightMiddleIntermediate, PointingDirection.Right),
            [HumanBodyBones.RightMiddleIntermediate] = (HumanBodyBones.RightMiddleDistal, PointingDirection.Right),
            [HumanBodyBones.RightMiddleDistal] = (HumanBodyBones.LastBone, PointingDirection.Right),
            [HumanBodyBones.RightRingProximal] = (HumanBodyBones.RightRingIntermediate, PointingDirection.Right),
            [HumanBodyBones.RightRingIntermediate] = (HumanBodyBones.RightRingDistal, PointingDirection.Right),
            [HumanBodyBones.RightRingDistal] = (HumanBodyBones.LastBone, PointingDirection.Right),
        };
        readonly Dictionary<Transform, HumanBodyBones> boneToHumanBone = new();
        readonly Dictionary<Transform, Matrix4x4> movedBones = new();
        readonly Dictionary<Transform, TranslateRotate> cachedPositions = new();
        readonly HashSet<Transform> skeletonBoneTransforms = new();
        readonly UnityObject assetRoot;
        readonly Animator animator;
        readonly Transform root;
        readonly Transform[] bones;
        Avatar avatar;

        public static void Process(Animator animator, UnityObject assetRoot, bool normalize = false, bool fixCrossLeg = false, Transform[] bones = null) {
            var processor = new HumanoidAvatarProcessor(animator, assetRoot, bones);
            if (normalize) {
                processor.Normalize();
                processor.UpdateBindposes();
            }
            if (fixCrossLeg)
                processor.FixCrossLeg();
            processor.RegenerateAvatar();
            processor.ApplyAvatar();
        }

        HumanoidAvatarProcessor(Animator animator, UnityObject assetRoot, Transform[] bones) {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            this.assetRoot = assetRoot;
            this.animator = animator;
            avatar = animator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new ArgumentException("The avatar is not a vaild humanoid.", nameof(animator));
            root = animator.transform;
            if (bones == null || bones.Length != (int)HumanBodyBones.LastBone) {
                bones = new Transform[(int)HumanBodyBones.LastBone];
                for (HumanBodyBones i = 0; i < HumanBodyBones.LastBone; i++) {
                    var transform = animator.GetBoneTransform(i);
                    if (transform == null) continue;
                    bones[(int)i] = transform;
                }
            }
            for (HumanBodyBones i = 0; i < HumanBodyBones.LastBone; i++){
                var bone = bones[(int)i];
                if (bone != null) boneToHumanBone[bone] = i;
            }
            this.bones = bones;
        }

        #region Steps
        void Normalize() {
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                var transform = bones[(int)bone];
                if (transform == null) continue;
                bool canContainTwistBone = false;
                switch (bone) {
                    case HumanBodyBones.LeftUpperLeg: case HumanBodyBones.RightUpperLeg:
                    case HumanBodyBones.LeftLowerLeg: case HumanBodyBones.RightLowerLeg:
                    case HumanBodyBones.LeftUpperArm: case HumanBodyBones.RightUpperArm:
                    case HumanBodyBones.LeftLowerArm: case HumanBodyBones.RightLowerArm:
                        canContainTwistBone = true;
                        break;
                }
                Transform twistBone = null;
                for (int i = transform.childCount - 1; i >= 0; i--) {
                    var child = transform.GetChild(i);
                    if (canContainTwistBone && child.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) >= 0) {
                        twistBone = child;
                        continue;
                    }
                    CachePosition(child);
                }
                var orgMatrix = transform.localToWorldMatrix;
                var twistOrgMatrix = twistBone != null ? twistBone.localToWorldMatrix : Matrix4x4.identity;
                transform.rotation = GetAdjustedRotation(bone, transform);
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                if (twistBone != null) {
                    twistBone.localRotation = Quaternion.identity;
                    movedBones[twistBone] = twistBone.worldToLocalMatrix * twistOrgMatrix;
                }
                RestoreCachedPositions();
            }
            for (
                var transform = bones[(int)HumanBodyBones.Hips].parent;
                transform != null && transform != root;
                transform = transform.parent
            ) {
                for (int i = transform.childCount - 1; i >= 0; i--)
                    CachePosition(transform.GetChild(i));
                var orgMatrix = transform.localToWorldMatrix;
                transform.SetPositionAndRotation(root.position, root.rotation);
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                RestoreCachedPositions();
            }
        }

        void UpdateBindposes() {
            foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var orgMesh = skinnedMeshRenderer.sharedMesh;
                var bones = skinnedMeshRenderer.bones;
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
                clonedMesh.name = orgMesh.name;
                clonedMesh.bindposes = bindposes;
                AssetDatabase.AddObjectToAsset(clonedMesh, assetRoot);
                skinnedMeshRenderer.sharedMesh = clonedMesh;
            }
        }

        Quaternion GetAdjustedRotation(HumanBodyBones bone, Transform transform) {
            if (!remapChildBones.TryGetValue(bone, out var info) ||
                info.direction == PointingDirection.NoChange)
                return root.rotation;
            var refSrc = transform;
            var refDist = info.childBone < HumanBodyBones.LastBone ?
                bones[(int)info.childBone] :
                null;
            if (refDist == null) {
                refSrc = bones[HumanTrait.GetParentBone((int)bone)];
                refDist = transform;
            }
            var up = (refDist.position - refSrc.position).normalized;
            Vector3 forward;
            switch (info.direction) {
                case PointingDirection.Up: forward = -root.right; break;
                case PointingDirection.Left: forward = root.forward; break;
                case PointingDirection.Right: forward = -root.forward; break;
                case PointingDirection.Forward: forward = root.up; break;
                default: return root.rotation;
            }
            return Quaternion.LookRotation(Vector3.Cross(up, forward), up);
        }

        void FixCrossLeg() {
            FixCrossLegSide(
                bones[(int)HumanBodyBones.LeftUpperLeg],
                bones[(int)HumanBodyBones.LeftLowerLeg],
                bones[(int)HumanBodyBones.LeftFoot]
            );
            FixCrossLegSide(
                bones[(int)HumanBodyBones.RightUpperLeg],
                bones[(int)HumanBodyBones.RightLowerLeg],
                bones[(int)HumanBodyBones.RightFoot]
            );
        }

        void FixCrossLegSide(Transform thigh, Transform knee, Transform foot) {
            var vector = thigh.InverseTransformPoint(knee.position).normalized;
            if (Mathf.Abs(vector.x) < 0.001 && vector.z < 0) return; // Already fixed
            var footRotation = foot.rotation;
            var rotation =
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg - 90F, Vector3.forward) *
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.z) * Mathf.Rad2Deg - 90.05F, Vector3.right);
            thigh.localRotation = rotation * thigh.localRotation;
            knee.localRotation = Quaternion.Inverse(rotation) * knee.localRotation;
            foot.rotation = footRotation;
        }

        void RegenerateAvatar() {
            var boneNames = new HashSet<string>();
            var transformsToAdd = new List<(Transform, int)>();
            foreach (var bone in movedBones.Keys)
                for (var parent = bone; parent != null && parent != root; parent = parent.parent) {
                    if (!skeletonBoneTransforms.Add(parent)) break;
                    boneNames.Add(parent.name);
                }
            var ambiguousNameMap = new Dictionary<Transform, string>();
            var stack = new Stack<(int, Transform)>();
            stack.Push((0, root));
            while (stack.TryPop(out var entry)) {
                var (index, bone) = entry;
                if (index >= bone.childCount) continue;
                var child = bone.GetChild(index);
                stack.Push((index + 1, bone));
                stack.Push((0, child));
                if (skeletonBoneTransforms.Contains(child))
                    transformsToAdd.Add((child, stack.Count));
                else if (boneNames.Contains(child.name)) {
                    Debug.LogWarning($"[Humanoid Avatar Processor] Transform name \"{child.name}\" is ambiguous, this may prevents generated humanoid avatar working. You will have to fix it afterward.", child);
                    ambiguousNameMap[child] = child.name;
                    var temp = new string[boneNames.Count];
                    boneNames.CopyTo(temp);
                    var tempName = ObjectNames.GetUniqueName(temp, child.name);
                    boneNames.Add(tempName);
                    child.name = tempName;
                }
                CachePosition(child, true);
            }
            int i;
            var skeletonBones = new SkeletonBone[transformsToAdd.Count];
            var humanBones = new HumanBone[boneToHumanBone.Count];
            var humanBoneNames = HumanTrait.BoneName;
            transformsToAdd.Sort(CompareDepth);
            i = 0;
            foreach (var (bone, _) in transformsToAdd)
                skeletonBones[i++] = new SkeletonBone {
                    name = bone.name,
                    position = bone.localPosition,
                    rotation = bone.localRotation,
                    scale = bone.localScale,
                };
            i = 0;
            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++) {
                var boneTransform = bones[bone];
                if (boneTransform == null) continue;
                humanBones[i++] = new HumanBone {
                    boneName = boneTransform.name,
                    humanName = humanBoneNames[bone],
                    limit = new HumanLimit { useDefaultValues = true },
                };
            }
            var desc = avatar.humanDescription;
            desc = new HumanDescription {
                armStretch = desc.armStretch,
                upperArmTwist = desc.upperArmTwist,
                lowerArmTwist = desc.lowerArmTwist,
                legStretch = desc.legStretch,
                lowerLegTwist = desc.lowerLegTwist,
                upperLegTwist = desc.upperLegTwist,
                feetSpacing = desc.feetSpacing,
                hasTranslationDoF = desc.hasTranslationDoF,
                human = humanBones,
                skeleton = skeletonBones,
            };
            animator.avatar = null;
            RestoreCachedPositions();
            avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, desc);
            avatar.name = $"(Generated) {root.name} Avatar";
            foreach (var kv in ambiguousNameMap) kv.Key.name = kv.Value;
        }

        void ApplyAvatar() {
            foreach (var child in skeletonBoneTransforms) CachePosition(child, true);
            animator.avatar = avatar;
            AssetDatabase.AddObjectToAsset(avatar, assetRoot);
            RestoreCachedPositions();
        }
        #endregion

        #region Utility
        static int CompareDepth((Transform, int) a, (Transform, int) b) => a.Item2 - b.Item2;

        void CachePosition(Transform transform, bool isLocal = false) {
            cachedPositions[transform] = new TranslateRotate(transform, isLocal);
        }

        void RestoreCachedPositions() {
            foreach (var c in cachedPositions)
                c.Value.ApplyTo(c.Key);
            cachedPositions.Clear();
        }
        #endregion
    }

    struct TranslateRotate {
        public Vector3 position;
        public Quaternion rotation;
        public bool isLocal;

        public TranslateRotate(Transform transform, bool isLocal = false) {
            if (isLocal) {
                position = transform.localPosition;
                rotation = transform.localRotation;
            } else {
                position = transform.position;
                rotation = transform.rotation;
            }
            this.isLocal = isLocal;
        }

        public void ApplyTo(Transform transform) {
            if (isLocal) {
                #if UNITY_2021_3_OR_NEWER
                transform.SetLocalPositionAndRotation(position, rotation);
                #else
                transform.localPosition = position;
                transform.localRotation = rotation;
                #endif
            } else
                transform.SetPositionAndRotation(position, rotation);
        }
    }

    enum PointingDirection : byte {
        NoChange,
        Left,
        Right,
        Up,
        Forward,
    }
}