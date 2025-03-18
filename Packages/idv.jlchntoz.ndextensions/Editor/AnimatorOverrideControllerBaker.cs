using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public class AnimatorOverrideControllerBaker {
        AnimatorController controller;
        readonly HashSet<UnityObject> createdObjects = new();
        readonly string name;
        readonly Dictionary<AnimationClip, AnimationClip> clipOverrides = new();

        public AnimatorOverrideControllerBaker(AnimatorOverrideController overrideController) {
            name = overrideController.name;
            var temp = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            while (overrideController != null) {
                overrideController.GetOverrides(temp);
                foreach (var map in temp) {
                    if (map.Key == null || map.Value == null) continue;
                    if (clipOverrides.TryGetValue(map.Value, out var value2)) {
                        clipOverrides.Remove(map.Value);
                        clipOverrides.Add(map.Key, value2);
                    } else
                        clipOverrides[map.Key] = map.Value;
                }
                if (overrideController.runtimeAnimatorController is AnimatorController baseController) {
                    controller = baseController;
                    break;
                }
                overrideController = overrideController.runtimeAnimatorController as AnimatorOverrideController;
            }
        }

        public AnimatorController Bake() {
            if (clipOverrides.Count == 0) return controller; // No need to bake
            var dependencies = new Dictionary<UnityObject, HashSet<UnityObject>>();
            var remap = new Dictionary<UnityObject, UnityObject>();
            var walked = new HashSet<UnityObject>();
            var pending = new Stack<UnityObject>();
            var parentStack = new Stack<UnityObject>();
            var cloneNeeded = new HashSet<UnityObject>();
            foreach (var kv in clipOverrides) remap[kv.Key] = kv.Value;
            pending.Push(controller);
            while (pending.TryPop(out var entry)) {
                using var so = new SerializedObject(entry);
                for (var iterator = so.GetIterator(); iterator.Next(true);) {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = iterator.objectReferenceValue;
                    if (value == null || value is GameObject || value is Component) continue;
                    HashSet<UnityObject> parents;
                    if (cloneNeeded.Contains(value) || (value is AnimationClip clip && clipOverrides.ContainsKey(clip))) {
                        parentStack.Push(entry);
                        while (parentStack.TryPop(out var parent))
                            if (dependencies.TryGetValue(parent, out parents) &&
                                cloneNeeded.Add(parent))
                                foreach (var p in parents)
                                    parentStack.Push(p);
                    }
                    if (dependencies.TryGetValue(value, out parents))
                        parents.Add(entry);
                    else
                        dependencies.Add(value, new() { entry });
                    if (walked.Add(value)) pending.Push(value);
                }
            }
            var newController = Instantiate(controller);
            createdObjects.Add(newController);
            newController.name = name;
            pending.Push(newController);
            while (pending.TryPop(out var entry)) {
                using var so = new SerializedObject(entry);
                for (var iterator = so.GetIterator(); iterator.Next(true);) {
                    if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = iterator.objectReferenceValue;
                    if (value == null) continue;
                    if (remap.TryGetValue(value, out var newValue)) {
                        iterator.objectReferenceValue = newValue;
                        continue;
                    }
                    if (cloneNeeded.Contains(value)) {
                        newValue = Instantiate(value);
                        newValue.name = value.name;
                        newValue.hideFlags = value.hideFlags;
                        createdObjects.Add(newValue);
                        remap[value] = newValue;
                        iterator.objectReferenceValue = newValue;
                        pending.Push(newValue);
                        continue;
                    }
                }
                so.ApplyModifiedProperties();
            }
            controller = newController;
            clipOverrides.Clear();
            return controller;
        }

        public void SaveToAsset(UnityObject asset) {
            foreach (var createdObject in createdObjects)
                if (!AssetDatabase.Contains(createdObject))
                    AssetDatabase.AddObjectToAsset(createdObject, asset);
            foreach (var clip in clipOverrides.Values)
                if (!AssetDatabase.Contains(clip))
                    AssetDatabase.AddObjectToAsset(clip, asset);
        }
    }
}