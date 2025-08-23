using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;
using System;


#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.ndmf.util;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.vrchat;
using VRCParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
using VRCParameterType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType;
using VRCLayerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    public class ParameterCompressorPass : Pass<ParameterCompressorPass> {
        protected override void Execute(BuildContext context) {
            var allParameters = new HashSet<string>();
            foreach (var component in context.AvatarRootObject.GetComponentsInChildren<ParameterCompressor>(true)) {
                if (component == null || component.parameters == null) continue;
                foreach (var parameter in component.parameters) {
                    if (string.IsNullOrEmpty(parameter.name)) continue;
                    allParameters.Add(parameter.name);
                }
                UnityObject.DestroyImmediate(component);
            }
#if VRC_SDK_VRCSDK3
            if (allParameters.Count == 0) return;
            var ctx = new ProcessorContext(context);
            ctx.Prepare();
            foreach (var parameter in allParameters)
                ctx.ProcessParameter(parameter);
            ctx.FinalizeParameterConnections();
#endif
        }

#if VRC_SDK_VRCSDK3
        class ProcessorContext {
            readonly VRCExpressionParameters parametersObject;
            readonly List<VRCParameter> parameters;
            readonly AnimatorServicesContext asc;
            readonly VirtualAnimatorController fx;
            readonly AAPContext aap;
            readonly HashSet<string> processParameters = new();
            readonly List<VirtualState> allInputStates = new(), allOutputStates = new();
            bool isPrepared;
            string syncParamName, syncParamRefName;
            VirtualStateMachine syncLayerRoot;
            VirtualState rootInputState, rootOutputState;
            VirtualClip emptyDelayClip;
            int maxIndex;

            public ProcessorContext(BuildContext context) {
                asc = context.Extension<AnimatorServicesContext>();
                fx = asc.ControllerContext.Controllers[VRCLayerType.FX];
                aap = AAPContext.ForController(fx);
                var descriptor = context.VRChatAvatarDescriptor();
                parametersObject = descriptor.expressionParameters;
                var saver = context.AssetSaver;
                if (parametersObject == null) {
                    parametersObject = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                    saver.SaveAsset(parametersObject);
                    descriptor.expressionParameters = parametersObject;
                } else if (!saver.IsTemporaryAsset(parametersObject)) {
                    parametersObject = UnityObject.Instantiate(parametersObject);
                    saver.SaveAsset(parametersObject);
                    descriptor.expressionParameters = parametersObject;
                }
                parameters = new(parametersObject.parameters);
            }

            public string GetUniqueParameter(string name, VRCParameterType type, bool synced = true, float defaultValue = 0) {
                var param = fx.EnsureParameter(name, type switch {
                    VRCParameterType.Bool => AnimatorControllerParameterType.Bool,
                    VRCParameterType.Int => AnimatorControllerParameterType.Int,
                    _ => AnimatorControllerParameterType.Float,
                });
                parameters.Add(new VRCParameter {
                    name = param.name,
                    valueType = type,
                    networkSynced = synced,
                    defaultValue = defaultValue,
                });
                return param.name;
            }

            public void Prepare() {
                if (isPrepared) return;
                emptyDelayClip = VirtualClip.Create("Delay").SetConstantClip<GameObject>($"Dummy_{Guid.NewGuid()}", "enabled", 1, 0.1F);
                syncParamName = GetUniqueParameter("__CompParam/Value", VRCParameterType.Int);
                syncParamRefName = GetUniqueParameter("__CompParam/Ref", VRCParameterType.Int);
                syncLayerRoot = fx.AddLayer(LayerPriority.Default, "Parameter Sync").StateMachine;
                rootInputState = syncLayerRoot.AddState("Input Root", emptyDelayClip).WriteDefaults(aap.WriteDefaults);
                rootOutputState = syncLayerRoot.AddState("Output Root", emptyDelayClip).WriteDefaults(aap.WriteDefaults);
                syncLayerRoot.DefaultState = rootOutputState;
                var transition = rootOutputState.ConnectTo(rootInputState);
                var localParameter = fx.EnsureParameter("IsLocal", AnimatorControllerParameterType.Bool, false);
                switch (localParameter.type) {
                    case AnimatorControllerParameterType.Bool:
                        transition.When(AnimatorConditionMode.If, "IsLocal");
                        break;
                    case AnimatorControllerParameterType.Int:
                        transition.When(AnimatorConditionMode.NotEqual, "IsLocal", 0);
                        break;
                    case AnimatorControllerParameterType.Float:
                        transition.When(AnimatorConditionMode.Greater, "IsLocal", 0.0001F);
                        break;
                }
                isPrepared = true;
            }

            public void ProcessParameter(string name) {
                if (!processParameters.Add(name)) return;
                int index = -1;
                VRCParameter p = default;
                for (int i = 0; i < parameters.Count; i++) {
                    p = parameters[i];
                    if (p.name == name) {
                        index = i;
                        break;
                    }
                }
                if (index < 0 || !p.networkSynced) return;
                p.networkSynced = false;
                parameters[index] = p;
                fx.EnsureParameter(name, AnimatorControllerParameterType.Float, false);
                int uniqueIndex = ++maxIndex;

                var inputState = syncLayerRoot.AddState($"{name} Input", emptyDelayClip).WriteDefaults(aap.WriteDefaults);
                var inputModifier = inputState.WithParameterChange();
                switch (p.valueType) {
                    case VRCParameterType.Bool:
                    case VRCParameterType.Int:
                        inputModifier.Copy(syncParamName, name);
                        break;
                    case VRCParameterType.Float:
                        inputModifier.Copy(syncParamName, name, 0, 254, -1, 1);
                        break;
                }
                rootInputState.ConnectTo(inputState)
                    .When(AnimatorConditionMode.Equals, syncParamRefName, uniqueIndex);
                allInputStates.Add(inputState);

                var lastValue = aap.GetUniqueParameter($"{name}/__prev", p.defaultValue);
                var diffValue = aap.GetUniqueParameter($"{name}/__diff", p.defaultValue);
                aap.Subtract(name, lastValue, diffValue, -1, 1);

                var outputState = syncLayerRoot.AddState($"{name} Output", emptyDelayClip).WriteDefaults(aap.WriteDefaults);
                var outputModifier = outputState.WithParameterChange();
                switch (p.valueType) {
                    case VRCParameterType.Bool:
                    case VRCParameterType.Int:
                        outputModifier.Copy(name, syncParamName);
                        break;
                    case VRCParameterType.Float:
                        outputModifier.Copy(name, syncParamName, -1, 1, 0, 254);
                        break;
                }
                outputModifier
                    .Copy(name, lastValue)
                    .Set(syncParamRefName, uniqueIndex);
                rootOutputState.ConnectTo(outputState)
                    .When(AnimatorConditionMode.Less, diffValue, -0.0001F)
                    .Or()
                    .When(AnimatorConditionMode.Greater, diffValue, 0.0001F);
                allOutputStates.Add(outputState);
            }

            public void FinalizeParameterConnections() {
                for (int i = 0; i < allInputStates.Count; i++) {
                    var state = allInputStates[i];
                    state.Transitions = state.Transitions.AddRange(rootInputState.Transitions);
                }
                var outputTransitions = rootOutputState.Transitions.RemoveAll(t => t.DestinationState == rootInputState);
                for (int i = 0; i < allOutputStates.Count; i++) {
                    var state = allOutputStates[i];
                    state.Transitions = state.Transitions.AddRange(outputTransitions);
                    state.ConnectTo(allOutputStates[(i + 1) % allOutputStates.Count]).ExitTime(1);
                }
                rootOutputState.ConnectTo(allOutputStates[0]).ExitTime(1);
                asc.HarmonizeParameterTypes();
                parametersObject.parameters = parameters.ToArray();
            }
        }
#endif
    }
}
