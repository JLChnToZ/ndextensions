using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
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
            blocks.Clear();
        }
    }
}