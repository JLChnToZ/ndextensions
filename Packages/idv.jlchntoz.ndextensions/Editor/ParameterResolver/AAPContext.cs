using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using nadena.dev.ndmf.animator;
using ACParameter = UnityEngine.AnimatorControllerParameter;
using ACParameterType = UnityEngine.AnimatorControllerParameterType;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    // Ref: https://vrc.school/docs/Other/AAPs
    // Ref: https://vrc.school/docs/Other/Advanced-BlendTrees
    public class AAPContext {
        static readonly ConditionalWeakTable<VirtualAnimatorController, AAPContext> instances = new();
        static readonly ConditionalWeakTable<VirtualAnimatorController, BuildContext> buildContexts = new();
        readonly VirtualAnimatorController controller;
        readonly Dictionary<float, string> tempParameters = new();
        VirtualBlendTree rootBlendTree;
        VirtualClip dummyClip;
        string frameTimeParameter;
        bool? writeDefaults;

        public bool WriteDefaults => writeDefaults ??= controller.DetermineWriteDefaults();

        public VirtualBlendTree RootBlendTree {
            get {
                if (rootBlendTree == null) {
                    var layerStateMachine = controller.AddLayer(LayerPriority.Default, "AAP Drivers").StateMachine;
                    rootBlendTree = VirtualBlendTree.Create("AAP Drivers").SetDirect();
                    var defaultState = layerStateMachine.AddState("AAP Drivers (WD On)", rootBlendTree).WriteDefaults();
                    layerStateMachine.DefaultState = defaultState;
                }
                return rootBlendTree;
            }
        }

        public string FrameTimeParameter {
            get {
                if (string.IsNullOrEmpty(frameTimeParameter)) {
                    frameTimeParameter = GetUniqueParameter("__AAP/Time/Delta_Time", 0);
                    var lastTimeParameter = GetUniqueParameter("__AAP/Time/Last_Frame", 0);
                    var timeParameter = GetUniqueParameter("__AAP/Time/Current", 0);
                    var timeTrackerStateMachine = controller.AddLayer(LayerPriority.Default, "AAP Time Tracker").StateMachine;
                    var timeTrackerState = timeTrackerStateMachine.AddState("Time Tracker",
                        VirtualClip.Create("Time Tracker").SetLinearParameterDriver(timeParameter, 0, 43200).Loop()
                    ).WriteDefaults(WriteDefaults);
                    timeTrackerStateMachine.DefaultState = timeTrackerState;
                    RootBlendTree
                    .AddMotion(timeParameter, ParameterDriverClip(frameTimeParameter, 1))
                    .AddMotion(lastTimeParameter, ParameterDriverClip(frameTimeParameter, -1))
                    .AddMotion(timeParameter, ParameterDriverClip(lastTimeParameter, 1));
                }
                return frameTimeParameter;
            }
        }

        public static AAPContext ForController(BuildContext context, VirtualAnimatorController controller) {
            buildContexts.AddOrUpdate(controller, context);
            return instances.GetValue(controller, CreateInstance);
        }

        static AAPContext CreateInstance(VirtualAnimatorController controller) => new(controller);

        static VirtualClip ParameterDriverClip(string parameterName, float value) =>
            VirtualClip.Create($"{parameterName} = {value}").SetConstantParameterDriver(parameterName, value);

        AAPContext(VirtualAnimatorController controller) {
            this.controller = controller;
        }

        public void Add(string srcA, string srcB, string dest, float min, float max) {
            var destMin = ParameterDriverClip(dest, min);
            var destMax = ParameterDriverClip(dest, max);
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {srcA}").Set1D(srcA)
                .AddMotion(min, destMin)
                .AddMotion(max, destMax)
            ).AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {srcB}").Set1D(srcB)
                .AddMotion(min, destMin)
                .AddMotion(max, destMax)
            );
        }

        public void Subtract(string srcA, string srcB, string dest, float min, float max) {
            var destMin = ParameterDriverClip(dest, min);
            var destMax = ParameterDriverClip(dest, max);
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {srcA}").Set1D(srcA)
                .AddMotion(min, destMin)
                .AddMotion(max, destMax)
            ).AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = -{srcB}").Set1D(srcB)
                .AddMotion(min, destMax)
                .AddMotion(max, destMin)
            );
        }

        public void Multiply(string srcA, string srcB, string dest, float maxA, float maxB) {
            var destMin = ParameterDriverClip(dest, 0);
            var destMax = ParameterDriverClip(dest, maxA * maxB);
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {srcA} * {srcB}").Set1D(srcA)
                .AddMotion(0, destMin)
                .AddMotion(maxA, VirtualBlendTree.Create($"{dest} = {srcB}").Set1D(srcB)
                    .AddMotion(0, destMin)
                    .AddMotion(maxB, destMax)
                )
            );
        }

        public void Multiply(string srcA, float value, string dest) =>
            RootBlendTree.AddMotion(srcA, ParameterDriverClip(dest, value));

        public void Multiply(string srcA, string srcB, string dest) =>
            RootBlendTree.AddMotion(srcA, VirtualBlendTree.Create($"{dest} = {srcB}").SetDirect()
                .AddMotion(srcB, ParameterDriverClip(dest, 1))
            );

        public void OneDivideAddOne(string src, string dest) =>
            RootBlendTree.AddMotion(src, VirtualBlendTree.Create($"{dest} = 1 / (1 + {src})").SetDirect(true)
                .AddMotion(src, dummyClip ??= buildContexts.TryGetValue(controller, out var context) ? DummyClips.For(context).Get() : VirtualClip.Create("Dummy"))
                .AddMotion(GetTempParameter(1), ParameterDriverClip(dest, 1))
            );

        public void Remap(string src, string dest, float srcMin, float srcMax, float destMin, float destMax) =>
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {src}").Set1D(src)
                .AddMotion(srcMin, ParameterDriverClip(dest, destMin))
                .AddMotion(srcMax, ParameterDriverClip(dest, destMax))
            );

        public void LinearSmooth(string src, string dest, SmoothFactor smoothFactor, float min, float max) {
            var deltaParameter = GetUniqueParameter($"{dest}/AAP_Delta", 0);
            var rangeMax = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max));
            var rangeMin = -rangeMax;
            float minDelta, maxDelta;
            if (string.IsNullOrWhiteSpace(smoothFactor.propertyName)) {
                minDelta = Mathf.Max(-smoothFactor.constantValue, rangeMin);
                maxDelta = Mathf.Min(smoothFactor.constantValue, rangeMax);
            } else {
                minDelta = rangeMin;
                maxDelta = rangeMax;
            }
            var srcDeltaMin = ParameterDriverClip(deltaParameter, rangeMin);
            var srcDeltaMax = ParameterDriverClip(deltaParameter, rangeMax);
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{deltaParameter} = {src}").Set1D(src)
                .AddMotion(rangeMin, srcDeltaMin)
                .AddMotion(rangeMax, srcDeltaMax)
            ).AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{deltaParameter} = -{dest}").Set1D(dest)
                .AddMotion(rangeMin, srcDeltaMax)
                .AddMotion(rangeMax, srcDeltaMin)
            ).AddMotion(GetTempParameter(1), VirtualBlendTree.Create($"{dest} = {dest}").Set1D(dest)
                .AddMotion(min, ParameterDriverClip(dest, min))
                .AddMotion(max, ParameterDriverClip(dest, max))
            ).AddMotion(PrepareSmoothFactorParameter(smoothFactor), VirtualBlendTree.Create($"{dest} = {deltaParameter}").Set1D(deltaParameter)
                .AddMotion(minDelta, ParameterDriverClip(dest, minDelta))
                .AddMotion(0, ParameterDriverClip(dest, 0))
                .AddMotion(maxDelta, ParameterDriverClip(dest, maxDelta))
            );
        }

        public void ExponentialSmooth(string src, string dest, SmoothFactor smoothFactor, float min, float max, float? remapMin = null, float? remapMax = null) {
            VirtualClip destMin, destMax;
            string lerp;
            if (remapMin.HasValue && remapMax.HasValue) {
                lerp = GetUniqueParameter($"{dest}/AAP_Lerp", 0);
                destMin = ParameterDriverClip(dest, remapMin.Value).SetConstantParameterDriver(lerp, min);
                destMax = ParameterDriverClip(dest, remapMax.Value).SetConstantParameterDriver(lerp, max);
            } else {
                lerp = dest;
                destMin = ParameterDriverClip(dest, min);
                destMax = ParameterDriverClip(dest, max);
            }
            var smoothFactorParameter = PrepareSmoothFactorParameter(smoothFactor);
            RootBlendTree.AddMotion(GetTempParameter(1), VirtualBlendTree.Create(smoothFactorParameter).Set1D(smoothFactorParameter)
                .AddMotion(0, VirtualBlendTree.Create($"{dest} = {lerp}").Set1D(lerp)
                    .AddMotion(min, destMin)
                    .AddMotion(max, destMax)
                )
                .AddMotion(1, VirtualBlendTree.Create($"{dest} = {src}").Set1D(src)
                    .AddMotion(min, destMin)
                    .AddMotion(max, destMax)
                )
            );
        }

        public void EnsureParameter(ACParameter parameter) => controller.EnsureParameter(ref parameter, false);

        public string GetUniqueParameter(string parameterName, float defaultValue) =>
            GetUniqueParameter(new ACParameter {
                name = parameterName,
                type = ACParameterType.Float,
                defaultFloat = defaultValue,
            });

        public string GetUniqueParameter(ACParameter parameter) {
            controller.EnsureParameter(ref parameter);
            return parameter.name;
        }

        public string GetTempParameter(float constant) {
            if (!tempParameters.TryGetValue(constant, out var parameterName)) {
                parameterName = GetUniqueParameter($"__AAP/Const_{constant}", constant);
                tempParameters[constant] = parameterName;
            }
            return parameterName;
        }

        string PrepareSmoothFactorParameter(SmoothFactor smoothFactor) {
            if (!smoothFactor.timeBased)
                return string.IsNullOrWhiteSpace(smoothFactor.propertyName) ?
                    GetTempParameter(smoothFactor.constantValue) :
                    smoothFactor.propertyName;
            string parameterName;
            if (string.IsNullOrWhiteSpace(smoothFactor.propertyName)) {
                parameterName = GetUniqueParameter($"__AAP/Time/Delta_Time_Scaled_{smoothFactor.constantValue}", 0);
                Multiply(FrameTimeParameter, smoothFactor.constantValue, parameterName);
            } else {
                parameterName = GetUniqueParameter($"{smoothFactor.propertyName}/AAP_Time_Scaled", 0);
                Multiply(FrameTimeParameter, smoothFactor.propertyName, parameterName);
            }
            return parameterName;
        }
    }
}