using System;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using nadena.dev.ndmf.animator;

using UnityObject = UnityEngine.Object;
using ACParameter = UnityEngine.AnimatorControllerParameter;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;

#if VRC_SDK_VRCSDK3
using static VRC.SDKBase.VRC_AvatarParameterDriver;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    public enum RelativePosition {
        Left = 0x1,
        Right = 0x2,
        Up = 0x4,
        Down = 0x8,
        UpperLeft = Left | Up,
        UpperRight = Right | Up,
        LowerLeft = Left | Down,
        LowerRight = Right | Down,
    }
    public static class VirtualControllerFluent {
        public static bool DetermineWriteDefaults(this VirtualAnimatorController controller) {
            int wdOffCount = 0, wdOnCount = 0;
            foreach (var layer in controller.Layers) {
                if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive) continue;
                var stateMachine = layer.StateMachine;
                if (stateMachine.StateMachines.Count == 0 &&
                    stateMachine.States.Count == 1 &&
                    stateMachine.AnyStateTransitions.Count == 0) {
                    var defaultState = stateMachine.DefaultState;
                    if (defaultState != null &&
                        defaultState.Transitions.Count == 0 &&
                        defaultState.Motion is VirtualBlendTree)
                        continue;
                }
                foreach (var state in stateMachine.AllStates())
                    if (state.WriteDefaultValues)
                        wdOnCount++;
                    else
                        wdOffCount++;
            }
            return wdOnCount > wdOffCount;
        }

        public static ACParameter EnsureParameter(this VirtualAnimatorController controller, string parameterName, ACParameterType parameterType, bool preventDuplicate = true) {
            var acp = new ACParameter {
                name = parameterName,
                type = parameterType,
            };
            EnsureParameter(controller, ref acp, preventDuplicate);
            return acp;
        }

        public static void EnsureParameter(this VirtualAnimatorController controller, ref ACParameter parameter, bool preventDuplicate = true) {
            if (controller.Parameters.TryGetValue(parameter.name, out var existingParameter)) {
                if (preventDuplicate)
                    for (int i = 0; ; i++) {
                        var newName = $"{parameter.name}_{i}";
                        if (!controller.Parameters.ContainsKey(newName)) {
                            parameter.name = newName;
                            break;
                        }
                    }
                else {
                    float defaultValue = 0;
                    float existingDefaultValue = existingParameter.type switch {
                        ACParameterType.Float => existingParameter.defaultFloat,
                        ACParameterType.Int => existingParameter.defaultInt,
                        ACParameterType.Bool => existingParameter.defaultBool ? 1 : 0,
                        _ => 1,
                    };
                    switch (parameter.type) {
                        case ACParameterType.Float:
                            defaultValue = parameter.defaultFloat;
                            parameter.defaultFloat = 0;
                            break;
                        case ACParameterType.Int:
                            if (existingParameter.type == ACParameterType.Float)
                                parameter.type = ACParameterType.Float;
                            defaultValue = parameter.defaultInt;
                            parameter.defaultInt = 0;
                            break;
                        case ACParameterType.Bool:
                            defaultValue = parameter.defaultBool ? 1 : 0;
                            parameter.defaultBool = false;
                            parameter.type = existingParameter.type;
                            break;
                    }
                    if (defaultValue == 0) defaultValue = existingDefaultValue;
                    switch (parameter.type) {
                        case ACParameterType.Float: parameter.defaultFloat = defaultValue; break;
                        case ACParameterType.Int: parameter.defaultInt = (int)defaultValue; break;
                        case ACParameterType.Bool: parameter.defaultBool = defaultValue > 0; break;
                    }
                }
            }
            controller.Parameters = controller.Parameters.SetItem(parameter.name, parameter);
        }

        public static VirtualState AddState(this VirtualLayer layer, string name, VirtualMotion motion = null, Vector3? position = null) =>
            layer.StateMachine.AddState(name, motion, position);

        public static VirtualStateMachine AddStateMachine(this VirtualStateMachine parent, string name = "", Vector3? position = null) {
            var stateMachine = VirtualStateMachine.Create(null, name);
            parent.StateMachines = parent.StateMachines.Add(new VirtualStateMachine.VirtualChildStateMachine {
                StateMachine = stateMachine,
                Position = position ?? (new Vector3(10, 10) * (parent.StateMachines.Count + parent.States.Count)),
            });
            return stateMachine;
        }

        public static Vector3 GetRelativePosition(
            this VirtualStateMachine parent,
            VirtualState childState,
            RelativePosition relativePosition,
            float units = 1
        ) {
            foreach (var state in parent.States)
                if (state.State == childState)
                    return GetStateRelativePosition(state.Position, relativePosition, units);
            return Vector3.zero;
        }

        public static Vector3 GetRelativePosition(
            this VirtualStateMachine parent,
            VirtualStateMachine childStateMachine,
            RelativePosition relativePosition,
            float units = 1
        ) {
            foreach (var state in parent.StateMachines)
                if (state.StateMachine == childStateMachine)
                    return GetStateRelativePosition(state.Position, relativePosition, units);
            return Vector3.zero;
        }

        public static Vector3 GetRelativePosition(
            this VirtualStateMachine parent,
            VirtualState childState,
            Vector2 offset
        ) {
            foreach (var state in parent.States)
                if (state.State == childState)
                    return GetStateRelativePosition(state.Position, offset);
            return Vector3.zero;
        }

        public static Vector3 GetRelativePosition(
            this VirtualStateMachine parent,
            VirtualStateMachine childStateMachine,
            Vector2 offset
        ) {
            foreach (var state in parent.StateMachines)
                if (state.StateMachine == childStateMachine)
                    return GetStateRelativePosition(state.Position, offset);
            return Vector3.zero;
        }

        static Vector2 GetStateRelativePosition(Vector2 position, RelativePosition relativePosition, float units = 1) {
            var offsets = Vector2.zero;
            if ((relativePosition & RelativePosition.Left) != 0) offsets.x -= units;
            if ((relativePosition & RelativePosition.Right) != 0) offsets.x += units;
            if ((relativePosition & RelativePosition.Up) != 0) offsets.y += units;
            if ((relativePosition & RelativePosition.Down) != 0) offsets.y -= units;
            return GetStateRelativePosition(position, offsets);
        }

        static Vector2 GetStateRelativePosition(Vector2 position, Vector2 offset) => new(
            Mathf.Round(position.x * 0.1f + offset.x * 25f) * 10f,
            Mathf.Round(position.y * 0.1f + offset.y * 5f) * 10f
        );

        public static VirtualStateMachine AddStateMachine(this VirtualLayer layer, string name = "", Vector3? position = null) =>
            layer.StateMachine.AddStateMachine(name, position);

        public static VirtualState WriteDefaults(this VirtualState state, bool write = true) {
            state.WriteDefaultValues = write;
            return state;
        }

        public static VirtualState Speed(this VirtualState state, float speed = 1) {
            state.Speed = speed;
            return state;
        }

        public static VirtualState Speed(this VirtualState state, string speedParam) {
            state.SpeedParameter = speedParam;
            return state;
        }

        public static VirtualState CycleOffset(this VirtualState state, float offset = 0) {
            state.CycleOffset = offset;
            return state;
        }

        public static VirtualState CycleOffset(this VirtualState state, string offsetParam) {
            state.CycleOffsetParameter = offsetParam;
            return state;
        }

        public static VirtualState Mirror(this VirtualState state, bool mirror = true) {
            state.Mirror = mirror;
            return state;
        }

        public static VirtualState Mirror(this VirtualState state, string mirrorParam) {
            state.MirrorParameter = mirrorParam;
            return state;
        }

        public static VirtualState Time(this VirtualState state, string timeParam) {
            state.TimeParameter = timeParam;
            return state;
        }

        public static VirtualState IKOnFeet(this VirtualState state, bool ikOnFeet = true) {
            state.IKOnFeet = ikOnFeet;
            return state;
        }
        public static VirtualStateTransition ConnectTo(this VirtualState from, VirtualState to) {
            var trans = VirtualTransitionRef.From(null, from, TransitionType.StateTransition) as VirtualStateTransition;
            trans.SetDestination(to);
            return trans;
        }

        public static VirtualStateTransition ConnectTo(this VirtualState from, VirtualStateMachine to) {
            var trans = VirtualTransitionRef.From(null, from, TransitionType.StateTransition) as VirtualStateTransition;
            trans.SetDestination(to);
            return trans;
        }

        public static VirtualStateTransition AnyConnectTo(this VirtualStateMachine from, VirtualState to) {
            var trans = VirtualTransitionRef.From(null, from, TransitionType.AnyStateTransition) as VirtualStateTransition;
            trans.SetDestination(to);
            return trans;
        }

        public static VirtualTransition ConnectTo(this VirtualStateMachine from, VirtualState to, VirtualStateMachine parent) {
            var trans = VirtualTransitionRef.From(parent, from, TransitionType.StateTransition) as VirtualTransition;
            trans.SetDestination(to);
            return trans;
        }

        public static VirtualStateTransition AnyConnectTo(this VirtualStateMachine from, VirtualStateMachine to) {
            var trans = VirtualTransitionRef.From(null, from, TransitionType.AnyStateTransition) as VirtualStateTransition;
            trans.SetDestination(to);
            from.AnyStateTransitions = from.AnyStateTransitions.Add(trans);
            return trans;
        }

        public static VirtualTransition ConnectTo(this VirtualStateMachine from, VirtualStateMachine to, VirtualStateMachine parent) {
            var trans = VirtualTransitionRef.From(parent, from, TransitionType.StateTransition) as VirtualTransition;
            trans.SetDestination(to);
            return trans;
        }

        public static VirtualStateTransition AnyConnectTo(this VirtualLayer from, VirtualState to) =>
            AnyConnectTo(from.StateMachine, to);

        public static VirtualStateTransition AnyConnectTo(this VirtualLayer from, VirtualStateMachine to) =>
            AnyConnectTo(from.StateMachine, to);

        public static VirtualStateTransition AnyConnectTo(this VirtualStateMachine from, VirtualLayer to) =>
            AnyConnectTo(from, to.StateMachine);

        public static VirtualStateTransition ConnectTo(this VirtualState from, VirtualNode to) {
            if (to is VirtualState state) return ConnectTo(from, state);
            if (to is VirtualStateMachine sm) return ConnectTo(from, sm);
            if (to is VirtualLayer layer) return ConnectTo(from, layer.StateMachine);
            return null;
        }

        public static VirtualStateTransition AnyConnectTo(this VirtualStateMachine from, VirtualNode to) {
            if (to is VirtualState state) return AnyConnectTo(from, state);
            if (to is VirtualStateMachine sm) return AnyConnectTo(from, sm);
            if (to is VirtualLayer layer) return AnyConnectTo(from, layer.StateMachine);
            return null;
        }

        public static VirtualTransition ConnectTo(this VirtualStateMachine from, VirtualNode to, VirtualStateMachine parent) {
            if (to is VirtualState state) return ConnectTo(from, state, parent);
            if (to is VirtualStateMachine sm) return ConnectTo(from, sm, parent);
            if (to is VirtualLayer layer) return ConnectTo(from, layer, parent);
            return null;
        }

        public static VirtualTransitionBase ConnectTo(this VirtualLayer from, VirtualNode to, VirtualStateMachine parent) =>
            ConnectTo(from.StateMachine, to, parent);

        public static VirtualTransition ConnectTo(this VirtualStateMachine from, VirtualLayer to, VirtualStateMachine parent) =>
            ConnectTo(from, to.StateMachine, parent);

        public static VirtualTransition BeginWith(this VirtualStateMachine machine, VirtualState state) {
            var trans = VirtualTransitionRef.From(null, machine, TransitionType.EntryTransition) as VirtualTransition;
            trans.SetDestination(state);
            machine.DefaultState ??= state;
            return trans;
        }

        public static VirtualTransition BeginWith(this VirtualStateMachine machine, VirtualStateMachine state) {
            var trans = VirtualTransitionRef.From(null, machine, TransitionType.EntryTransition) as VirtualTransition;
            trans.SetDestination(state);
            return trans;
        }

        public static VirtualTransition BeginWith(this VirtualStateMachine machine, VirtualNode node) {
            if (node is VirtualState state) return BeginWith(machine, state);
            if (node is VirtualStateMachine sm) return BeginWith(machine, sm);
            if (node is VirtualLayer layer) return BeginWith(machine, layer);
            return null;
        }

        public static VirtualTransition BeginWith(this VirtualLayer layer, VirtualState state) =>
            BeginWith(layer.StateMachine, state);

        public static VirtualTransition BeginWith(this VirtualLayer layer, VirtualStateMachine state) =>
            BeginWith(layer.StateMachine, state);

        public static VirtualTransition BeginWith(this VirtualLayer layer, VirtualNode node) =>
            BeginWith(layer.StateMachine, node);

        public static VirtualStateTransition ExitTo(this VirtualState state) {
            var trans = VirtualTransitionRef.From(null, state, TransitionType.StateTransition) as VirtualStateTransition;
            trans.SetExitDestination();
            return trans;
        }

        public static VirtualTransition ExitTo(this VirtualStateMachine stateMachine, VirtualStateMachine parent) {
            var transition = VirtualTransitionRef.From(null, stateMachine, TransitionType.StateTransition) as VirtualTransition;
            transition.SetExitDestination();
            return transition;
        }

        public static void ExtractAnyStateToScoped(this VirtualStateMachine stateMachine) {
            var anyStateTransitions = stateMachine.AnyStateTransitions;
            if (anyStateTransitions.IsEmpty) return;
            stateMachine.AnyStateTransitions = ImmutableList<VirtualStateTransition>.Empty;
            foreach (var transition in anyStateTransitions) {
                foreach (var child in stateMachine.States)
                    if (transition.CanTransitionToSelf || transition.DestinationState != child.State)
                        child.State.Transitions = child.State.Transitions.Add(transition);
                var vt = transition.CopyAsTransition();
                stateMachine.EntryTransitions = stateMachine.EntryTransitions.Add(vt);
                foreach (var child in stateMachine.StateMachines) {
                    if (!stateMachine.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                        transitions = ImmutableList<VirtualTransition>.Empty;
                    if (transition.CanTransitionToSelf || transition.DestinationStateMachine != child.StateMachine)
                        transitions = transitions.Add(vt);
                    if (!transitions.IsEmpty)
                        stateMachine.StateMachineTransitions = stateMachine.StateMachineTransitions.SetItem(child.StateMachine, transitions);
                }
            }
        }

        public static VirtualStateTransition Solo(this VirtualStateTransition transition, bool solo = true) {
            transition.Solo = solo;
            return transition;
        }

        public static VirtualTransition Solo(this VirtualTransition transition, bool solo = true) {
            transition.Solo = solo;
            return transition;
        }

        public static VirtualStateTransition Mute(this VirtualStateTransition transition, bool mute = true) {
            transition.Mute = mute;
            return transition;
        }

        public static VirtualTransition Mute(this VirtualTransition transition, bool mute = true) {
            transition.Mute = mute;
            return transition;
        }

        public static T When<T>(this T transition, AnimatorConditionMode mode, string parameter, float value = 1) where T : VirtualTransitionBase {
            switch (mode) {
                case AnimatorConditionMode.If:
                    if (value <= 0) mode = AnimatorConditionMode.IfNot;
                    value = 0;
                    break;
                case AnimatorConditionMode.IfNot:
                    if (value <= 0) mode = AnimatorConditionMode.If;
                    value = 0;
                    break;
            }
            transition.Conditions = transition.Conditions.Add(new AnimatorCondition {
                mode = mode,
                parameter = parameter,
                threshold = value,
            });
            return transition;
        }

        public static T When<T>(this T transition, AnimatorConditionMode mode, string parameter, bool value) where T : VirtualTransitionBase =>
            When(transition, mode, parameter, value ? 1 : 0);

        public static T When<T>(
            this T transition,
            string parameterName,
            VirtualAnimatorController controller,
            bool isTrue = true
        ) where T : VirtualTransitionBase =>
            When(transition, EnsureParameter(controller, parameterName, ACParameterType.Bool, false), isTrue);

        public static T When<T>(this T transition, ACParameter parameter, bool isTrue = true) where T : VirtualTransitionBase => parameter.type switch {
            ACParameterType.Bool => When(transition, isTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, parameter.name),
            ACParameterType.Int => When(transition, isTrue ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.Equals, parameter.name, 0),
            ACParameterType.Float => When(transition, isTrue ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, parameter.name, 0.5f),
            _ => transition,
        };

        public static VirtualTransition Or(this VirtualTransition transition) {
            var clone = VirtualTransitionRef.Clone(transition) as VirtualTransition;
            clone.Conditions = ImmutableList<AnimatorCondition>.Empty;
            return clone;
        }

        public static VirtualStateTransition Or(this VirtualStateTransition transition) {
            var clone = VirtualTransitionRef.Clone(transition) as VirtualStateTransition;
            clone.Conditions = ImmutableList<AnimatorCondition>.Empty;
            return clone;
        }

        public static VirtualTransition CopyAsTransition(this VirtualTransitionBase transition) =>
            Copy(transition, VirtualTransition.Create());

        public static VirtualStateTransition CopyAsStateTransition(this VirtualTransitionBase transition) =>
            Copy(transition, VirtualStateTransition.Create());

        static T Copy<T>(VirtualTransitionBase source, T destination) where T : VirtualTransitionBase {
            if (source.DestinationState != null)
                destination.SetDestination(source.DestinationState);
            else if (source.DestinationStateMachine != null)
                destination.SetDestination(source.DestinationStateMachine);
            else if (source.IsExit)
                destination.SetExitDestination();
            destination.Conditions = source.Conditions;
            if (source.Solo) destination.Solo = true;
            if (source.Mute) destination.Mute = true;
            return destination;
        }

        public static VirtualStateTransition ExitTime(this VirtualStateTransition transition, float exitTime = 0) {
            transition.ExitTime = exitTime;
            return transition;
        }

        public static VirtualStateTransition Duration(this VirtualStateTransition transition, float duration, bool isFixed = true) {
            transition.HasFixedDuration = isFixed;
            transition.Duration = duration;
            return transition;
        }

        public static VirtualStateTransition Offset(this VirtualStateTransition transition, float offset = 0) {
            transition.Offset = offset;
            return transition;
        }

        public static VirtualStateTransition InterruptionSource(this VirtualStateTransition transition, TransitionInterruptionSource source) {
            transition.InterruptionSource = source;
            return transition;
        }

        public static VirtualStateTransition OrderedIntrruption(this VirtualStateTransition transition, bool ordered = true) {
            transition.OrderedInterruption = ordered;
            return transition;
        }

        public static VirtualStateTransition CanFromSelf(this VirtualStateTransition transition, bool can = true) {
            transition.CanTransitionToSelf = can;
            return transition;
        }

        public static VirtualBlendTree SetDirect(this VirtualBlendTree tree, bool normalize = false) {
            tree.BlendType = BlendTreeType.Direct;
            tree.NormalizedBlendValues = normalize;
            return tree;
        }

        public static VirtualBlendTree Set1D(this VirtualBlendTree tree, string parameter) {
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = parameter;
            return tree;
        }

        public static VirtualBlendTree Set2D(this VirtualBlendTree tree, string parameterX, string parameterY) {
            tree.BlendType = BlendTreeType.SimpleDirectional2D;
            tree.BlendParameter = parameterX;
            tree.BlendParameterY = parameterY;
            return tree;
        }

        public static VirtualBlendTree Set2DFreeform(this VirtualBlendTree tree, string parameterX, string parameterY) {
            tree.BlendType = BlendTreeType.FreeformDirectional2D;
            tree.BlendParameter = parameterX;
            tree.BlendParameterY = parameterY;
            return tree;
        }

        public static VirtualBlendTree Set2DCartesian(this VirtualBlendTree tree, string parameterX, string parameterY) {
            tree.BlendType = BlendTreeType.FreeformCartesian2D;
            tree.BlendParameter = parameterX;
            tree.BlendParameterY = parameterY;
            return tree;
        }

        public static VirtualBlendTree AddMotion(this VirtualBlendTree tree, VirtualMotion motion, float timeScale = 1, bool mirror = false) {
            tree.UseAutomaticThresholds = true;
            tree.Children = tree.Children.Add(new VirtualBlendTree.VirtualChildMotion {
                Motion = motion,
                TimeScale = timeScale,
                Mirror = mirror,
            });
            return tree;
        }

        public static VirtualBlendTree AddMotion(this VirtualBlendTree tree, float threshold, VirtualMotion motion, float timeScale = 1, bool mirror = false) {
            tree.UseAutomaticThresholds = false;
            tree.Children = tree.Children.Add(new VirtualBlendTree.VirtualChildMotion {
                Motion = motion,
                Threshold = threshold,
                TimeScale = timeScale,
                Mirror = mirror,
            });
            return tree;
        }

        public static VirtualBlendTree AddMotion(this VirtualBlendTree tree, Vector2 position, VirtualMotion motion, float timeScale = 1, bool mirror = false) {
            tree.Children = tree.Children.Add(new VirtualBlendTree.VirtualChildMotion {
                Motion = motion,
                Position = position,
                TimeScale = timeScale,
                Mirror = mirror,
            });
            return tree;
        }

        public static VirtualBlendTree AddMotion(this VirtualBlendTree tree, float x, float y, VirtualMotion motion, float timeScale = 1, bool mirror = false) =>
            AddMotion(tree, new Vector2(x, y), motion, timeScale, mirror);

        public static VirtualBlendTree AddMotion(this VirtualBlendTree tree, string parameter, VirtualMotion motion, float timeScale = 1, bool mirror = false) {
            tree.Children = tree.Children.Add(new VirtualBlendTree.VirtualChildMotion {
                Motion = motion,
                DirectBlendParameter = parameter,
                TimeScale = timeScale,
                Mirror = mirror,
            });
            return tree;
        }

        public static VirtualClip Loop(this VirtualClip clip, bool loop = true) {
            var settings = clip.Settings;
            settings.loopTime = loop;
            clip.Settings = settings;
            return clip;
        }

        public static VirtualClip SetConstantParameterDriver(
            this VirtualClip clip, string parameterName, float value, float duration = 0
        ) => SetConstantClip<Animator>(clip, "", parameterName, value, duration);

        public static VirtualClip SetLinearParameterDriver(
            this VirtualClip clip, string parameterName, float startValue, float endValue, float duration = 0
        ) => SetLinearClip<Animator>(clip, "", parameterName, startValue, endValue, duration);

        public static VirtualClip SetLinearClip<T>(
            this VirtualClip clip, string path, string propertyName,
            float startValue, float endValue, float duration = 0, float delay = 0
        ) where T : UnityObject {
            clip.SetFloatCurve(path, typeof(T), propertyName, AnimationCurve.Linear(
                Mathf.Max(delay, 0),
                startValue,
                Mathf.Max(delay + (duration > 0 ? duration : Mathf.Abs(endValue - startValue)), 1F / clip.FrameRate),
                endValue
            ));
            return clip;
        }

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, string path, string propertyName,
            float value, float duration = 0
        ) where T : UnityObject {
            clip.SetFloatCurve(path, typeof(T), propertyName, AnimationCurve.Constant(
                0,
                Mathf.Max(duration, clip.FrameRate > 0 ? 1F / clip.FrameRate : 0),
                value
            ));
            return clip;
        }

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, string path, string propertyName,
            UnityObject value
        ) where T : UnityObject {
            clip.SetObjectCurve(path, typeof(T), propertyName, new[] {
                new ObjectReferenceKeyframe() { time = 0, value = value },
            });
            return clip;
        }

        public static VirtualClip SetLinearClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            float startValue, float endValue, float duration = 0
        ) where T : Component => SetLinearClip<T>(
            clip, remapper.GetVirtualPathForObject(target.transform), propertyName, startValue, endValue, duration
        );

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            float value, float duration = 0
        ) where T : Component => SetConstantClip<T>(
            clip, remapper.GetVirtualPathForObject(target.transform), propertyName, value, duration
        );

        public static VirtualClip SetConstantClip(
            this VirtualClip clip, ObjectPathRemapper remapper, GameObject target, string propertyName,
            float value, float duration = 0
        ) => SetConstantClip<GameObject>(
            clip, remapper.GetVirtualPathForObject(target.transform), propertyName, value, duration
        );

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            UnityObject value
        ) where T : Component => SetConstantClip<T>(
            clip, remapper.GetVirtualPathForObject(target.transform), propertyName, value
        );

        public static T WithBehaviour<T>(this VirtualState state) where T : StateMachineBehaviour {
            var behaviour = ScriptableObject.CreateInstance<T>();
            state.Behaviours = state.Behaviours.Add(behaviour);
            return behaviour;
        }

