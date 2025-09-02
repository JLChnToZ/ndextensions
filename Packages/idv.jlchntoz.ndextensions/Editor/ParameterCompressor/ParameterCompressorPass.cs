using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;
#if VRC_SDK_VRCSDK3
using VRCParameterType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(ParameterCompressorContext))]
    [DependsOnContext(typeof(ParameterResolverContext))]
    public class ParameterCompressorPrePass : Pass<ParameterCompressorPrePass> {
        protected override void Execute(BuildContext context) {
            var paramResolver = context.Extension<ParameterResolverContext>();
            var paramCompressor = context.Extension<ParameterCompressorContext>();
            // Pre pass to make VRCFury happy.
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<ParameterCompressor>(true))
                if (component != null && component.parameters != null)
                    foreach (var parameter in component.parameters) {
                        paramResolver.Resolve(parameter, out var info);
                        if (string.IsNullOrEmpty(info.ParameterName))
                            continue;
                        paramCompressor.PreprocessParameter(info.ParameterName);
                    }
        }
    }

    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(ParameterCompressorContext))]
    [DependsOnContext(typeof(ParameterResolverContext))]
    public class ParameterCompressorPass : Pass<ParameterCompressorPass> {
        protected override void Execute(BuildContext context) {
            var paramResolver = context.Extension<ParameterResolverContext>();
            var paramCompressor = context.Extension<ParameterCompressorContext>();
            var allParameters = new HashSet<string>();
            int boolsCount = 0, intFloatsCount = 0;
            float threshold = 0F;
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<ParameterCompressor>(true)) {
                if (component == null) continue;
                if (component.parameters != null)
                    foreach (var parameter in component.parameters) {
                        paramResolver.Resolve(parameter, out var info);
                        if (string.IsNullOrEmpty(info.ParameterName) ||
                            !allParameters.Add(info.ParameterName)) {
                            AddNotResolveError(parameter, info);
                            continue;
                        }
                        var p = paramCompressor.PreprocessParameter(info.ParameterName);
                        if (p == null) {
                            AddNotResolveError(parameter, info);
                            continue;
                        }
#if VRC_SDK_VRCSDK3
                        if (p.valueType == VRCParameterType.Bool)
                            boolsCount++;
                        else
                            intFloatsCount++;
#endif
                    }
                threshold = Math.Max(threshold, component.threshold);
                UnityObject.DestroyImmediate(component);
            }
            if (allParameters.Count == 0) return;
            paramCompressor.Init(intFloatsCount, boolsCount, threshold);
            foreach (var parameter in allParameters)
                paramCompressor.ProcessParameter(parameter);
            paramCompressor.FinalizeParameterConnections();
        }

        void AddNotResolveError(AnimatorParameterRef src, ParameterMapping info) {
            var warn = new NDMFWarn("ParameterCompressor:not_resolved") {
                detailsSubSt = new[] { src.name, info.ParameterName },
            };
            if (src.source != null) warn.AddReference(ObjectRegistry.GetReference(src.source));
            ErrorReport.ReportError(warn);
        }
    }
}
