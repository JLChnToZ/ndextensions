using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;

#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    public partial class HumanoidAvatarProcessor {
        static readonly Queue<Transform> tempQueue = new();
        readonly Dictionary<Transform, List<(Component component, int refId)>> affectedComponents = new();
        readonly Dictionary<(Component component, int refId), TranslateRotate> cachedPositions = new();
        readonly HashSet<string> boneNames = new();
        readonly Dictionary<Transform, string> cachedRanamedBones = new();

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

        bool TryAvoidAmbiguousBoneName(Transform bone) {
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

        void RestoreAmbiguousBoneNames() {
            foreach (var kv in cachedRanamedBones)
                kv.Key.name = kv.Value;
            cachedRanamedBones.Clear();
            boneNames.Clear();
        }
    }
}