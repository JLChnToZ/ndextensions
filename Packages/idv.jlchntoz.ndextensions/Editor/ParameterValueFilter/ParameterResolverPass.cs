using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

using NDMFParameterInfo = nadena.dev.ndmf.ParameterInfo;

namespace JLChnToZ.NDExtensions.Editors {
    sealed class ParameterResolverPass : Pass<ParameterResolverPass> {
        FieldPatcher<AnimatorParameterRef> fieldPatcher;
        readonly Dictionary<Component, IReadOnlyDictionary<(ParameterNamespace, string), ParameterMapping>> sourceToMappings = new();
        NDMFParameterInfo parameterInfos;

        protected override void Execute(BuildContext context) {
            fieldPatcher = new(Resolve);
            parameterInfos = NDMFParameterInfo.ForContext(context);
            sourceToMappings.Clear();
            foreach (var config in context.AvatarRootObject.GetComponentsInChildren<MonoBehaviour>(true))
                fieldPatcher.Patch(config);
        }

        bool Resolve(ref AnimatorParameterRef parameter) {
            if (string.IsNullOrEmpty(parameter.name) || parameter.source == null) return false;
            if (!sourceToMappings.TryGetValue(parameter.source, out var mappings))
                mappings = parameterInfos.GetParameterRemappingsAt(parameter.source, true);
            if (mappings.TryGetValue((ParameterNamespace.Animator, parameter.name), out var mapping))
                parameter.name = mapping.ParameterName;
            parameter.source = null;
            return true;
        }
    }
}