using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using nadena.dev.ndmf.animator;

using static UnityEngine.Object;

#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    public partial class HumanoidAvatarProcessor {
        static readonly Queue<Transform> tempQueue = new();
        readonly Dictionary<Transform, List<(Component component, int refId)>> affectedComponents = new();
        readonly Dictionary<(Component component, int refId), TranslateRotate> cachedPositions = new();
        readonly Dictionary<Transform, Matrix4x4> movedBones = new();
        readonly HashSet<string> boneNames = new();
        readonly Dictionary<Transform, string> cachedRanamedBones = new();
        public AnimationIndex animationIndex;

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
            if (target == null) return;
            if (!affectedComponents.TryGetValue(target, out var list))
                affectedComponents[target] = list = new();
            list.Add((component, refId));
        }

        void CachePosition(Component component, bool isLocal = false, int refId = -1) =>
            cachedPositions[(component, refId)] = new(component, isLocal, refId);

        void CacheAffectedComponents(Transform transform) {
            if (transform == null) return;
            if (affectedComponents.TryGetValue(transform, out var components))
                foreach (var (component, refId) in components)
                    CachePosition(component, refId: refId);
        }

        void RestoreCachedPositions(bool restoreAnimation = true) {
            foreach (var c in cachedPositions)
                c.Value.ApplyTo(c.Key.component, c.Key.refId, restoreAnimation ? animationIndex : null, root);
            cachedPositions.Clear();
        }

        bool TryAvoidAmbiguousBoneName(Transform bone) {
            if (cachedRanamedBones.ContainsKey(bone)) return true;
            var boneName = bone.name;
            if (boneNames.Add(boneName)) return false;
            cachedRanamedBones[bone] = boneName;
            do {
                boneName = Guid.NewGuid().ToString();
            } while (!boneNames.Add(boneName));
            bone.name = boneName;
            return true;
        }

        void RestoreAmbiguousBoneNames() {
            foreach (var kv in cachedRanamedBones)
                kv.Key.name = kv.Value;
            cachedRanamedBones.Clear();
            boneNames.Clear();
        }

        void RecordMovedBone(Transform bone, Matrix4x4 orgL2W) {
            var deltaMatrix = bone.worldToLocalMatrix * orgL2W;
            if (movedBones.TryGetValue(bone, out var matrix))
                movedBones[bone] = matrix * deltaMatrix;
            else
                movedBones.Add(bone, deltaMatrix);
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
    }
}