using System.Collections.Generic;
using System.Linq;
using JLChnToZ.NDExtensions.Editors;
using JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Tests {
    public class AnimatorSimplePatcherCompilerTests {
        GameObject root;
        VirtualAnimatorController controller;
        AAPContext aap;
        Dictionary<string, Object> references;

        [SetUp]
        public void SetUp() {
            root = new GameObject("root");
            controller = VirtualAnimatorController.Create(new CloneContext(GenericPlatformAnimatorBindings.Instance));
            aap = AAPContext.ForController(new BuildContext(root, null, isClone: true), controller);
            references = new Dictionary<string, Object> { ["this"] = root.transform, ["asset"] = root };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        VirtualLayer Compile(string source) {
            using var parser = new Parser(source);
            using var program = parser.Parse();
            using var context = new CompileContext(controller, references, null, DummyClips.Instance, root.transform, true, aap);
            context.Validate(program);
            context.Compile(program, 0, "test");
            return controller.Layers.Single();
        }

        [Test]
        public void CompilesAnimationCurvesAndWaitTiming() {
            var layer = Compile(@"this (Animator):m_Speed -> 0 -> 1 CubicOut 2s;
                this (Animator):m_Controller -> asset;
                wait 1s; exit;");
            var state = layer.StateMachine.DefaultState;
            var clip = (VirtualClip)state.Motion;
            var curve = clip.GetFloatCurve("", typeof(Animator), "m_Speed");
            Assert.AreEqual(0f, curve.Evaluate(0), 0.0001f);
            Assert.Greater(curve.Evaluate(1), 0f);
            Assert.Less(curve.Evaluate(1), 1f);
            Assert.AreEqual(1f, curve.Evaluate(2), 0.0001f);
            Assert.AreSame(root, clip.GetObjectCurve("", typeof(Animator), "m_Controller")[0].value);
            Assert.AreEqual(0.5f, state.Transitions.Single().ExitTime.Value, 0.0001f);
            Assert.IsTrue(state.Transitions.Single().DestinationState.Transitions.Single().IsExit);
        }

        [Test]
        public void DefaultsToConstantTangentsWithoutChangingExplicitLinear() {
            var layer = Compile(@"this (Animator):m_Speed -> 1;
                this (Animator):m_LinearVelocityBlending -> 0 -> 1 Linear 1s;
                wait 1s; exit;");
            var clip = (VirtualClip)layer.StateMachine.DefaultState.Motion;
            var constant = clip.GetFloatCurve("", typeof(Animator), "m_Speed");
            var linear = clip.GetFloatCurve("", typeof(Animator), "m_LinearVelocityBlending");
            Assert.IsTrue(float.IsPositiveInfinity(constant[0].outTangent));
            Assert.IsFalse(float.IsInfinity(linear[0].outTangent));
            Assert.AreEqual(1f, constant.Evaluate(0.5f));
        }

        [Test]
        public void CompilesIndependentAnimationTimelinesIntoOneClip() {
            var layer = Compile(@"this (Animator):m_Speed -> 0 -> 1 CubicOut 1s -> 0 1s;
                this (Animator):m_LinearVelocityBlending -> 2 @2s -> 3 1s;");
            var clip = (VirtualClip)layer.StateMachine.DefaultState.Motion;
            var speed = clip.GetFloatCurve("", typeof(Animator), "m_Speed");
            var scale = clip.GetFloatCurve("", typeof(Animator), "m_LinearVelocityBlending");
            Assert.AreEqual(0f, speed[0].time);
            Assert.AreEqual(1f, speed[1].time);
            Assert.AreEqual(2f, speed[2].time);
            Assert.IsTrue(float.IsPositiveInfinity(speed[1].outTangent));
            Assert.IsFalse(float.IsInfinity(speed[1].inTangent));
            Assert.AreEqual(2f, scale[0].time);
            Assert.AreEqual(3f, scale[1].time);
            Assert.AreEqual(2f, scale.Evaluate(2.5f), 0.0001f);
            Assert.AreEqual(3f, scale.Evaluate(3f), 0.0001f);
        }

        [Test]
        public void CompilesTimedObjectReferenceKeyframes() {
            var layer = Compile(@"this (Animator):m_Controller -> asset -> this @1s;");
            var clip = (VirtualClip)layer.StateMachine.DefaultState.Motion;
            var curve = clip.GetObjectCurve("", typeof(Animator), "m_Controller");
            Assert.AreEqual(2, curve.Length);
            Assert.AreEqual(0f, curve[0].time);
            Assert.AreSame(root, curve[0].value);
            Assert.AreEqual(1f, curve[1].time);
            Assert.AreSame(root.transform, curve[1].value);
        }

        [Test]
        public void DefaultConstantAfterEasingKeepsTheIncomingTangent() {
            var layer = Compile(@"this (Animator):m_Speed -> 0 -> 1 CubicOut 2s -> 1 1s;
                wait 3s; exit;");
            var curve = ((VirtualClip)layer.StateMachine.DefaultState.Motion)
                .GetFloatCurve("", typeof(Animator), "m_Speed");
            Assert.IsTrue(float.IsPositiveInfinity(curve[1].outTangent));
            Assert.IsFalse(float.IsInfinity(curve[1].inTangent));
            Assert.Greater(curve.Evaluate(1), 0f);
            Assert.Less(curve.Evaluate(1), 1f);
            Assert.AreEqual(1f, curve.Evaluate(2.5f));
        }

        [Test]
        public void CompilesBooleanBranchesIntoTransitionAlternatives() {
            var layer = Compile(@"this (Animator):m_Speed -> 1;
                if (A && !B) { this (Animator):m_Speed -> 2; exit; }
                else { this (Animator):m_Speed -> 3; exit; }");
            var transitions = layer.StateMachine.DefaultState.Transitions;
            Assert.AreEqual(3, transitions.Count);
            Assert.IsTrue(transitions.Any(t => t.Conditions.Count == 2 &&
                t.Conditions.Any(c => c.parameter == "A" && c.mode == AnimatorConditionMode.If) &&
                t.Conditions.Any(c => c.parameter == "B" && c.mode == AnimatorConditionMode.IfNot)));
            Assert.IsTrue(transitions.Any(t => t.Conditions.Count == 1 &&
                t.Conditions[0].parameter == "A" && t.Conditions[0].mode == AnimatorConditionMode.IfNot));
            Assert.IsTrue(transitions.Any(t => t.Conditions.Count == 1 &&
                t.Conditions[0].parameter == "B" && t.Conditions[0].mode == AnimatorConditionMode.If));
            Assert.AreEqual(AnimatorControllerParameterType.Bool, controller.Parameters["A"].type);
        }

        [Test]
        public void CompilesIntegerComparisonAndItsInverse() {
            var layer = Compile(@"this (Animator):m_Speed -> 1;
                if (Count >= 2) { this (Animator):m_Speed -> 2; exit; }
                else { this (Animator):m_Speed -> 3; exit; }");
            var conditions = layer.StateMachine.DefaultState.Transitions.Select(t => t.Conditions.Single()).ToArray();
            Assert.AreEqual(2, conditions.Length);
            Assert.IsTrue(conditions.Any(c => c.parameter == "Count" && c.mode == AnimatorConditionMode.Greater && c.threshold == 1));
            Assert.IsTrue(conditions.Any(c => c.parameter == "Count" && c.mode == AnimatorConditionMode.Less && c.threshold == 2));
            Assert.AreEqual(AnimatorControllerParameterType.Int, controller.Parameters["Count"].type);
        }

        [Test]
        public void CompilesFloatInclusiveComparisonAgainstIntegerLiteral() {
            controller.EnsureParameter("Speed", AnimatorControllerParameterType.Float);
            var layer = Compile(@"if (Speed <= 1) exit; else exit;");
            var conditions = layer.StateMachine.DefaultState.Transitions.SelectMany(t => t.Conditions).ToArray();

            Assert.IsTrue(conditions.Any(c => c.parameter == "Speed" &&
                c.mode == AnimatorConditionMode.Less && Mathf.Approximately(c.threshold, 1.0001f)));
            Assert.IsTrue(conditions.Any(c => c.parameter == "Speed" &&
                c.mode == AnimatorConditionMode.Greater && Mathf.Approximately(c.threshold, 1f)));
        }

        [Test]
        public void CompilesIntegerInclusiveComparisonAgainstDecimalLiteral() {
            controller.EnsureParameter("Count", AnimatorControllerParameterType.Int);
            var layer = Compile(@"if (Count <= 1.5) exit; else exit;");
            var conditions = layer.StateMachine.DefaultState.Transitions.SelectMany(t => t.Conditions).ToArray();

            Assert.IsTrue(conditions.Any(c => c.parameter == "Count" &&
                c.mode == AnimatorConditionMode.Less && Mathf.Approximately(c.threshold, 1.5001f)));
            Assert.IsTrue(conditions.Any(c => c.parameter == "Count" &&
                c.mode == AnimatorConditionMode.Greater && Mathf.Approximately(c.threshold, 1.5f)));
        }

        [Test]
        public void PromotesParameterTypesUsingFallbackOrder() {
            controller.EnsureParameter("Value", AnimatorControllerParameterType.Bool);
            var layer = Compile(@"if (Value == true && Value < 2 && Value < 2.5) exit; else exit;");
            var conditions = layer.StateMachine.DefaultState.Transitions.SelectMany(t => t.Conditions).ToArray();

            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["Value"].type);
            Assert.IsTrue(conditions.Any(c => c.parameter == "Value" &&
                c.mode == AnimatorConditionMode.Equals && c.threshold == 1));
        }

        [TestCase("&&", "Condition expands to too many conditions in one alternative.")]
        [TestCase("||", "Condition expands to too many transition alternatives.")]
        public void RejectsConditionsBeyondExpansionLimits(string op, string message) {
            var expression = string.Join($" {op} ", Enumerable.Range(0, 65).Select(i => $"P{i}"));
            var exception = Assert.Throws<ParseException>(() => Compile($"if ({expression}) exit;"));
            StringAssert.Contains(message, exception.Message);
        }

        [TestCase("&&", 1, 64)]
        [TestCase("||", 64, 1)]
        public void AllowsConditionExpansionAt64(string op, int transitionCount, int conditionsPerTransition) {
            var expression = string.Join($" {op} ", Enumerable.Range(0, 64).Select(i => $"P{i}"));
            var layer = Compile($"if ({expression}) exit;");
            var transitions = layer.StateMachine.DefaultState.Transitions;

            Assert.AreEqual(transitionCount, transitions.Count);
            Assert.IsTrue(transitions.All(t => t.Conditions.Count == conditionsPerTransition));
        }

        [Test]
        public void GotoResolvesForwardLabels() {
            var layer = Compile(@"goto next;
                unused:
                this (Animator):m_Speed -> 1; exit;
                next:
                this (Animator):m_Speed -> 2; exit;");
            var state = layer.StateMachine.DefaultState;
            Assert.AreEqual(2f, ((VirtualClip)state.Motion).GetFloatCurve("", typeof(Animator), "m_Speed").Evaluate(0));
            Assert.IsTrue(state.Transitions.Single().IsExit);
        }

        [TestCase("goto missing;")]
        [TestCase("duplicate:\nexit;\nduplicate:\nexit;")]
        public void RejectsInvalidLabels(string source) {
            Assert.Throws<ParseException>(() => Compile(source));
        }

#if VRC_SDK_VRCSDK3
        [Test]
        public void CompilesSharedAndLocalParameterDrivers() {
            var state = Compile(@"set Flag = true; set Count -= 2;
                set Destination(0, 1) = Source(-1, 1) local;
                set RandomValue = random(0, 1);").StateMachine.DefaultState;
            Assert.AreEqual(2, state.Behaviours.Count);
            var drivers = state.Behaviours.Select(b => new SerializedObject(b)).ToArray();
            var shared = drivers.Single(d => !d.FindProperty("localOnly").boolValue);
            var local = drivers.Single(d => d.FindProperty("localOnly").boolValue);
            var sharedParameters = shared.FindProperty("parameters");
            Assert.AreEqual(3, sharedParameters.arraySize);
            Assert.AreEqual("Flag", sharedParameters.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue);
            Assert.AreEqual(-2f, sharedParameters.GetArrayElementAtIndex(1).FindPropertyRelative("value").floatValue);
            var random = sharedParameters.GetArrayElementAtIndex(2);
            Assert.AreEqual("RandomValue", random.FindPropertyRelative("name").stringValue);
            Assert.AreEqual(0f, random.FindPropertyRelative("valueMin").floatValue);
            Assert.AreEqual(1f, random.FindPropertyRelative("valueMax").floatValue);
            var copy = local.FindProperty("parameters").GetArrayElementAtIndex(0);
            Assert.AreEqual("Destination", copy.FindPropertyRelative("name").stringValue);
            Assert.AreEqual("Source", copy.FindPropertyRelative("source").stringValue);
            Assert.IsTrue(copy.FindPropertyRelative("convertRange").boolValue);
            Assert.AreEqual(-1f, copy.FindPropertyRelative("sourceMin").floatValue);
            Assert.AreEqual(1f, copy.FindPropertyRelative("destMax").floatValue);
        }
#endif
    }
}
