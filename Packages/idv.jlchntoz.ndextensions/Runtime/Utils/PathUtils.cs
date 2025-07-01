using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public static class PathUtils {
        static readonly Stack<string> pathStack = new Stack<string>();

        public static string GetPath(this GameObject gameObject, Transform root = null) =>
            gameObject != null ? GetPath(gameObject.transform, root) : string.Empty;

        public static string GetPath(this Component component, Transform root = null) =>
            component != null ? GetPath(component.transform, root) : string.Empty;

        public static string GetPath(this Transform transform, Transform root = null) {
            lock (pathStack)
                try {
                    for (var c = transform; c != null && c != root; c = c.parent)
                        pathStack.Push(c.name);
                    return string.Join('/', pathStack);
                } finally {
                    pathStack.Clear();
                }
        }
    }
}