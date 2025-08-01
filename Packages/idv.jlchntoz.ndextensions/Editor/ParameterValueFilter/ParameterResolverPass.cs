using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

using NDMFParameterInfo = nadena.dev.ndmf.ParameterInfo;

namespace JLChnToZ.NDExtensions.Editors {
    sealed class ParameterResolverPass : Pass<ParameterResolverPass> {
        readonly Dictionary<Type, FieldInfo[]> fieldCache = new();
        readonly Dictionary<Component, IReadOnlyDictionary<(ParameterNamespace, string), ParameterMapping>> sourceToMappings = new();
        NDMFParameterInfo parameterInfos;

        protected override void Execute(BuildContext context) {
            parameterInfos = NDMFParameterInfo.ForContext(context);
            sourceToMappings.Clear();
            foreach (var config in context.AvatarRootObject.GetComponentsInChildren<MonoBehaviour>(true))
                Resolve(config);
        }

        void Resolve(MonoBehaviour config) {
            if (config == null) return;
            var configType = config.GetType();
            if (!fieldCache.TryGetValue(configType, out var fields)) {
                fields = configType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => field.FieldType == typeof(AnimatorParameterRef))
                    .ToArray();
                fieldCache[configType] = fields;
            }
            foreach (var field in fields) {
                var parameter = (AnimatorParameterRef)field.GetValue(config);
                if (!parameter.IsValid) continue;
                Resolve(ref parameter);
                field.SetValue(config, parameter);
            }
        }

        void Resolve(ref AnimatorParameterRef parameter) {
            if (string.IsNullOrEmpty(parameter.name) || parameter.source == null) return;
            if (!sourceToMappings.TryGetValue(parameter.source, out var mappings))
                mappings = parameterInfos.GetParameterRemappingsAt(parameter.source, true);
            if (mappings.TryGetValue((ParameterNamespace.Animator, parameter.name), out var mapping))
                parameter.name = mapping.ParameterName;
            parameter.source = null;
        }
    }
}