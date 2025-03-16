using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public class HierarchyComparer: IComparer<Transform> {
        readonly Dictionary<Transform, float> scores = new();
        readonly bool depthFirst;

        public HierarchyComparer(bool depthFirst = false) => this.depthFirst = depthFirst;

        public void ClearCache() => scores.Clear();

        public int Compare(Transform x, Transform y) {
            if(x == y) return 0;
            if(x == null) return -1;
            if(y == null) return 1;
            return GetScore(x).CompareTo(GetScore(y));
        }

        protected float GetScore(Transform transform) {
            if (!scores.TryGetValue(transform, out var score))
                scores[transform] = score = CalculateScore(transform);
            return score;
        }

        protected virtual float CalculateScore(Transform transform) {
            float score = 0;
            int depth = 0;
            for (var current = transform; current != null; current = current.parent, depth++)
                score = score / Mathf.Max(current.childCount, 1) + current.GetSiblingIndex();
            score /= Mathf.Max(transform.gameObject.scene.rootCount, 1);
            if (depthFirst) score += depth;
            return score;
        }
    }
}