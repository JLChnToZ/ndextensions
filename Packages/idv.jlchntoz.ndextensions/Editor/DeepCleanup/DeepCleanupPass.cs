using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.runtime;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class DeepCleanupPass : Pass<DeepCleanupPass> {
        static readonly List<Component> tempComponents = new();

        public override string DisplayName => "Deep Cleanup";

        protected override void Execute(BuildContext context) {
            if (!context.AvatarRootObject.TryGetComponent(out DeepCleanup m)) return;
            var state = GatherObjects(context.AvatarRootTransform);
            try {
                AssetDatabase.StartAssetEditing();
                RemoveObjectsFromContainer(state);
                CloneObjects(context.AssetContainer, state);
            } finally {
                AssetDatabase.StopAssetEditing();
            }
            UnityObject.DestroyImmediate(m);
        }

        static State GatherObjects(Transform root) {
            var objQueue = WalkHierarchy(root);
            var state = new State {
                allObjects = new(),
                parents = new(),
                clonableObjects = new(),
                scannedPaths = new(),
            };
            while (objQueue.TryDequeue(out var pair)) {
                if (state.clonableObjects.Contains(pair.target)) state.parents.Add(pair.parent);
                if (!state.allObjects.Add(pair.target)) continue;
                var assetPath = AssetDatabase.GetAssetPath(pair.target);
                if (!string.IsNullOrEmpty(assetPath)) {
                    if (!state.scannedPaths.TryGetValue(assetPath, out bool isCloned))
                        state.scannedPaths.Add(assetPath, isCloned = AssetDatabase.LoadMainAssetAtPath(assetPath) is SubAssetContainer);
                    if (!isCloned && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && AssetDatabase.IsNativeAsset(pair.target)) {
                        state.parents.Add(pair.parent);
                        state.clonableObjects.Add(pair.target);
                    }
                }
                using var so = new SerializedObject(pair.target);
                for (var prop = so.GetIterator(); prop.Next(true); ) {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = prop.objectReferenceValue;
                    if (value == null || value is GameObject || value is Component) continue;
                    objQueue.Enqueue((pair.target, value));
                }
            }
            return state;
        }

        static Queue<(UnityObject parent, UnityObject target)> WalkHierarchy(Transform root) {
            var componentQueue = new Queue<(UnityObject, UnityObject)>();
            var transformQueue = new Queue<Transform>();
            transformQueue.Enqueue(root);
            while (transformQueue.TryDequeue(out var transform)) {
                foreach (Transform child in transform) transformQueue.Enqueue(child);
                transform.GetComponents(tempComponents);
                foreach (var component in tempComponents) {
                    if (component is Transform) continue;
                    componentQueue.Enqueue((transform, component));
                }
            }
            return componentQueue;
        }

        static int RemoveObjectsFromContainer(in State state) {
            int removeCount = 0;
            foreach (var kv in state.scannedPaths) {
                if (!kv.Value) continue;
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(kv.Key)) {
                    if (obj == null || AssetDatabase.IsMainAsset(obj) || state.allObjects.Contains(obj)) continue;
                    AssetDatabase.RemoveObjectFromAsset(obj);
                    removeCount++;
                }
            }
            return removeCount;
        }

        static Dictionary<UnityObject, UnityObject> CloneObjects(in State state) {
            var cloneMap = new Dictionary<UnityObject, UnityObject>();
            foreach (var obj in state.clonableObjects) {
                var clone = UnityObject.Instantiate(obj);
                clone.name = obj.name;
                cloneMap[obj] = clone;
            }
            return cloneMap;
        }

        static int CloneObjects(UnityObject currentContainer, in State state) {
            int cloneCount = 0;
            var objQueue = new Queue<UnityObject>(state.parents);
            state.allObjects.Clear();
            var cloneMap = CloneObjects(state);
            while (objQueue.TryDequeue(out var obj)) {
                if (!state.allObjects.Add(obj)) continue;
                using var so = new SerializedObject(obj);
                bool modified = false;
                for (var prop = so.GetIterator(); prop.Next(true); ) {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = prop.objectReferenceValue;
                    if (value == null || !cloneMap.TryGetValue(value, out var clone)) continue;
                    if (state.clonableObjects.Remove(value)) {
                        objQueue.Enqueue(clone);
                        AssetDatabase.AddObjectToAsset(clone, currentContainer);
                        cloneCount++;
                    }
                    prop.objectReferenceValue = value = clone;
                    modified = true;
                }
                if (modified) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return cloneCount;
        }

        struct State {
            public HashSet<UnityObject> allObjects;
            public HashSet<UnityObject> parents;
            public HashSet<UnityObject> clonableObjects;
            public Dictionary<string, bool> scannedPaths;
        }
    }
}