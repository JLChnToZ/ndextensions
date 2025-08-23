using System;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class ParameterCache {
        readonly GameObject root;
        readonly Dictionary<string, Component> guessedComponents = new();
        readonly Dictionary<(string, Component), (AnimatorControllerParameterType type, bool synchroized)> parameters = new();
        readonly Type[] ignoreComponents;

        public IReadOnlyDictionary<(string propertyName, Component source), (AnimatorControllerParameterType type, bool synchroized)> Parameters => parameters;

        public IReadOnlyDictionary<string, Component> GuessedComponents => guessedComponents;

        public ParameterCache(GameObject root, Type[] ignoreComponents = null) {
            this.root = root;
            this.ignoreComponents = ignoreComponents ?? Array.Empty<Type>();
            Refresh();
        }

        public void Refresh() {
            parameters.Clear();
            var info = ParameterInfo.ForUI.GetParametersForObject(root);
            foreach (var p in info) {
                if (p.ParameterType == null ||
                    p.Namespace != ParameterNamespace.Animator ||
                    p.Source != null && Array.IndexOf(ignoreComponents, p.Source.GetType()) >= 0)
                    continue;
                parameters[(p.OriginalName, p.Source)] = (p.ParameterType.Value, p.WantSynced);
                if (p.Source != null &&
                    p.OriginalName == p.EffectiveName &&
                    !guessedComponents.ContainsKey(p.OriginalName))
                    guessedComponents.Add(p.OriginalName, p.Source);
            }
        }
    }
}
