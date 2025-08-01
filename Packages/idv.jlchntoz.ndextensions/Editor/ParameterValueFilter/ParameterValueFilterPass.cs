using System.Collections.Generic;
using System.Collections.Immutable;
using UnityEngine;
using UnityEditor.Animations;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

using UnityObject = UnityEngine.Object;
using ACParameter = UnityEngine.AnimatorControllerParameter;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;

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
            var parameters = controller.Parameters;
            var replaceParameters = new Dictionary<string, string>();
            var remapParameters = new Dictionary<string, string>();
            foreach (var parameter in parameters) {
                if (!filters.TryGetValue(parameter.Key, out var filter)) continue;
                if (filter.smoothParameter.IsValid) {
                    if (!parameters.ContainsKey(filter.smoothParameter.name))
                        parameters = parameters.SetItem(filter.smoothParameter.name, filter.smoothParameter);
                } else if (filter.smoothValue >= 1 && (!filter.remapValues || (filter.remapMin == filter.minValue && filter.remapMax == filter.maxValue)))
                    continue;
                var smoothedName = GetUniqueName(ref parameters, $"__AAP/smooth_{parameter.Key}", parameter.Value.defaultFloat);
                replaceParameters[parameter.Key] = smoothedName;
                if (filter.remapValues && (filter.remapMin != filter.minValue || filter.remapMax != filter.maxValue) && filter.smoothValue < 1)
                    remapParameters[parameter.Key] = GetUniqueName(ref parameters, $"__AAP/remap_{parameter.Key}", filter.remapMin);
            }
            if (replaceParameters.Count == 0) return;
            ReplaceParameters(controller, replaceParameters);
            var layerStateMachine = controller.AddLayer(LayerPriority.Default, "Parameter Value Filter").StateMachine;
            var defaultBlendTree = VirtualBlendTree.Create("Smooth Parameters");
            defaultBlendTree.BlendType = BlendTreeType.Direct;
            var defaultState = layerStateMachine.AddState("Smooth Parameters (WD On)", defaultBlendTree);
            defaultState.WriteDefaultValues = true;
            layerStateMachine.DefaultState = defaultState;
            var tempParameters = new Dictionary<float, string>();
            foreach (var (src, dest) in replaceParameters) {
                if (!filters.TryGetValue(src, out var filter)) continue;
                var dest0 = VirtualClip.Create($"{dest} = {filter.minValue}");
                var dest1 = VirtualClip.Create($"{dest} = {filter.maxValue}");
                if (remapParameters.TryGetValue(src, out var lerp)) {
                    dest0.Name = $"{dest} = {filter.remapMin}";
                    dest0.SetFloatCurve("", typeof(Animator), dest, AnimationCurve.Constant(0, 1 / dest0.FrameRate, filter.remapMin));
                    dest1.Name = $"{dest} = {filter.remapMax}";
                    dest1.SetFloatCurve("", typeof(Animator), dest, AnimationCurve.Constant(0, 1 / dest1.FrameRate, filter.remapMax));
                } else
                    lerp = dest;
                dest0.SetFloatCurve("", typeof(Animator), lerp, AnimationCurve.Constant(0, 1 / dest0.FrameRate, filter.minValue));
                dest1.SetFloatCurve("", typeof(Animator), lerp, AnimationCurve.Constant(0, 1 / dest1.FrameRate, filter.maxValue));
                var destValueTree = VirtualBlendTree.Create("immediate");
                destValueTree.BlendType = BlendTreeType.Simple1D;
                destValueTree.BlendParameter = src;
                destValueTree.UseAutomaticThresholds = false;
                destValueTree.Children = ImmutableList.Create(
                    new VirtualBlendTree.VirtualChildMotion { Motion = dest0, Threshold = filter.minValue },
                    new VirtualBlendTree.VirtualChildMotion { Motion = dest1, Threshold = filter.maxValue }
                );
                if (filter.smoothParameter.IsValid || filter.smoothValue < 1) {
                    var srcValueTree = VirtualBlendTree.Create("smooth");
                    srcValueTree.BlendType = BlendTreeType.Simple1D;
                    srcValueTree.BlendParameter = lerp;
                    srcValueTree.UseAutomaticThresholds = false;
                    srcValueTree.Children = ImmutableList.Create(
                        new VirtualBlendTree.VirtualChildMotion { Motion = dest0, Threshold = filter.minValue },
                        new VirtualBlendTree.VirtualChildMotion { Motion = dest1, Threshold = filter.maxValue }
                    );
                    var smoothRootTree = VirtualBlendTree.Create(src);
                    smoothRootTree.BlendType = BlendTreeType.Simple1D;
                    smoothRootTree.BlendParameter = filter.smoothParameter.IsValid ?
                        filter.smoothParameter.name :
                        GetTempParameter(ref parameters, tempParameters, filter.smoothValue);
                    smoothRootTree.UseAutomaticThresholds = false;
                    smoothRootTree.Children = ImmutableList.Create(
                        new VirtualBlendTree.VirtualChildMotion { Motion = srcValueTree, Threshold = 0 },
                        new VirtualBlendTree.VirtualChildMotion { Motion = destValueTree, Threshold = 1 }
                    );
                    defaultBlendTree.Children = defaultBlendTree.Children.Add(
                        new VirtualBlendTree.VirtualChildMotion {
                            Motion = smoothRootTree,
                            DirectBlendParameter = GetTempParameter(ref parameters, tempParameters, 1F),
                        }
                    );
                } else {
                    destValueTree.Name = src;
                    defaultBlendTree.Children = defaultBlendTree.Children.Add(
                        new VirtualBlendTree.VirtualChildMotion {
                            Motion = destValueTree,
                            DirectBlendParameter = GetTempParameter(ref parameters, tempParameters, 1F),
                        }
                    );
                }
            }
            controller.Parameters = parameters;
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

        static string GetUniqueName(ref ImmutableDictionary<string, ACParameter> dictionary, string parameterName, float defaultValue) =>
            GetUniqueName(ref dictionary, new ACParameter {
                name = parameterName,
                type = ACParameterType.Float,
                defaultFloat = defaultValue,
            });

        static string GetUniqueName(ref ImmutableDictionary<string, ACParameter> dictionary, ACParameter parameter) {
            if (dictionary.ContainsKey(parameter.name))
                for (int i = 0; ; i++) {
                    var newName = $"{parameter.name}_{i}";
                    if (!dictionary.ContainsKey(newName)) {
                        parameter.name = newName;
                        break;
                    }
                }
            dictionary = dictionary.SetItem(parameter.name, parameter);
            return parameter.name;
        }

        string GetTempParameter(ref ImmutableDictionary<string, ACParameter> parameters, IDictionary<float, string> tempParameters, float constant) {
            if (!tempParameters.TryGetValue(constant, out var parameterName)) {
                parameterName = GetUniqueName(ref parameters, $"__AAP/const_{constant}", constant);
                tempParameters[constant] = parameterName;
            }
            return parameterName;
        }
    }
}