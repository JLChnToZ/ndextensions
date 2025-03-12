using System;
using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    // Find the sole of the model for adjusting the avatar's height offset
    public sealed class SoleResolver {
        readonly Animator animator;
        readonly Stack<Transform> tempBones = new();
        readonly List<Vector3> tempVertices = new();
        readonly List<int> tempIndices = new();
        readonly HashSet<Transform> boneOfInterest = new();
        float minOffset = float.PositiveInfinity;

        public static float FindOffset(Animator animator) {
            var resolver = new SoleResolver(animator);
            resolver.AddBoneOfInterest(HumanBodyBones.LeftToes);
            resolver.AddBoneOfInterest(HumanBodyBones.RightToes);
            resolver.AddBoneOfInterest(HumanBodyBones.LeftFoot);
            resolver.AddBoneOfInterest(HumanBodyBones.RightFoot);
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                resolver.AddRendererOfInterest(renderer);
            return resolver.minOffset;
        }

        SoleResolver(Animator root) => animator = root;

        void AddBoneOfInterest(HumanBodyBones bone) {
            var boneTransform = animator.GetBoneTransform(bone);
            if (boneTransform == null) return;
            try {
                tempBones.Push(boneTransform);
                while (tempBones.TryPop(out var current)) {
                    if (!boneOfInterest.Add(current)) continue; // Already visited
                    foreach (Transform child in current) tempBones.Push(child);
                }
            } finally {
                tempBones.Clear();
            }
        }

        void AddRendererOfInterest(SkinnedMeshRenderer renderer) {
            if (renderer == null) return;
            var mesh = renderer.sharedMesh;
            if (mesh == null) return;
            var bones = renderer.bones;
            if (bones == null || bones.Length == 0) return;
            try {
                foreach (var bone in boneOfInterest) {
                    int bi = 0;
                    while ((bi = Array.IndexOf(bones, bone, bi)) >= 0) {
                        tempIndices.Add(bi);
                        bi++;
                    }
                }
                if (tempIndices.Count == 0) return;
                var boneMatrices = new Matrix4x4[tempIndices.Count];
                var bindposes = mesh.bindposes;
                for (int i = 0; i < boneMatrices.Length; i++)
                    boneMatrices[i] = bones[tempIndices[i]].localToWorldMatrix * bindposes[tempIndices[i]];
                mesh.GetVertices(tempVertices);
                var bonesPerVertex = mesh.GetBonesPerVertex();
                var boneWeights = mesh.GetAllBoneWeights();
                for (int i = 0, j = 0; i < bonesPerVertex.Length; i++)
                    for (int k = 0; k < bonesPerVertex[i]; k++, j++) {
                        if (boneWeights[j].weight <= 0) continue;
                        int bi = tempIndices.IndexOf(boneWeights[j].boneIndex);
                        if (bi < 0) continue;
                        minOffset = Mathf.Min(minOffset, boneMatrices[bi].MultiplyPoint3x4(tempVertices[i]).y);
                    }
            } finally {
                tempIndices.Clear();
                tempVertices.Clear();
            }
        }
    }
}