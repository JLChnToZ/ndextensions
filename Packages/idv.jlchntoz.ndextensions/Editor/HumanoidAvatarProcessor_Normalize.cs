using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    public partial class HumanoidAvatarProcessor {
        #region Normalize Bone Rotation
        void Normalize() {
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                var transform = bones[(int)bone];
                if (transform == null) continue;
                bool canContainTwistBone = false;
                switch (bone) {
                    case HumanBodyBones.LeftUpperLeg:
                    case HumanBodyBones.RightUpperLeg:
                    case HumanBodyBones.LeftLowerLeg:
                    case HumanBodyBones.RightLowerLeg:
                    case HumanBodyBones.LeftUpperArm:
                    case HumanBodyBones.RightUpperArm:
                    case HumanBodyBones.LeftLowerArm:
                    case HumanBodyBones.RightLowerArm:
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
        #endregion

        #region Fix Armature Root
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
        #endregion

        #region Fix Siblings
        void FixSiblings() {
            for (int i = (int)HumanBodyBones.LastBone - 1; i >= 0; i--) FixSibling(i);
            FixSibling((int)HumanBodyBones.Spine);
        }

        void FixSibling(int boneIndex) {
            if (bones[boneIndex] == null) return;
            bones[boneIndex].SetAsFirstSibling();
        }
        #endregion

        #region Fix Cross Leg
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
        #endregion
    }
}