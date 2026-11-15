using System;
using System.Collections.Generic;
using UnityEngine.Pool;
using UnityEditor.Animations;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    readonly struct ClauseScope : IDisposable {
        readonly List<List<AnimatorCondition>> clauses;
        public int Count => clauses.Count;

        public ClauseScope(out List<List<AnimatorCondition>> clauses) {
            this.clauses = ListPool<List<AnimatorCondition>>.Get();
            clauses = this.clauses;
        }

        public List<AnimatorCondition> Add() {
            var clause = ListPool<AnimatorCondition>.Get();
            clauses.Add(clause);
            return clause;
        }

        public void Dispose() {
            foreach (var clause in clauses) ListPool<AnimatorCondition>.Release(clause);
            ListPool<List<AnimatorCondition>>.Release(clauses);
        }
    }
}
