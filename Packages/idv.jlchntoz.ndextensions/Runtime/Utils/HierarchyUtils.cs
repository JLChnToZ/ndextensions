using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors {
    public static class HierarchyUtils {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<T> GetBuildableComponentsInChildren<T>(this GameObject root) where T : class =>
            GetBuildableComponentsInChildren<T>(root.transform);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<T> GetBuildableComponentsInChildren<T>(this Component root) where T : class =>
            GetBuildableComponentsInChildren<T>(root.transform);

        public static IEnumerable<T> GetBuildableComponentsInChildren<T>(this Transform root) where T : class {
            if (root.CompareTag("EditorOnly")) yield break;
            var queue = new Queue<Transform>();
            var tempComponents = new List<Component>();
            queue.Enqueue(root);
            while (queue.TryDequeue(out var current)) {
                current.GetComponents(tempComponents);
                foreach (var c in tempComponents)
                    if (c is T typedComponent)
                        yield return typedComponent;
                foreach (Transform child in current)
                    if (!child.CompareTag("EditorOnly"))
                        queue.Enqueue(child);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBuildableComponentsInChildren<T>(this GameObject root, IList<T> results) where T : class =>
            GetBuildableComponentsInChildren(root.transform, results);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBuildableComponentsInChildren<T>(this Component root, IList<T> results) where T : class =>
            GetBuildableComponentsInChildren(root.transform, results);

        public static void GetBuildableComponentsInChildren<T>(this Transform root, IList<T> results) where T : class {
            results.Clear();
            var iter = root.GetBuildableComponentsInChildren<T>();
            if (results is List<T> list) {
                list.AddRange(iter);
                return;
            }
            foreach (var component in iter)
                results.Add(component);
        }
    }
}