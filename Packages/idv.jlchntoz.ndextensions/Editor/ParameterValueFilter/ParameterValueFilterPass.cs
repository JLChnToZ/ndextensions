using System.Collections.Generic;
using UnityEditor.Animations;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    [RunsOnAllPlatforms]
    [DependsOnContext(typeof(ParameterResolverContext))]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public sealed class ParameterValueFilterPass : Pass<ParameterValueFilterPass> {
        protected override void Execute(BuildContext context) {
            var extContext = context.Extension<AnimatorServicesContext>();
            var resolverContext = context.Extension<ParameterResolverContext>();
            var controllers = extContext.ControllerContext.Controllers;
            var lookup = new Dictionary<string, ParameterValueFilter>();
            foreach (var filter in context.AvatarRootObject.GetComponentsInChildren<ParameterValueFilter>(true)) {
                resolverContext.Resolve(filter.parameter, out var info);
                if (!lookup.ContainsKey(info.ParameterName))
                    lookup[info.ParameterName] = filter;
            }
#if VRC_SDK_VRCSDK3
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.Base, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.Additive, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.Gesture, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.Action, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.FX, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.Sitting, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.IKPose, lookup);
            ProcessController(context, controllers, VRCAvatarDescriptor.AnimLayerType.TPose, lookup);
#else
            ProcessController(context, controllers, context.AvatarRootObject.GetComponent<Animator>(), lookup);
#endif
            foreach (var tag in lookup.Values) UnityObject.DestroyImmediate(tag);
        }

        void ProcessController(BuildContext context, IDictionary<object, VirtualAnimatorController> controllers, object tag, IReadOnlyDictionary<string, ParameterValueFilter> filters) {
            if (tag != null && controllers.TryGetValue(tag, out var controller)) ProcessController(context, controller, filters);
        }

        void ProcessController(BuildContext buildContext, VirtualAnimatorController controller, IReadOnlyDictionary<string, ParameterValueFilter> filters) {
            var context = AAPContext.ForController(buildContext, controller);
            var replaceParameters = new Dictionary<string, string>();
            var processFilters = new Dictionary<ParameterValueFilter, (string src, string dest)>();
            foreach (var kv in controller.Parameters) {
                var dest = kv.Key;
                if (!filters.TryGetValue(dest, out var filter)) continue;
                var parameter = kv.Value;
                var smoothFactor = filter.SmoothFactor;
                if (!string.IsNullOrEmpty(smoothFactor.propertyName))
                    context.EnsureParameter(filter.parameter);
                else if (filter.smoothType == SmoothType.None && !filter.remapValues)
                    continue;
                string src;
                if (filter.sourceParameter.IsValid) {
                    src = filter.sourceParameter.name;
                    context.EnsureParameter(filter.sourceParameter);
                } else {
                    src = dest;
                    dest = context.GetUniqueParameter($"{src}/AAP_Smooth", parameter.defaultFloat);
                    replaceParameters[src] = dest;
                }
                processFilters[filter] = (src, dest);
            }
            if (processFilters.Count == 0) return;
            ReplaceParameters(controller, replaceParameters);
            foreach (var (filter, (src, dest)) in processFilters) {
                switch (filter.smoothType) {
                    case SmoothType.None:
                        if (filter.remapValues)
                            context.Remap(src, dest, filter.minValue, filter.maxValue, filter.remapMin, filter.remapMax);
                        break;
                    case SmoothType.Linear:
                        if (filter.remapValues) {
                            var remapParameter = context.GetUniqueParameter($"{src}/AAP_Remap", 0);
                            context.LinearSmooth(src, remapParameter, filter.SmoothFactor, filter.minValue, filter.maxValue);
                            context.Remap(remapParameter, dest, filter.minValue, filter.maxValue, filter.remapMin, filter.remapMax);
                        } else
                            context.LinearSmooth(src, dest, filter.SmoothFactor, filter.minValue, filter.maxValue);
                        break;
                    case SmoothType.Exponential:
                        if (filter.remapValues)
                            context.ExponentialSmooth(src, dest, filter.SmoothFactor, filter.minValue, filter.maxValue, filter.remapMin, filter.remapMax);
                        else
                            context.ExponentialSmooth(src, dest, filter.SmoothFactor, filter.minValue, filter.maxValue);
                        break;
                }
            }
        }

        void ReplaceParameters(VirtualAnimatorController controller, IReadOnlyDictionary<string, string> parameters) {
            if (parameters.Count == 0) return;
            foreach (var node in controller.AllReachableNodes()) {
                string n;
                if (node is VirtualTransitionBase transition) {
                    transition.Conditions = transition.Conditions.ConvertAll(c => {
                        if (c.parameter != null &&
                            parameters.TryGetValue(c.parameter, out var n))
                            c.parameter = n;
                        return c;
                    });
                    continue;
                }
                if (node is VirtualState state) {
                    if (state.MirrorParameter != null &&
                        parameters.TryGetValue(state.MirrorParameter, out n))
                        state.MirrorParameter = n;
                    if (state.SpeedParameter != null &&
                        parameters.TryGetValue(state.SpeedParameter, out n))
                        state.SpeedParameter = n;
                    if (state.TimeParameter != null &&
                        parameters.TryGetValue(state.TimeParameter, out n))
                        state.TimeParameter = n;
                    if (state.CycleOffsetParameter != null &&
                        parameters.TryGetValue(state.CycleOffsetParameter, out n))
                        state.CycleOffsetParameter = n;
                    continue;
                }
                if (node is VirtualBlendTree blendTree) {
                    switch (blendTree.BlendType) {
                        default:
                            if (blendTree.BlendParameterY != null &&
                                parameters.TryGetValue(blendTree.BlendParameterY, out n))
                                blendTree.BlendParameterY = n;
                            goto case BlendTreeType.Simple1D;
                        case BlendTreeType.Simple1D:
                            if (blendTree.BlendParameter != null &&
                                parameters.TryGetValue(blendTree.BlendParameter, out n))
                                blendTree.BlendParameter = n;
                            break;
                        case BlendTreeType.Direct:
                            blendTree.Children = blendTree.Children.ConvertAll(c => {
                                if (c.DirectBlendParameter != null &&
                                    parameters.TryGetValue(c.DirectBlendParameter, out var n))
                                    c.DirectBlendParameter = n;
                                return c;
                            });
                            break;
                    }
                    continue;
                }
            }
        }
    }
}