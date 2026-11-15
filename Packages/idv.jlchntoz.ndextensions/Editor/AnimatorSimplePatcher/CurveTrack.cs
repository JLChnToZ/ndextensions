using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class CurveTrack {
        readonly SortedDictionary<float, Keyframe> keys = new();
        public float LastValue;
        public float LastTime;

        public void Set(Keyframe key) => keys[key.time] = key;

        public bool TryGet(float time, out Keyframe key) => keys.TryGetValue(time, out key);

        public Keyframe[] CopyKeys() {
            var result = new Keyframe[keys.Count];
            keys.Values.CopyTo(result, 0);
            return result;
        }
    }

    sealed class PointerCurveTrack {
        readonly SortedDictionary<float, ObjectReferenceKeyframe> keys = new();
        public UnityObject LastValue;
        public float LastTime;

        public void Set(ObjectReferenceKeyframe key) => keys[key.time] = key;

        public ObjectReferenceKeyframe[] CopyKeys() {
            var result = new ObjectReferenceKeyframe[keys.Count];
            keys.Values.CopyTo(result, 0);
            return result;
        }
    }
}
