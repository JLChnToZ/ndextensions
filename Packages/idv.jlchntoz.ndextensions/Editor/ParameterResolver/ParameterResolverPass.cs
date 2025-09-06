using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

using NDMFParameterInfo = nadena.dev.ndmf.ParameterInfo;

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(ParameterResolverContext))]
    sealed class ParameterResolverEarlyPass : Pass<ParameterResolverEarlyPass> {
        protected override void Execute(BuildContext context) =>
            context.GetState(ParameterResolverState.Create).Resolve(context);
    }

    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(ParameterResolverContext))]
    sealed class ParameterResolverPass : Pass<ParameterResolverPass> {
        protected override void Execute(BuildContext context) =>
            context.GetState(ParameterResolverState.Create).Resolve(context);
    }

    sealed class ParameterResolverState {
        readonly ParameterResolverContext extContext;
        bool firstRun;

        public static ParameterResolverState Create(BuildContext context) =>
            new(context.Extension<ParameterResolverContext>());

        ParameterResolverState(ParameterResolverContext extContext) {
            this.extContext = extContext;
            firstRun = true;
        }

        public void Resolve(BuildContext context) {
            var fieldPatcher = new FieldPatcher<AnimatorParameterRef>(Resolve);
            foreach (var config in context.AvatarRootObject.GetComponentsInChildren<MonoBehaviour>(true))
                fieldPatcher.Patch(config);
            firstRun = false;
        }

        bool Resolve(ref AnimatorParameterRef parameter) {
            if (firstRun) parameter = parameter.Persistant;
            extContext.Resolve(parameter, out _, true);
            return firstRun;
        }
    }

    public class ParameterResolverContext : IExtensionContext {
        NDMFParameterInfo parameterInfos;
        readonly Dictionary<int, IReadOnlyDictionary<(ParameterNamespace, string), ParameterMapping>> sourceToMappings = new();
        private readonly Dictionary<AnimatorParameterRef, ParameterMapping> refToMappings = new();

        void IExtensionContext.OnActivate(BuildContext context) {
            parameterInfos = NDMFParameterInfo.ForContext(context);
        }

        void IExtensionContext.OnDeactivate(BuildContext context) {
            sourceToMappings.Clear();
        }

        public bool Resolve(AnimatorParameterRef parameter, out ParameterMapping mapping, bool forceRefresh = false) {
            if (string.IsNullOrEmpty(parameter.name)) {
                mapping = default;
                return false;
            }
            parameter = parameter.Persistant;
            if (!forceRefresh && refToMappings.TryGetValue(parameter, out mapping))
                return true;
            int instanceID = parameter.InstanceID;
            if (!sourceToMappings.TryGetValue(instanceID, out var mappings)) {
                if (parameter.source != null) mappings = parameterInfos.GetParameterRemappingsAt(parameter.source, true);
                else if (parameter.gameObject != null) mappings = parameterInfos.GetParameterRemappingsAt(parameter.gameObject);
                sourceToMappings[instanceID] = mappings;
            }
            if (mappings != null && mappings.TryGetValue((ParameterNamespace.Animator, parameter.name), out mapping)) {
                refToMappings[parameter] = mapping;
                return true;
            }
            mapping = new ParameterMapping {
                ParameterName = parameter.name,
                IsHidden = false,
            };
            return false;
        }
    }
}