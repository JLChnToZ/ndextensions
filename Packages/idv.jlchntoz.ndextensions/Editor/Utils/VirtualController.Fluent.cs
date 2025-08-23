using System.Reflection;
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
    public static class VirtualControllerFluent {
        static readonly ConditionalWeakTable<VirtualTransitionBase, VirtualNode> transitionSources = new();
        static readonly FieldInfo blendTreeField = typeof(VirtualBlendTree).GetField("_tree", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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

        public static VirtualState AddState(this VirtualLayer layer, string name, VirtualMotion motion = null) =>
            layer.StateMachine.AddState(name, motion);

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
            var trans = CreateStateTransition();
            trans.SetDestination(to);
            from.Transitions = from.Transitions.Add(trans);
            transitionSources.Add(trans, from);
            return trans;
        }

        public static VirtualStateTransition ConnectTo(this VirtualState from, VirtualStateMachine to) {
            var trans = CreateStateTransition();
            trans.SetDestination(to);
            from.Transitions = from.Transitions.Add(trans);
            transitionSources.Add(trans, from);
            return trans;
        }

        public static VirtualStateTransition ConnectTo(this VirtualStateMachine from, VirtualState to) {
            var trans = CreateStateTransition();
            trans.SetDestination(to);
            from.AnyStateTransitions = from.AnyStateTransitions.Add(trans);
            transitionSources.Add(trans, from);
            return trans;
        }

        public static VirtualStateTransition ConnectTo(this VirtualStateMachine from, VirtualStateMachine to) {
            var trans = CreateStateTransition();
            trans.SetDestination(to);
            from.AnyStateTransitions = from.AnyStateTransitions.Add(trans);
            transitionSources.Add(trans, from);
            return trans;
        }

        public static VirtualStateTransition ConnectTo(this VirtualLayer from, VirtualState to) =>
            from.StateMachine.ConnectTo(to);

        public static VirtualStateTransition ConnectTo(this VirtualLayer from, VirtualStateMachine to) =>
            from.StateMachine.ConnectTo(to);

        public static VirtualStateTransition ConnectTo(this VirtualStateMachine from, VirtualLayer to) =>
            from.ConnectTo(to.StateMachine);

        public static VirtualTransition BeginWith(this VirtualStateMachine machine, VirtualState state) {
            var trans = VirtualTransition.Create();
            trans.SetDestination(state);
            machine.EntryTransitions = machine.EntryTransitions.Add(trans);
            machine.DefaultState ??= state;
            transitionSources.Add(trans, machine);
            return trans;
        }

        public static VirtualTransition BeginWith(this VirtualStateMachine machine, VirtualStateMachine state) {
            var trans = VirtualTransition.Create();
            trans.SetDestination(state);
            machine.EntryTransitions = machine.EntryTransitions.Add(trans);
            return trans;
        }

        public static VirtualTransition BeginWith(this VirtualLayer layer, VirtualState state) =>
            layer.StateMachine.BeginWith(state);

        public static VirtualTransition BeginWith(this VirtualLayer layer, VirtualStateMachine state) =>
            layer.StateMachine.BeginWith(state);

        public static VirtualStateTransition ExitTo(this VirtualState state) {
            var trans = CreateStateTransition();
            trans.SetExitDestination();
            state.Transitions = state.Transitions.Add(trans);
            return trans;
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

        static VirtualStateTransition CreateStateTransition() {
            var transition = VirtualStateTransition.Create();
            transition.Offset = 0;
            transition.Duration = 0;
            transition.ExitTime = null;
            transition.CanTransitionToSelf = false;
            return transition;
        }

        public static VirtualStateTransition When(this VirtualStateTransition transition, AnimatorConditionMode mode, string parameter, float value = 1) {
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

        public static VirtualStateTransition When(this VirtualStateTransition transition, AnimatorConditionMode mode, string parameter, bool value) =>
            transition.When(mode, parameter, value ? 1 : 0);

        public static VirtualTransition Or(this VirtualTransition transition) {
            var clone = transition.Clone() as VirtualTransition;
            clone.Conditions = ImmutableList<AnimatorCondition>.Empty;
            if (transitionSources.TryGetValue(transition, out var source) &&
                source is VirtualStateMachine sm) {
                sm.EntryTransitions = sm.EntryTransitions.Add(clone);
                transitionSources.Add(clone, source);
            }
            return clone;
        }

        public static VirtualStateTransition Or(this VirtualStateTransition transition) {
            var clone = transition.Clone() as VirtualStateTransition;
            clone.Conditions = ImmutableList<AnimatorCondition>.Empty;
            if (transitionSources.TryGetValue(transition, out var source)) {
                if (source is VirtualStateMachine sm)
                    sm.AnyStateTransitions = sm.AnyStateTransitions.Add(clone);
                else if (source is VirtualState s)
                    s.Transitions = s.Transitions.Add(clone);
                transitionSources.Add(clone, source);
            }
            return clone;
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
            using (var so = new SerializedObject((BlendTree)blendTreeField.GetValue(tree))) {
                so.FindProperty("m_NormalizedBlendValues").boolValue = normalize;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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
            tree.AddMotion(new Vector2(x, y), motion, timeScale, mirror);

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
            clip.SetFloatCurve(path, typeof(T), propertyName, AnimationCurve.Constant(0, Mathf.Max(duration, 1F / clip.FrameRate), value));
            return clip;
        }

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, string path, string propertyName,
            UnityObject value
        ) where T : UnityObject {
            clip.SetObjectCurve(
                EditorCurveBinding.PPtrCurve(path, typeof(T), propertyName),
                new[] { new ObjectReferenceKeyframe() { time = 0, value = value } }
            );
            return clip;
        }

        public static VirtualClip SetLinearClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            float startValue, float endValue, float duration = 0
        ) where T : Component => clip.SetLinearClip<T>(
            remapper.GetVirtualPathForObject(target.transform), propertyName, startValue, endValue, duration
        );

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            float value, float duration = 0
        ) where T : Component => clip.SetConstantClip<T>(
            remapper.GetVirtualPathForObject(target.transform), propertyName, value, duration
        );

        public static VirtualClip SetConstantClip(
            this VirtualClip clip, ObjectPathRemapper remapper, GameObject target, string propertyName,
            float value, float duration = 0
        ) => clip.SetConstantClip<GameObject>(
            remapper.GetVirtualPathForObject(target.transform), propertyName, value, duration
        );

        public static VirtualClip SetConstantClip<T>(
            this VirtualClip clip, ObjectPathRemapper remapper, T target, string propertyName,
            UnityObject value
        ) where T : Component => clip.SetConstantClip<T>(
            remapper.GetVirtualPathForObject(target.transform), propertyName, value
        );

        public static T WithBehaviour<T>(this VirtualState state) where T : StateMachineBehaviour {
            var behaviour = ScriptableObject.CreateInstance<T>();
            state.Behaviours = state.Behaviours.Add(behaviour);
            return behaviour;
        }

#if VRC_SDK_VRCSDK3
        public static VRCAvatarParameterDriver WithParameterChange(this VirtualState state) =>
            state.WithBehaviour<VRCAvatarParameterDriver>();

        public static VRCAvatarParameterDriver Set(this VRCAvatarParameterDriver driver, string name, float value) {
            driver.parameters.Add(new Parameter {
                type = ChangeType.Set,
                name = name,
                value = value,
            });
            return driver;
        }

        public static VRCAvatarParameterDriver Set(this VRCAvatarParameterDriver driver, string name, bool value) => driver.Set(name, value ? 1 : 0);

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
    }
}