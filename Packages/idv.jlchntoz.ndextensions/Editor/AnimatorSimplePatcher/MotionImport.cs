using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEditor.Animations;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    static class MotionImport {
        public static Motion Resolve(ImportStatement statement, IReadOnlyDictionary<string, UnityObject> references) {
            if (references == null || !references.TryGetValue(statement.referenceName, out var value) || value == null)
                throw new ParseException(statement.row, statement.col, $"Could not resolve motion reference '{statement.referenceName}'.");
            if (value is AnimationClip ac) return ac;
            if (value is BlendTree bt) return bt;
            throw new ParseException(statement.row, statement.col, $"Reference '{statement.referenceName}' is not an animation clip or blend tree.");
        }

        public static float Analyze(ImportStatement statement, Motion motion, Action<string> addParameter) {
            using (HashSetPool<BlendTree>.Get(out var active))
                return Analyze(statement, motion, addParameter, active);
        }

        static float Analyze(ImportStatement statement, Motion motion, Action<string> addParameter, HashSet<BlendTree> active) {
            if (motion is AnimationClip clip) {
                var length = clip.length;
                if (float.IsNaN(length) || float.IsInfinity(length) || length < 0)
                    throw new ParseException(statement.row, statement.col, "Imported clip has an invalid duration.");
                return length;
            }
            if (!(motion is BlendTree tree))
                throw new ParseException(statement.row, statement.col, "Imported tree contains an unsupported motion.");
            if (!active.Add(tree))
                throw new ParseException(statement.row, statement.col, "Imported blend tree contains a cycle.");
            try {
                if (tree.blendType != BlendTreeType.Direct) {
                    AddParameter(tree.blendParameter, addParameter);
                    if (tree.blendType != BlendTreeType.Simple1D)
                        AddParameter(tree.blendParameterY, addParameter);
                }
                float duration = 0;
                foreach (var child in tree.children) {
                    if (tree.blendType == BlendTreeType.Direct) AddParameter(child.directBlendParameter, addParameter);
                    if (child.motion == null) continue;
                    if (child.timeScale == 0 || float.IsNaN(child.timeScale) || float.IsInfinity(child.timeScale))
                        throw new ParseException(statement.row, statement.col, "Imported blend tree contains a zero or invalid child time scale.");
                    var childDuration = Analyze(statement, child.motion, addParameter, active) / Mathf.Abs(child.timeScale);
                    if (float.IsNaN(childDuration) || float.IsInfinity(childDuration))
                        throw new ParseException(statement.row, statement.col, "Imported blend tree has an invalid duration.");
                    duration = Mathf.Max(duration, childDuration);
                }
                return duration;
            } finally {
                active.Remove(tree);
            }
        }

        static void AddParameter(string name, Action<string> addParameter) {
            if (!string.IsNullOrEmpty(name)) addParameter(name);
        }
    }
}
