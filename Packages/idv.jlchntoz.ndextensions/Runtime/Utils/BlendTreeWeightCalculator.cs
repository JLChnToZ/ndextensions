using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public class BlendTreeWeightCalculator<T> {
        readonly Dictionary<T, Vector2> positions = new();

        public BlendTreeWeightCalculator() { }

        public void SetPosition(T key, float x, float y) => positions[key] = new Vector2(x, y);

        public void Clear() => positions.Clear();

        public Dictionary<T, float> CalcWeightsSimple1D(float x) {
            var results = new Dictionary<T, float>();
            CalcWeightsSimple1D(x, results);
            return results;
        }

        public void CalcWeightsSimple1D(float x, IDictionary<T, float> results) {
            results.Clear();
            if (positions.Count == 0) return;
            if (positions.Count == 1) {
                foreach (var kv in positions) results[kv.Key] = 1F;
                return;
            }
            var sorted = new List<KeyValuePair<T, Vector2>>(positions);
            sorted.Sort(ComparePosition1D);
            int low = 0, high = sorted.Count - 1;
            while (low <= high) {
                int mid = (low + high) / 2;
                var midValue = sorted[mid].Value.x;
                if (midValue < x)
                    low = mid + 1;
                else if (midValue > x)
                    high = mid - 1;
                else {
                    results[sorted[mid].Key] = 1F;
                    return;
                }
            }
            if (low > high) (low, high) = (high, low);
            var lowKV = sorted[low];
            var highKV = sorted[high];
            var w = Mathf.InverseLerp(lowKV.Value.x, highKV.Value.x, x);
            results[lowKV.Key] = 1F - w;
            results[highKV.Key] = w;
        }

        public Dictionary<T, float> CalcWeightsSimpleDirectional2D(float x, float y) {
            var results = new Dictionary<T, float>();
            CalcWeightsSimpleDirectional2D(x, y, results);
            return results;
        }

        public void CalcWeightsSimpleDirectional2D(float x, float y, IDictionary<T, float> results) {
            results.Clear();
            if (positions.Count == 0) return;
            if (positions.Count == 1) {
                foreach (var kv in positions) results[kv.Key] = 1F;
                return;
            }
            float sum = 0F;
            var input = new Vector2(x, y);
            var normalizedInput = input.normalized;
            foreach (var kv in positions) {
                var weight = Mathf.Max(Vector2.Dot(normalizedInput, kv.Value.normalized), 0F);
                results[kv.Key] = weight;
                sum += weight;
            }
            if (sum > 0F)
                foreach (var k in positions.Keys)
                    if (results.TryGetValue(k, out var weight))
                        results[k] = weight / sum;
        }

        public Dictionary<T, float> CalcWeightsFreeformDirectional2D(float x, float y) {
            var results = new Dictionary<T, float>();
            CalcWeightsFreeformDirectional2D(x, y, results);
            return results;
        }

        public void CalcWeightsFreeformDirectional2D(float x, float y, IDictionary<T, float> results) {
            results.Clear();
            if (positions.Count == 0) return;
            if (positions.Count == 1) {
                foreach (var kv in positions) results[kv.Key] = 1F;
                return;
            }
        }

        static int ComparePosition1D(KeyValuePair<T, Vector2> a, KeyValuePair<T, Vector2> b) => a.Value.x.CompareTo(b.Value.x);
    }
}