#if VRC_SDK_VRCSDK3
        public static VRCAvatarParameterDriver WithParameterChange(this VirtualState state) =>
            WithBehaviour<VRCAvatarParameterDriver>(state);

        public static VRCAvatarParameterDriver Set(this VRCAvatarParameterDriver driver, string name, float value) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Set,
                name = name,
                value = value,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Set(this VRCAvatarParameterDriver driver, string name, bool value) => Set(driver, name, value ? 1 : 0);

        public static VRCAvatarParameterDriver Increment(this VRCAvatarParameterDriver driver, string name, float value) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Add,
                name = name,
                value = value,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Copy(this VRCAvatarParameterDriver driver, string source, string dest) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Copy,
                name = dest,
                source = source,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Copy(this VRCAvatarParameterDriver driver, string source, string dest, float srcMin, float srcMax, float destMin, float destMax) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Copy,
                name = dest,
                source = source,
                convertRange = true,
                sourceMin = srcMin,
                sourceMax = srcMax,
                destMin = destMin,
                destMax = destMax,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Random(this VRCAvatarParameterDriver driver, string name, float min, float max) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Random,
                name = name,
                valueMin = min,
                valueMax = max,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Random(this VRCAvatarParameterDriver driver, string name, float chance) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Random,
                name = name,
                chance = chance,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver LocalOnly(this VRCAvatarParameterDriver driver, bool local = true) {
            driver.localOnly = local;
            return driver;
        }

        public static VRCAvatarParameterDriver Debug(this VRCAvatarParameterDriver driver, string message) {
            driver.debugString = message;
            return driver;
        }
