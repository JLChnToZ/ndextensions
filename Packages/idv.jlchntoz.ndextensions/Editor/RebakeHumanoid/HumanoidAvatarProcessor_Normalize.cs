using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    public partial class HumanoidAvatarProcessor {
        #region Normalize Bone Rotation
        void Normalize(bool fixPose = false) {
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                Normalize(bone);
                if (fixPose) FixPose(bone);
            }
        }

        void NormalizeLeg() {
            // Minimal normalization before fixing cross-legs
            for (var bone = HumanBodyBones.LeftUpperLeg; bone <= HumanBodyBones.RightFoot; bone++)
                Normalize(bone);
        }

        void Normalize(HumanBodyBones bone) {
            var transform = bones[(int)bone];
            if (transform == null) return;
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
            RecordMovedBone(transform, orgMatrix);
            if (twistBone != null) {
                CacheAffectedComponents(twistBone);
                twistBone.localRotation = Quaternion.identity;
                RecordMovedBone(twistBone, twistOrgMatrix);
            }
            RestoreCachedPositions();
        }

        void FixPose() {
            for (var bone = HumanBodyBones.LeftUpperLeg; bone <= HumanBodyBones.RightLowerArm; bone++)
                FixPose(bone);
        }

        void FixPose(HumanBodyBones bone) {
            if (bone < HumanBodyBones.LeftUpperLeg || bone > HumanBodyBones.RightLowerArm) return;
            var transform = bones[(int)bone];
            if (transform == null) return;
            Vector3 orgV = Vector3.zero, newV = Vector3.zero;
            int childCount = 0;
            for (int b = (int)bone + 1; b <= (int)HumanBodyBones.RightLowerArm; b++) {
                if (HumanTrait.GetParentBone(b) != (int)bone) continue;
                var child = bones[b];
                if (child == null) continue;
                orgV += (child.position - transform.position).normalized;
                childCount++;
            }
            if (childCount > 0) {
                orgV /= childCount;
                int longestAxis = 0;
                float sign = Mathf.Sign(orgV.x);
                for (int i = 1; i < 3; i++)
                    if (Mathf.Abs(orgV[i]) > Mathf.Abs(orgV[longestAxis])) {
                        longestAxis = i;
                        sign = Mathf.Sign(orgV[i]);
                    }
                newV[longestAxis] = sign;
                transform.rotation = Quaternion.FromToRotation(orgV, newV) * transform.rotation;
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
                RecordMovedBone(transform, orgMatrix);
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