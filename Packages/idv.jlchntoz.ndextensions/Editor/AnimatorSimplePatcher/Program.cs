using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    static class ParameterTypeFallback {
        public static AnimatorControllerParameterType Merge(
            AnimatorControllerParameterType left,
            AnimatorControllerParameterType right
        ) => Rank(left) >= Rank(right) ? left : right;

        static int Rank(AnimatorControllerParameterType type) => type switch {
            AnimatorControllerParameterType.Bool => 0,
            AnimatorControllerParameterType.Int => 1,
            AnimatorControllerParameterType.Float => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported animator parameter type."),
        };
    }

    sealed class Program : IDisposable {
        public readonly Block root;
        readonly List<Block> blocks;

        public Program(Block root, List<Block> blocks) {
            this.root = root;
            this.blocks = blocks;
        }

        public void Dispose() {
            foreach (var block in blocks)
                ListPool<Statement>.Release(block.statements);
            ListPool<Block>.Release(blocks);
        }
    }
}