#endif

        sealed class VirtualTransitionRef {
            static readonly ConditionalWeakTable<VirtualTransitionBase, VirtualTransitionRef> refs = new();
            readonly VirtualNode source;
            readonly VirtualStateMachine parent;
            readonly TransitionType type;

            public static VirtualTransitionBase From(VirtualStateMachine parent, VirtualNode source, TransitionType type) {
                var trans = type switch {
                    TransitionType.AnyStateTransition => CreateStateTransition(),
                    TransitionType.EntryTransition => CreateTransition(),
                    _ => source is VirtualStateMachine ? CreateTransition() : CreateStateTransition(),
                };
                ConnectAndRecord(trans, source, parent, type);
                return trans;
            }

            static VirtualTransitionBase CreateTransition() => VirtualTransition.Create();

            static VirtualTransitionBase CreateStateTransition() {
                var trans = VirtualStateTransition.Create();
                trans.Offset = 0;
                trans.Duration = 0;
                trans.ExitTime = null;
                trans.CanTransitionToSelf = false;
                return trans;
            }

            public static VirtualTransitionBase Clone(VirtualTransitionBase transition) {
                var clone = transition.Clone();
                if (refs.TryGetValue(transition, out var entry))
                    ConnectAndRecord(clone, entry.source, entry.parent, entry.type);
                return clone;
            }

            static void ConnectAndRecord(VirtualTransitionBase transition, VirtualNode source, VirtualStateMachine parent, TransitionType type) {
                refs.AddOrUpdate(transition, new(parent, source, type));
                switch (type) {
                    case TransitionType.StateTransition:
                        if (source is VirtualState vs)
                            vs.Transitions = vs.Transitions.Add(transition as VirtualStateTransition);
                        if (source is VirtualStateMachine vsm) {
                            if (!parent.StateMachineTransitions.TryGetValue(vsm, out var transitions))
                                transitions = ImmutableList<VirtualTransition>.Empty;
                            transitions = transitions.Add(transition as VirtualTransition);
                            parent.StateMachineTransitions = parent.StateMachineTransitions.SetItem(vsm, transitions);
                        }
                        break;
                    case TransitionType.AnyStateTransition:
                        vsm = source as VirtualStateMachine;
                        vsm.AnyStateTransitions = vsm.AnyStateTransitions.Add(transition as VirtualStateTransition);
                        break;
                    case TransitionType.EntryTransition:
                        vsm = source as VirtualStateMachine;
                        vsm.EntryTransitions = vsm.EntryTransitions.Add(transition as VirtualTransition);
                        break;
                }
            }

            VirtualTransitionRef(VirtualStateMachine parent, VirtualNode source, TransitionType type) {
                this.parent = parent;
                this.source = source;
                this.type = type;
            }

            public override int GetHashCode() => HashCode.Combine(source, parent, type);

            public override bool Equals(object obj) =>
                obj is VirtualTransitionRef other &&
                other.source == source &&
                other.parent == parent &&
                other.type == type;
        }

        enum TransitionType : byte {
            StateTransition,
            AnyStateTransition,
            EntryTransition,
        }
    }
}