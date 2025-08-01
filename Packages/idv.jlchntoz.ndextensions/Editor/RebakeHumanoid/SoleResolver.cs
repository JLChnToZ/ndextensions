using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    // Find the sole of the model for adjusting the avatar's height offset
    public sealed class SoleResolver {
        readonly IBoneTransformProvider root;
        readonly Queue<Transform> tempBones = new();
        readonly List<Vector3> tempVertices = new();
        readonly List<int> tempIndices = new();
        readonly HashSet<Transform> boneOfInterest = new();
        readonly HashSet<int> tempProcessedVertices = new();
        readonly ConditionalWeakTable<Mesh, Dictionary<int, BlendshapeData>> blendshapeCache = new();
        float minOffset = float.PositiveInfinity;

        public static float FindOffset(IBoneTransformProvider provider, GameObject root, SkinnedMeshRenderer[] skins = null) {
            var resolver = new SoleResolver(provider);
            resolver.AddBoneOfInterest(HumanBodyBones.LeftToes);
            resolver.AddBoneOfInterest(HumanBodyBones.RightToes);
            resolver.AddBoneOfInterest(HumanBodyBones.LeftFoot);
            resolver.AddBoneOfInterest(HumanBodyBones.RightFoot);
            if (resolver.boneOfInterest.Count > 0)
                foreach (var renderer in (skins as IEnumerable<SkinnedMeshRenderer>) ?? root.GetBuildableComponentsInChildren<SkinnedMeshRenderer>())
                    resolver.AddRendererOfInterest(renderer);
            return resolver.minOffset;
        }

        SoleResolver(IBoneTransformProvider root) => this.root = root;

        void AddBoneOfInterest(HumanBodyBones bone) {
            var boneTransform = root.GetBoneTransform(bone);
            if (boneTransform == null) return;
            try {
                tempBones.Enqueue(boneTransform);
                while (tempBones.TryDequeue(out var current)) {
                    if (!boneOfInterest.Add(current)) continue; // Already visited
                    foreach (Transform child in current) tempBones.Enqueue(child);
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
                foreach (var bone in boneOfInterest)
                    for (int i = 0; i < bones.Length && (i = Array.IndexOf(bones, bone, i)) >= 0; i++)
                        tempIndices.Add(i);
                if (tempIndices.Count == 0) return;
                var boneMatrices = new Matrix4x4[tempIndices.Count];
                var bindposes = mesh.bindposes;
                for (int i = 0; i < boneMatrices.Length; i++)
                    boneMatrices[i] = bones[tempIndices[i]].localToWorldMatrix * bindposes[tempIndices[i]];
                mesh.GetVertices(tempVertices);
                if (!blendshapeCache.TryGetValue(mesh, out var blendshapeDatas))
                    blendshapeCache.Add(mesh, blendshapeDatas = new());
                var blendshapeWeights = new float[mesh.blendShapeCount];
                for (int i = 0; i < blendshapeWeights.Length; i++) {
                    blendshapeWeights[i] = renderer.GetBlendShapeWeight(i);
                    if (blendshapeWeights[i] > 0 && !blendshapeDatas.ContainsKey(i))
                        blendshapeDatas.Add(i, new(mesh, i));
                }
                var bonesPerVertex = mesh.GetBonesPerVertex();
                var boneWeights = mesh.GetAllBoneWeights();
                for (int i = 0, j = 0; i < bonesPerVertex.Length; i++)
                    for (int k = 0; k < bonesPerVertex[i]; k++, j++) {
                        if (boneWeights[j].weight <= 0) continue;
                        int bi = tempIndices.IndexOf(boneWeights[j].boneIndex);
                        if (bi < 0) continue;
                        if (tempProcessedVertices.Add(i))
                            foreach (var kvp in blendshapeDatas)
                                tempVertices[i] += kvp.Value.GetVertex(blendshapeWeights[kvp.Key], i);
                        minOffset = Mathf.Min(minOffset, boneMatrices[bi].MultiplyPoint3x4(tempVertices[i]).y);
                    }
            } finally {
                tempIndices.Clear();
                tempVertices.Clear();
                tempProcessedVertices.Clear();
            }
        }

        readonly struct BlendshapeData {
            readonly object vertices;

            public BlendshapeData(Mesh mesh, int index) {
                int count = mesh.GetBlendShapeFrameCount(index);
                if (count == 1) {
                    var v = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(index, 0, v, null, null);
                    vertices = Tuple.Create(mesh.GetBlendShapeFrameWeight(index, 0), v);
                } else if (count > 1) {
                    var vertices = new SortedDictionary<float, Vector3[]>();
                    for (int i = 0; i < count; i++) {
                        var v = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(index, i, v, null, null);
                        if (v == null || v.Length == 0) continue;
                        var weight = mesh.GetBlendShapeFrameWeight(index, i);
                        if (weight <= 0) continue;
                        vertices.Add(weight, v);
                    }
                    this.vertices = vertices;
                } else {
                    vertices = null;
                }
            }

            public readonly Vector3 GetVertex(float weight, int index) {
                if (weight > 0) {
                    if (vertices is Tuple<float, Vector3[]> v1) {
                        if (index < 0 || index >= v1.Item2.Length) return Vector3.zero;
                        return v1.Item2[index] * Mathf.InverseLerp(0, v1.Item1, weight);
                    }
                    if (vertices is SortedDictionary<float, Vector3[]> vMany) {
                        KeyValuePair<float, Vector3[]>? last = null;
                        foreach (var kvp in vMany) {
                            if (kvp.Key >= weight) {
                                if (last.HasValue) {
                                    var lastKvp = last.Value;
                                    return Vector3.Lerp(lastKvp.Value[index], kvp.Value[index], Mathf.InverseLerp(lastKvp.Key, kvp.Key, weight));
                                }
                                return kvp.Value[index] * Mathf.InverseLerp(0, kvp.Key, weight);
                            }
                            last = kvp;
                        }
                        if (last.HasValue) return last.Value.Value[index];
                    }
                }
                return Vector3.zero;
            }
        }
    }
}