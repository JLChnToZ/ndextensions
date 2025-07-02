using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using nadena.dev.ndmf;
using nadena.dev.ndmf.runtime;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class DeepCleanupPass : Pass<DeepCleanupPass> {
        readonly List<Component> tempComponents = new();
        readonly Queue<Transform> tempTransforms = new();
        readonly Queue<UnityObject> tempQueue = new();
        readonly Dictionary<UnityObject, UnityObject> tempClones = new();

        public override string DisplayName => "Deep Cleanup";

        protected override void Execute(BuildContext context) {
            if (!context.AvatarRootObject.TryGetComponent(out DeepCleanup m)) return;
            var state = GatherObjects(context.AvatarRootTransform);
            try {
                AssetDatabase.StartAssetEditing();
                RemoveObjectsFromContainer(in state);
                CloneObjects(context.AssetContainer, in state);
            } finally {
                AssetDatabase.StopAssetEditing();
            }
            UnityObject.DestroyImmediate(m);
        }

        State GatherObjects(Transform root) {
            var state = new State {
                allObjects = new(),
                parents = new(),
                clonableObjects = new(),
                scannedPaths = new(),
            };
            var dependencies = new Dictionary<UnityObject, HashSet<UnityObject>>();
            tempTransforms.Enqueue(root);
            while (tempTransforms.TryDequeue(out var transform)) {
                foreach (Transform child in transform) tempTransforms.Enqueue(child);
                transform.GetComponents(tempComponents);
                foreach (var component in tempComponents) {
                    if (component is Transform) continue;
                    tempQueue.Enqueue(component);
                }
            }
            while (tempQueue.TryDequeue(out var target)) {
                if (!state.allObjects.Add(target)) continue;
                ValidateCloneable(target, in state, false);
                using var so = new SerializedObject(target);
                for (var prop = so.GetIterator(); prop.Next(true); ) {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = prop.objectReferenceValue;
                    if (value == null || value == target) continue;
                    if (!dependencies.TryGetValue(value, out var depd))
                        dependencies[value] = depd = new();
                    depd.Add(target);
                    tempQueue.Enqueue(value);
                }
            }
            foreach (var obj in state.clonableObjects) tempQueue.Enqueue(obj);
            while (tempQueue.TryDequeue(out var target))
                if (dependencies.TryGetValue(target, out var parents))
                    foreach (var parent in parents)
                        if (state.parents.Add(parent) &&
                            ValidateCloneable(parent, in state, true))
                            tempQueue.Enqueue(parent);
            return state;
        }

        static bool ValidateCloneable(UnityObject target, in State state, bool forced) {
            var assetPath = AssetDatabase.GetAssetPath(target);
            bool isAsset = !string.IsNullOrEmpty(assetPath);
            bool isCloned = false;
            if (isAsset && !state.scannedPaths.TryGetValue(assetPath, out isCloned)) {
                isCloned = AssetDatabase.LoadMainAssetAtPath(assetPath) is SubAssetContainer;
                state.scannedPaths.Add(assetPath, isCloned);
            }
            if (isCloned) return false;
            if (isAsset && !forced) {
                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (AssetDatabase.IsMainAsset(target) &&
                    AssetDatabase.LoadAllAssetsAtPath(assetPath).Length <= 1)
                    return false;
            }
            if (target is GameObject || target is Component)
                return false;
            return state.clonableObjects.Add(target);
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

        int CloneObjects(UnityObject currentContainer, in State state) {
            try {
                int cloneCount = 0;
                foreach (var obj in state.clonableObjects) {
                    var clone = UnityObject.Instantiate(obj);
                    clone.name = obj.name;
                    clone.hideFlags = obj.hideFlags;
                    tempClones[obj] = clone;
                }
                foreach (var obj in state.parents) tempQueue.Enqueue(obj);
                state.allObjects.Clear();
                while (tempQueue.TryDequeue(out var obj)) {
                    if (!state.allObjects.Add(obj)) continue;
                    using var so = new SerializedObject(obj);
                    bool modified = false;
                    for (var prop = so.GetIterator(); prop.Next(true);) {
                        if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var value = prop.objectReferenceValue;
                        if (value == null || !tempClones.TryGetValue(value, out var clone)) continue;
                        if (state.clonableObjects.Remove(value)) {
                            tempQueue.Enqueue(clone);
                            AssetDatabase.AddObjectToAsset(clone, currentContainer);
                            cloneCount++;
                        }
                        prop.objectReferenceValue = value = clone;
                        modified = true;
                    }
                    if (modified) so.ApplyModifiedPropertiesWithoutUndo();
                }
                return cloneCount;
            } finally {
                tempClones.Clear();
            }
        }

        struct State {
            public HashSet<UnityObject> allObjects;
            public HashSet<UnityObject> parents;
            public HashSet<UnityObject> clonableObjects;
            public Dictionary<string, bool> scannedPaths;
        }
    }
}