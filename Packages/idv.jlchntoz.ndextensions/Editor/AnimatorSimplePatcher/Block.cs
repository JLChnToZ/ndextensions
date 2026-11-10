using System.Collections.Generic;
using UnityEngine.Pool;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class Block {
        public readonly List<Statement> statements = ListPool<Statement>.Get();
    }
}