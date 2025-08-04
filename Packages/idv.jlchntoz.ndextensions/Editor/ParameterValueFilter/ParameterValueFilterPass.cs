using System.Collections.Generic;
using UnityEditor.Animations;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class ParameterValueFilterPass : Pass<ParameterValueFilterPass> {
        protected override void Execute(BuildContext context) {
            var extContext = context.Extension<AnimatorServicesContext>();
            var controllers = extContext.ControllerContext.Controllers;
            var lookup = new Dictionary<string, ParameterValueFilter>();
            foreach (var filter in context.AvatarRootObject.GetComponentsInChildren<ParameterValueFilter>(true)) {
                if (filter == null || string.IsNullOrEmpty(filter.parameter.name)) continue;
                if (!lookup.ContainsKey(filter.parameter.name))
                    lookup[filter.parameter.name] = filter;
            }
#if VRC_SDK_VRCSDK3
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.Base, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.Additive, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.Gesture, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.Action, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.FX, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.Sitting, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.IKPose, lookup);
            ProcessController(controllers, VRCAvatarDescriptor.AnimLayerType.TPose, lookup);
#else
            ProcessController(controllers, context.AvatarRootObject.GetComponent<Animator>(), lookup);
#endif
            foreach (var tag in lookup.Values) UnityObject.DestroyImmediate(tag);
        }

        void ProcessController(IDictionary<object, VirtualAnimatorController> controllers, object tag, IReadOnlyDictionary<string, ParameterValueFilter> filters) {
            if (tag != null && controllers.TryGetValue(tag, out var controller)) ProcessController(controller, filters);
        }

        void ProcessController(VirtualAnimatorController controller, IReadOnlyDictionary<string, ParameterValueFilter> filters) {
            var context = new AAPContext(controller);
            var replaceParameters = new Dictionary<string, string>();
            foreach (var parameter in controller.Parameters) {
                if (!filters.TryGetValue(parameter.Key, out var filter)) continue;
                var smoothFactor = filter.SmoothFactor;
                if (!string.IsNullOrEmpty(smoothFactor.propertyName))
                    context.EnsureParameter(filter.parameter);
                else if (filter.smoothType == SmoothType.None && !filter.remapValues)
                    continue;
                replaceParameters[parameter.Key] = context.GetUniqueParameter($"{parameter.Key}/AAP_Smooth", parameter.Value.defaultFloat);
            }
            if (replaceParameters.Count == 0) return;
            ReplaceParameters(controller, replaceParameters);
            foreach (var (src, dest) in replaceParameters) {
                if (!filters.TryGetValue(src, out var filter)) continue;
                switch (filter.smoothType) {
                    case SmoothType.None:
                        if (filter.remapValues)
                            context.Remap(src, dest, filter.minValue, filter.maxValue, filter.remapMin, filter.remapMax);
                        break;
                    case SmoothType.Linear:
                        if (filter.remapValues) {
                            var remapParameter = context.GetUniqueParameter($"{src}/AAP_Remap", 0);
                            context.LinearSmooth(src, remapParameter, filter.SmoothFactor, filter.minValue, filter.maxValue, filter.maxDelta);
                            context.Remap(remapParameter, dest, filter.minValue, filter.maxValue, filter.remapMin, filter.remapMax);
                        } else
                            context.LinearSmooth(src, dest, filter.SmoothFactor, filter.minValue, filter.maxValue, filter.maxDelta);
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