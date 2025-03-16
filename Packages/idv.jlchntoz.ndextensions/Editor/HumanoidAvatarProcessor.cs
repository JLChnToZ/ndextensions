using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class HumanoidAvatarProcessor {
        static readonly int[] remapChildBones;
        static readonly Queue<Transform> tempQueue = new();
        readonly Dictionary<Transform, HumanBodyBones> boneToHumanBone = new();
        readonly Dictionary<Transform, Matrix4x4> movedBones = new();
        readonly Dictionary<(Component component, int refId), TranslateRotate> cachedPositions = new();
        readonly HashSet<Transform> skeletonBoneTransforms = new();
        readonly HashSet<string> boneNames = new();
        readonly Dictionary<Transform, string> cachedRanamedBones = new();
        readonly Dictionary<Transform, List<(Component component, int refId)>> affectedComponents = new();
        readonly UnityObject assetRoot;
        readonly Animator animator;
        readonly Transform root;
        readonly Transform[] bones;
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

        public static void Process(Animator animator, UnityObject assetRoot, bool normalize = false, bool fixCrossLeg = false, Transform[] bones = null) {
            var processor = new HumanoidAvatarProcessor(animator, bones, assetRoot);
            processor.ScanAffectedComponents();
            if (normalize) processor.Normalize();
            processor.FixArmatureRoot();
            processor.UpdateBindposes();
            if (fixCrossLeg) processor.FixCrossLeg();
            processor.FixSiblings();
            processor.RegenerateAvatar();
            processor.ApplyAvatar();
        }

        HumanoidAvatarProcessor(Animator animator, Transform[] bones, UnityObject assetRoot) :
            this(animator.transform, bones, animator.avatar, assetRoot) {
            this.animator = animator;
        }

        HumanoidAvatarProcessor(Transform root, Transform[] bones, Avatar avatar, UnityObject assetRoot) {
            this.root = root;
            this.avatar = avatar;
            this.assetRoot = assetRoot;
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

        #region Steps
        void ScanAffectedComponents() {
            var tempComponents = new List<Component>();
            tempQueue.Enqueue(root);
            while (tempQueue.TryDequeue(out var transform)) {
                foreach (Transform child in transform) tempQueue.Enqueue(child);
                transform.GetComponents(tempComponents);
                foreach (var c in tempComponents) {
                    if (c is Collider collider) {
                        AddAffectedComponent(collider.transform, collider);
                        continue;
                    }
                    if (c is IConstraint constraint) {
                        for (int i = 0, count = constraint.sourceCount; i < count; i++)
                            AddAffectedComponent(constraint.GetSource(i).sourceTransform, constraint as Component, i);
                        continue;
                    }
                    #if VRC_SDK_VRCSDK3
                    if (c is VRCPhysBoneColliderBase pbc) {
                        AddAffectedComponent(pbc.GetRootTransform(), pbc);
                        continue;
                    }
                    if (c is ContactBase contact) {
                        AddAffectedComponent(contact.GetRootTransform(), contact);
                        continue;
                    }
                    if (c is VRCConstraintBase vc) {
                        for (int i = 0; i < vc.Sources.Count; i++)
                            AddAffectedComponent(vc.Sources[i].SourceTransform, vc, i);
                        continue;
                    }
                    #endif
                }
            }
            tempQueue.Clear();
        }

        void AddAffectedComponent(Transform target, Component component, int refId = -1) {
            if (!affectedComponents.TryGetValue(target, out var list))
                affectedComponents[target] = list = new();
            list.Add((component, refId));
        }

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
                foreach (Transform child in transform) {
                    if (canContainTwistBone && child.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) >= 0) {
                        twistBone = child;
                        continue;
                    }
                    CachePosition(child);
                }
                CacheAffectedComponents(transform);
                var orgMatrix = transform.localToWorldMatrix;
                var twistOrgMatrix = twistBone != null ? twistBone.localToWorldMatrix : Matrix4x4.identity;
                transform.rotation = GetAdjustedRotation((int)bone, transform);
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                if (twistBone != null) {
                    CacheAffectedComponents(twistBone);
                    twistBone.localRotation = Quaternion.identity;
                    movedBones[twistBone] = twistBone.worldToLocalMatrix * twistOrgMatrix;
                }
                RestoreCachedPositions();
            }
        }

        void FixArmatureRoot() {
            for (
                var transform = bones[(int)HumanBodyBones.Hips].parent;
                transform != null && transform != root;
                transform = transform.parent
            ) {
                foreach (Transform child in transform) CachePosition(child);
                CacheAffectedComponents(transform);
                var orgMatrix = transform.localToWorldMatrix;
                transform.SetPositionAndRotation(root.position, root.rotation);
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                RestoreCachedPositions();
            }
        }

        void FixSiblings() {
            for (int i = (int)HumanBodyBones.LastBone - 1; i >= 0; i--) FixSibling(i);
            FixSibling((int)HumanBodyBones.Spine);
        }

        void FixSibling(int boneIndex) {
            if (bones[boneIndex] == null) return;
            bones[boneIndex].SetAsFirstSibling();
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

        Quaternion GetAdjustedRotation(int bone, Transform transform) {
            switch ((HumanBodyBones)bone) {
                case HumanBodyBones.Hips:
                case HumanBodyBones.LeftEye:
                case HumanBodyBones.RightEye:
                    return root.rotation;
            }
            int childBone = remapChildBones[bone];
            bool hasUp = false;
            Vector3 up = default;
            if (childBone >= 0 && childBone < (int)HumanBodyBones.LastBone) {
                var refDist = bones[childBone];
                if (refDist != null) {
                    up = Vector3.Normalize(refDist.position - transform.position);
                    hasUp = true;
                }
            }
            while (!hasUp && (bone = HumanTrait.GetParentBone(bone)) >= 0) {
                var refSrc = bones[bone];
                if (refSrc != null) {
                    up = Vector3.Normalize(transform.position - refSrc.position);
                    hasUp = true;
                }
            }
            return hasUp ? Quaternion.LookRotation(-Vector3.Cross(Vector3.Cross(up, root.up), up), up) : root.rotation;
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
            CacheAffectedComponents(thigh);
            CacheAffectedComponents(knee);
            CacheAffectedComponents(foot);
            var vector = thigh.InverseTransformPoint(knee.position).normalized;
            if (Mathf.Abs(vector.x) < 0.001 && vector.z < 0) return; // Already fixed
            var footRotation = foot.rotation;
            var rotation =
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg - 90F, Vector3.forward) *
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.z) * Mathf.Rad2Deg - 90.05F, Vector3.right);
            thigh.localRotation = rotation * thigh.localRotation;
            knee.localRotation = Quaternion.Inverse(rotation) * knee.localRotation;
            foot.rotation = footRotation;
            RestoreCachedPositions();
        }

        void RegenerateAvatar() {
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
                    FixAmbiguousBone(child);
                CachePosition(child, true);
            }
            var transformsToAdd = new Transform[skeletonBoneTransforms.Count];
            skeletonBoneTransforms.CopyTo(transformsToAdd);
            Array.Sort(transformsToAdd, new HierarchyComparer(true));
            var skeletonBones = Array.ConvertAll(transformsToAdd, ToSkeletonBone);
            var humanBones = new HumanBone[boneToHumanBone.Count];
            var humanBoneNames = HumanTrait.BoneName;
            for (int i = 0, bone = 0; bone < (int)HumanBodyBones.LastBone; bone++) {
                var boneTransform = bones[bone];
                if (boneTransform == null) continue;
                humanBones[i++] = new HumanBone {
                    boneName = boneTransform.name,
                    humanName = humanBoneNames[bone],
                    limit = new HumanLimit { useDefaultValues = true },
                };
            }
            HumanDescription desc;
            if (avatar != null) {
                desc = avatar.humanDescription;
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
            } else
                desc = new HumanDescription {
                    armStretch = 0.05F,
                    upperArmTwist = 0.5F,
                    lowerArmTwist = 0.5F,
                    legStretch = 0.05F,
                    lowerLegTwist = 0.5F,
                    upperLegTwist = 0.5F,
                    feetSpacing = 0.0F,
                    hasTranslationDoF = false,
                    human = humanBones,
                    skeleton = skeletonBones,
                };
            if (animator != null) animator.avatar = null;
            RestoreCachedPositions();
            avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, desc);
            if (avatar.isValid)
                avatar.name = $"{root.name} Avatar (Generated)";
            else {
                DestroyImmediate(avatar);
                avatar = null;
            }
            RestoreCachedBoneNames();
        }

        SkeletonBone ToSkeletonBone(Transform bone) => new() {
            name = bone.name,
            position = bone.localPosition,
            rotation = bone.localRotation,
            scale = bone.localScale,
        };

        void ApplyAvatar() {
            if (avatar == null) return;
            if (animator != null) animator.avatar = avatar;
            if (assetRoot != null) AssetDatabase.AddObjectToAsset(avatar, assetRoot);
        }
        #endregion

        #region Utility
        void CachePosition(Component component, bool isLocal = false, int refId = -1) =>
            cachedPositions[(component, refId)] = new(component, isLocal, refId);

        void CacheAffectedComponents(Transform transform) {
            if (transform == null) return;
            if (affectedComponents.TryGetValue(transform, out var components))
                foreach (var (component, refId) in components)
                    CachePosition(component, refId: refId);
        }

        void RestoreCachedPositions() {
            foreach (var c in cachedPositions)
                c.Value.ApplyTo(c.Key.component, c.Key.refId);
            cachedPositions.Clear();
        }

        bool FixAmbiguousBone(Transform bone) {
            if (cachedRanamedBones.ContainsKey(bone)) return true;
            var boneName = bone.name;
            if (boneNames.Add(boneName)) return false;
            cachedRanamedBones[bone] = boneName;
            var temp = new string[boneNames.Count];
            boneNames.CopyTo(temp);
            boneName = ObjectNames.GetUniqueName(temp, boneName);
            boneNames.Add(boneName);
            bone.name = boneName;
            return true;
        }

        void RestoreCachedBoneNames() {
            foreach (var kv in cachedRanamedBones)
                kv.Key.name = kv.Value;
            cachedRanamedBones.Clear();
            boneNames.Clear();
        }
        #endregion

        struct RemapBoneData {
            public int childBone;
        }
    }

    readonly struct TranslateRotate {
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly bool isLocal;

        public TranslateRotate(Component component, bool isLocal = false, int refId = -1) {
            if (component is Transform transform) {
                if (isLocal) {
                    #if UNITY_2021_3_OR_NEWER
                    transform.GetLocalPositionAndRotation(out position, out rotation);
                    #else
                    position = transform.localPosition;
                    rotation = transform.localRotation;
                    #endif
                } else {
                    #if UNITY_2021_3_OR_NEWER
                    transform.GetPositionAndRotation(out position, out rotation);
                    #else
                    position = transform.position;
                    rotation = transform.rotation;
                    #endif
                }
            } else if (component is BoxCollider bc) {
                if (isLocal) {
                    position = bc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = bc.transform.TransformPoint(bc.center);
                    rotation = bc.transform.rotation;
                }
            } else if (component is SphereCollider sc) {
                if (isLocal) {
                    position = sc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = sc.transform.TransformPoint(sc.center);
                    rotation = sc.transform.rotation;
                }
            } else if (component is CapsuleCollider cc) {
                if (isLocal) {
                    position = cc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = cc.transform.TransformPoint(cc.center);
                    rotation = cc.transform.rotation;
                }
            }
            #if VRC_SDK_VRCSDK3
            else if (component is ContactBase contact) {
                if (isLocal) {
                    position = contact.position;
                    rotation = contact.rotation;
                } else {
                    transform = contact.GetRootTransform();
                    position = transform.TransformPoint(contact.position);
                    rotation = transform.rotation * contact.rotation;
                }
            } else if (component is VRCPhysBoneColliderBase pbc) {
                if (isLocal) {
                    position = pbc.position;
                    rotation = pbc.rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    position = transform.TransformPoint(pbc.position);
                    rotation = transform.rotation * pbc.rotation;
                }
            } else if (component is VRCConstraintBase vrcConstraint) {
                var source = vrcConstraint.Sources[refId];
                if (isLocal) {
                    position = source.ParentPositionOffset;
                    rotation = Quaternion.identity;
                } else {
                    transform = source.SourceTransform;
                    position = transform.TransformPoint(source.ParentPositionOffset);
                    rotation = transform.rotation;
                }
            }
            #endif
            else {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
            this.isLocal = isLocal;
        }

        public void ApplyTo(Component component, int refId = -1) {
            if (component is Transform transform) {
                if (isLocal) {
                    #if UNITY_2021_3_OR_NEWER
                    transform.SetLocalPositionAndRotation(position, rotation);
                    #else
                    transform.localPosition = position;
                    transform.localRotation = rotation;
                    #endif
                } else
                    transform.SetPositionAndRotation(position, rotation);
                return;
            }
            if (component is BoxCollider bc) {
                if (isLocal) bc.center = position;
                else bc.center = bc.transform.InverseTransformPoint(position);
                return;
            }
            if (component is SphereCollider sc) {
                if (isLocal) sc.center = position;
                else sc.center = sc.transform.InverseTransformPoint(position);
                return;
            }
            if (component is CapsuleCollider cc) {
                if (isLocal) cc.center = position;
                else cc.center = cc.transform.InverseTransformPoint(position);
                return;
            }
            #if VRC_SDK_VRCSDK3
            if (component is ContactBase contact) {
                if (isLocal) {
                    contact.position = position;
                    contact.rotation = rotation;
                } else {
                    transform = contact.GetRootTransform();
                    contact.position = transform.InverseTransformPoint(position);
                    contact.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                }
                return;
            }
            if (component is VRCPhysBoneColliderBase pbc) {
                if (isLocal) {
                    pbc.position = position;
                    pbc.rotation = rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    pbc.position = transform.InverseTransformPoint(position);
                    pbc.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                }
                return;
            }
            if (component is VRCConstraintBase vrcConstraint) {
                var src = vrcConstraint.Sources[refId];
                if (isLocal)
                    src.ParentPositionOffset = position;
                else {
                    transform = src.SourceTransform;
                    src.ParentPositionOffset = transform.InverseTransformPoint(position);
                    src.ParentRotationOffset += (Quaternion.Inverse(transform.rotation) * rotation).eulerAngles;
                }
                vrcConstraint.Sources[refId] = src;
                return;
            }
            #endif
        }
    }
}