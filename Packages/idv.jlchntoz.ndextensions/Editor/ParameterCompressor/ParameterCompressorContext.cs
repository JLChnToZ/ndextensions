using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using UnityObject = UnityEngine.Object;
#if VRC_SDK_VRCSDK3
using System.Collections.Immutable;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.ndmf.util;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.vrchat;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;
using VRCParameter = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter;
using VRCParameterType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType;
using VRCLayerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    [DependsOnContext(typeof(AnimatorServicesContext))]
    public class ParameterCompressorContext : IExtensionContext {
        public static int CountRequiredParameterBits(int i, int b) {
            i += (b + 15) >> 3;
            i |= i >> 1;
            i |= i >> 2;
            i |= i >> 4;
            i |= i >> 8;
            i |= i >> 16;
            i = (i & 0x55555555) + ((i >> 1) & 0x55555555);
            i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
            i = (i & 0x0F0F0F0F) + ((i >> 4) & 0x0F0F0F0F);
            i = (i & 0x00FF00FF) + ((i >> 8) & 0x00FF00FF);
            i = (i & 0x0000FFFF) + ((i >> 16) & 0x0000FFFF);
            return i;
        }

        void IExtensionContext.OnDeactivate(BuildContext context) { }

#if VRC_SDK_VRCSDK3
        const float changeThreshold = 1F / 128F;
        readonly Dictionary<string, VRCParameter> parameterWillProcess = new();
        readonly List<VirtualNode> allSendStates = new();
        readonly List<string> boolParameters = new(8);
        VRCExpressionParameters parametersObject;
        List<VRCParameter> parameters;
        AnimatorServicesContext asc;
        DummyClips dummyClips;
        VirtualAnimatorController fx;
        AAPContext aap;
        string syncParamName;
        string[] syncParamRefNames;
        VirtualStateMachine syncLayerRoot, rootReceiverSM, rootSenderSM;
        VirtualClip emptyDelayClip;
        int indexCount, totalCount;

        static bool IsFlagOn(int flags, int bit) => (flags & (1 << bit)) != 0;

        static AnimatorConditionMode ToConditionMode(bool value, ACParameterType type) => type switch {
            ACParameterType.Bool => value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
            ACParameterType.Int => value ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.Equals,
            _ => value ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
        };

        public void OnActivate(BuildContext context) {
            asc = context.Extension<AnimatorServicesContext>();
            fx = asc.ControllerContext.Controllers[VRCLayerType.FX];
            aap = AAPContext.ForController(context, fx);
            dummyClips = DummyClips.For(context);
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
            parametersObject.parameters ??= Array.Empty<VRCParameter>();
            parameters = new(parametersObject.parameters.Length);
            foreach (var parameter in parametersObject.parameters)
                if (parameter != null) parameters.Add(parameter);
        }

        public void Init(int parameterCount, int boolsCount, float threshold) {
            emptyDelayClip = dummyClips.Get(threshold);
            syncParamName = GetUniqueParameter("__CompParam/Value", VRCParameterType.Int);
            totalCount = parameterCount + (boolsCount + 7) / 8;
            int requiredBits = CountRequiredParameterBits(parameterCount, boolsCount);
            syncParamRefNames = new string[requiredBits];
            for (int i = 0; i < requiredBits; i++)
                syncParamRefNames[i] = GetUniqueParameter($"__CompParam/Ref{i}", VRCParameterType.Bool);
            syncLayerRoot = fx.AddLayer(LayerPriority.Default, "Parameter Sync").StateMachine;
            var defaultState = syncLayerRoot.AddState("Default", emptyDelayClip, new(250, 0)).WriteDefaults(aap.WriteDefaults);
            syncLayerRoot.DefaultState = defaultState;
            rootReceiverSM = syncLayerRoot.AddStateMachine("Receiver", new(500, 0));
            rootReceiverSM.DefaultState = rootReceiverSM.AddState("Idle", emptyDelayClip, new(250, 0)).WriteDefaults(aap.WriteDefaults);
            rootSenderSM = syncLayerRoot.AddStateMachine("Sender", new(500, 100));
            rootSenderSM.DefaultState = rootSenderSM.AddState("Idle", emptyDelayClip, new(250, 0)).WriteDefaults(aap.WriteDefaults);
            defaultState
            .ConnectTo(rootSenderSM)
            .When("IsLocal", fx)
            .When("PreviewMode", fx, false);
            defaultState
            .ConnectTo(rootReceiverSM)
            .When("IsLocal", fx, false)
            .When("PreviewMode", fx, false);
        }

        string GetUniqueParameter(string name, VRCParameterType type, bool synced = true, float defaultValue = 0) {
            var param = fx.EnsureParameter(name, type switch {
                VRCParameterType.Bool => ACParameterType.Bool,
                VRCParameterType.Int => ACParameterType.Int,
                _ => ACParameterType.Float,
            });
            parameters.Add(new VRCParameter {
                name = param.name,
                valueType = type,
                networkSynced = synced,
                saved = false,
                defaultValue = defaultValue,
            });
            return param.name;
        }

        VRCParameter InternalPreprocessParameter(string name) {
            int index = -1;
            VRCParameter p = null;
            for (int i = 0; i < parameters.Count; i++) {
                p = parameters[i];
                if (p.name == name) {
                    index = i;
                    break;
                }
            }
            if (index < 0 || p == null || !p.networkSynced) return null;
            p.networkSynced = false;
            parameters[index] = p;
            return p;
        }

        public VRCParameter PreprocessParameter(string name) {
            if (!parameterWillProcess.TryGetValue(name, out var p)) {
                p = InternalPreprocessParameter(name);
                if (p != null) parameterWillProcess[name] = p;
            }
            return p;
        }

        public void ProcessParameter(string name) {
            if (!parameterWillProcess.TryGetValue(name, out var p)) return;
            if (p.valueType == VRCParameterType.Bool) {
                boolParameters.Add(p.name);
                if (boolParameters.Count >= 8) ProcessBoolParameterBank();
                return;
            }
            int uniqueIndex = ++indexCount;
            var receiverState = rootReceiverSM.AddState(p.name, emptyDelayClip, GetNodePosition()).WriteDefaults(aap.WriteDefaults);
            var receiveModifier = receiverState.WithParameterChange();
            switch (p.valueType) {
                case VRCParameterType.Int:
                    receiveModifier.Copy(syncParamName, p.name);
                    break;
                case VRCParameterType.Float:
                    receiveModifier.Copy(syncParamName, p.name, 0, 254, -1, 1);
                    break;
            }
            var receiveTransition = rootReceiverSM.AnyConnectTo(receiverState);
            for (int i = 0; i < syncParamRefNames.Length; i++)
                receiveTransition.When(ToConditionMode(IsFlagOn(uniqueIndex, i), ACParameterType.Bool), syncParamRefNames[i]);

            var sendState = rootSenderSM.AddState(p.name, emptyDelayClip, GetNodePosition()).WriteDefaults(aap.WriteDefaults);
            ConfigurateSubtraction(p.name, out var lastValue, out var diffValue);
            ConfigurateSendStateEnter(sendState, diffValue);
            var sendModifier = sendState.WithParameterChange();
            switch (p.valueType) {
                case VRCParameterType.Int:
                    sendModifier.Copy(p.name, syncParamName);
                    break;
                case VRCParameterType.Float:
                    sendModifier.Copy(p.name, syncParamName, -1, 1, 0, 254);
                    break;
            }
            sendModifier.Copy(p.name, lastValue);
            SetParamRef(sendModifier, uniqueIndex);
            allSendStates.Add(sendState);
        }

        void ProcessBoolParameterBank() {
            int count = boolParameters.Count;
            if (count == 0) return;
            int uniqueIndex = ++indexCount;
            int stateCount = 1 << count;
            float statesPerRow = Mathf.Ceil(Mathf.Sqrt(stateCount));
            var paramList = string.Join(", ", boolParameters);
            var bankReceiveSM = rootReceiverSM.AddStateMachine(paramList, GetNodePosition());
            (bankReceiveSM.DefaultState = bankReceiveSM
                .AddState("Default", emptyDelayClip, new(250, 100))
                .WriteDefaults(aap.WriteDefaults)
            ).ExitTo().ExitTime(0);
            for (int i = 0; i < stateCount; i++) {
                var state = bankReceiveSM.AddState(
                    i.ToString("X2"),
                    emptyDelayClip,
                    new((i % statesPerRow + 1) * 250, Mathf.Floor(i / statesPerRow + 2) * 100)
                ).WriteDefaults(aap.WriteDefaults);
                var transition = bankReceiveSM.BeginWith(state).When(AnimatorConditionMode.Equals, syncParamName, i);
                for (int j = 0; j < syncParamRefNames.Length; j++)
                    transition.When(ToConditionMode(IsFlagOn(uniqueIndex, j), ACParameterType.Bool), syncParamRefNames[j]);
                var modifier = state.WithParameterChange();
                for (int j = 0; j < count; j++)
                    modifier.Set(boolParameters[j], IsFlagOn(i, j));
                state.ExitTo().ExitTime(0);
            }
            var receiveTransition = rootReceiverSM.AnyConnectTo(bankReceiveSM);
            for (int i = 0; i < syncParamRefNames.Length; i++)
                receiveTransition.When(ToConditionMode(IsFlagOn(uniqueIndex, i), ACParameterType.Bool), syncParamRefNames[i]);

            var bankSendSM = rootSenderSM.AddStateMachine(paramList, GetNodePosition());
            (bankSendSM.DefaultState = bankSendSM
                .AddState("Default", emptyDelayClip, new(250, 100))
                .WriteDefaults(aap.WriteDefaults)
            ).ExitTo().ExitTime(0);
            var lastValues = new string[count];
            for (int i = 0; i < count; i++) {
                ConfigurateSubtraction(boolParameters[i], out lastValues[i], out var diffValue);
                ConfigurateSendStateEnter(bankSendSM, diffValue);
            }
            for (int i = 0; i < stateCount; i++) {
                var state = bankSendSM.AddState(
                    i.ToString("X2"),
                    emptyDelayClip,
                    new((i % statesPerRow + 1) * 250, Mathf.Floor(i / statesPerRow + 2) * 100)
                ).WriteDefaults(aap.WriteDefaults);
                var transition = bankSendSM.BeginWith(state);
                var modifier = state.WithParameterChange().Set(syncParamName, i);
                SetParamRef(modifier, uniqueIndex);
                for (int j = 0; j < count; j++) {
                    transition.When(ToConditionMode(IsFlagOn(i, j), ACParameterType.Float), boolParameters[j], 0.5F);
                    modifier.Copy(boolParameters[j], lastValues[j]);
                }
                state.ExitTo().ExitTime(1);
            }
            allSendStates.Add(bankSendSM);

            boolParameters.Clear();
        }

        void ConfigurateSubtraction(string parameterName, out string lastValue, out string diffValue) {
            fx.EnsureParameter(parameterName, ACParameterType.Float, false);
            lastValue = aap.GetUniqueParameter($"{parameterName}/__prev", 0);
            diffValue = aap.GetUniqueParameter($"{parameterName}/__diff", 0);
            aap.Subtract(parameterName, lastValue, diffValue, -1, 1);
        }

        Vector2 GetNodePosition() {
            float x = (float)indexCount / totalCount * Mathf.PI * 2;
            float y = totalCount * 100F;
            return new(Mathf.Cos(x) * y + y, Mathf.Sin(x) * y + y - 250);
        }

        void SetParamRef(VRCAvatarParameterDriver modifier, int index) {
            for (int i = 0; i < syncParamRefNames.Length; i++)
                modifier.Set(syncParamRefNames[i], IsFlagOn(index, i));
        }

        void ConfigurateSendStateEnter(VirtualNode sendState, string diffValue) =>
            rootSenderSM.DefaultState.ConnectTo(sendState)
                .When(AnimatorConditionMode.Less, diffValue, -changeThreshold)
                .Or()
                .When(AnimatorConditionMode.Greater, diffValue, changeThreshold);

        public void FinalizeParameterConnections() {
            ProcessBoolParameterBank();
            rootReceiverSM.ExtractAnyStateToScoped();
            var rootSenderStart = rootSenderSM.DefaultState;
            for (int i = 0; i < allSendStates.Count; i++) {
                int bi = -1;
                if (allSendStates[i] is VirtualState s) {
                    foreach (var transition in rootSenderStart.Transitions) {
                        var clone = transition.CopyAsStateTransition().ExitTime(1);
                        s.Transitions = bi < 0 ? s.Transitions.Add(clone) : s.Transitions.Insert(bi++, clone);
                        if (transition.DestinationState == s) bi = 0;
                    }
                    s.ConnectTo(allSendStates[(i + 1) % allSendStates.Count]).ExitTime(1);
                    continue;
                }
                if (allSendStates[i] is VirtualStateMachine sm) {
                    if (!rootSenderSM.StateMachineTransitions.TryGetValue(sm, out var transitions))
                        transitions = ImmutableList<VirtualTransition>.Empty;
                    foreach (var transition in rootSenderStart.Transitions) {
                        var clone = transition.CopyAsTransition();
                        transitions = bi < 0 ? transitions.Add(clone) : transitions.Insert(bi++, clone);
                        if (transition.DestinationStateMachine == sm) bi = 0;
                    }
                    if (!transitions.IsEmpty)
                        rootSenderSM.StateMachineTransitions = rootSenderSM.StateMachineTransitions.SetItem(sm, transitions);
                    sm.ConnectTo(allSendStates[(i + 1) % allSendStates.Count], rootSenderSM);
                    continue;
                }
            }
            if (allSendStates.Count > 0) rootSenderStart.ConnectTo(allSendStates[0]).ExitTime(1);
            asc.HarmonizeParameterTypes();
            parametersObject.parameters = parameters.ToArray();
        }
#else
        public void OnActivate(BuildContext context) { }

        public void Init(int parameterCount, float threshold = 0.1F) { }

        public object PreprocessParameter(string name) => null;

        public void ProcessParameter(string name) { }

        public void FinalizeParameterConnections() { }
#endif
    }
}