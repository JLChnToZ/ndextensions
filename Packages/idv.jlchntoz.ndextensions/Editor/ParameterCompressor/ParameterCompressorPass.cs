using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(ParameterCompressorContext))]
    public class ParameterCompressorPrePass : Pass<ParameterCompressorPrePass> {
        protected override void Execute(BuildContext context) {
            var ctx = context.Extension<ParameterCompressorContext>();
            // Pre pass to make VRCFury happy.
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<ParameterCompressor>(true))
                if (component != null && component.parameters != null)
                    foreach (var parameter in component.parameters)
                        if (!string.IsNullOrEmpty(parameter.name))
                            ctx.PreprocessParameter(parameter.name);
        }
    }

    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(ParameterCompressorContext))]
    public class ParameterCompressorPass : Pass<ParameterCompressorPass> {
        protected override void Execute(BuildContext context) {
            var allParameters = new HashSet<string>();
            float threshold = 0F;
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<ParameterCompressor>(true)) {
                if (component == null || component.parameters == null) continue;
                foreach (var parameter in component.parameters) {
                    if (string.IsNullOrEmpty(parameter.name)) continue;
                    allParameters.Add(parameter.name);
                }
                threshold = Math.Max(threshold, component.threshold);
                UnityObject.DestroyImmediate(component);
            }
            var ctx = context.Extension<ParameterCompressorContext>();
            if (allParameters.Count == 0) return;
            ctx.Init(allParameters.Count, threshold);
            ctx.ResetProcessedMarker();
            foreach (var parameter in allParameters)
                ctx.ProcessParameter(parameter);
            ctx.FinalizeParameterConnections();
        }
    }
}
