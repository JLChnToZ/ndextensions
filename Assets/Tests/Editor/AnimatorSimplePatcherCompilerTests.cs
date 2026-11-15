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
        CloneContext cloneContext;
        AAPContext aap;
        Dictionary<string, Object> references;
        readonly List<Object> assets = new();

        [SetUp]
        public void SetUp() {
            root = new GameObject("root");
            cloneContext = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            controller = VirtualAnimatorController.Create(cloneContext);
            aap = AAPContext.ForController(new BuildContext(root, null, isClone: true), controller);
            references = new Dictionary<string, Object> { ["this"] = root.transform, ["asset"] = root };
        }

        [TearDown]
        public void TearDown() {
            foreach (var asset in assets) Object.DestroyImmediate(asset);
            assets.Clear();
            Object.DestroyImmediate(root);
        }

        VirtualLayer Compile(string source) {
            using var program = Parser.Parse(source);
            using var context = new CompileContext(controller, references, null, DummyClips.Instance, root.transform, true, aap, cloneContext);
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

        AnimationClip MakeClip(float length) {
            var clip = new AnimationClip { name = "source" };
            assets.Add(clip);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "m_Speed"),
                AnimationCurve.Linear(0, 0, length, 1));
            return clip;
        }

        BlendTree MakeTree(string parameter, Motion motion, float speed) {
            var tree = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = parameter };
            assets.Add(tree);
            tree.children = new[] { new ChildMotion { motion = motion, timeScale = speed } };
            return tree;
        }

        [Test]
        public void SingleImportUsesSharedTreeAndClonesSourceClip() {
            var clip = MakeClip(2);
            references["walk"] = clip;
            Compile("import walk;");
            var imported = (VirtualClip)aap.RootBlendTree.Children.Single().Motion;
            Assert.AreSame(cloneContext.Clone(clip), imported);
            Assert.AreEqual(1f, imported.GetFloatCurve("", typeof(Animator), "m_Speed").Evaluate(2), 0.0001f);
            Assert.AreEqual(2f, clip.length, 0.0001f);
        }

        [Test]
        public void SeparateCompilationsReuseSharedMotionClone() {
            var clip = MakeClip(2);
            references["walk"] = clip;
            Compile("import walk;");
            Compile("import walk;");
            var children = aap.RootBlendTree.Children;
            Assert.AreEqual(2, children.Count);
            Assert.AreSame(cloneContext.Clone(clip), children[0].Motion);
            Assert.AreSame(children[0].Motion, children[1].Motion);
        }

        [Test]
        public void ImportTreeChildRegistersParametersAndUsesNestedPlaybackSpeeds() {
            var clip = MakeClip(4);
            var inner = MakeTree("inner", clip, -2);
            var outer = MakeTree("outer", inner, 0.5f);
            references["motion"] = outer;
            controller.EnsureParameter("inner", AnimatorControllerParameterType.Bool);
            var state = Compile("tree (weight) { 0: import motion; } wait 2s; exit;").StateMachine.DefaultState;
            var tree = (VirtualBlendTree)state.Motion;
            Assert.AreSame(cloneContext.Clone((Motion)outer), tree.Children[0].Motion);
            Assert.AreEqual(0.5f, state.Transitions[0].ExitTime.Value, 0.0001f);
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["inner"].type);
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["outer"].type);
            Assert.AreEqual(4f, clip.length, 0.0001f);
        }

        [Test]
        public void WaitLongerThanImportedClipKeepsFullDuration() {
            references["walk"] = MakeClip(2);
            var state = Compile("import walk; wait 3s; exit;").StateMachine.DefaultState;
            Assert.AreEqual(1.5f, state.Transitions[0].ExitTime.Value, 0.0001f);
        }

        [Test]
        public void ComposesImportsWithAnimationWithoutFlow() {
            references["walk"] = MakeClip(2);
            var state = Compile("import walk; this (Animator):m_Speed -> 1;").StateMachine.DefaultState;
            var wrapper = (VirtualBlendTree)state.Motion;
            Assert.AreEqual(2, wrapper.Children.Count);
            Assert.IsTrue(wrapper.Children.Any(child => ReferenceEquals(child.Motion, cloneContext.Clone((Motion)references["walk"]))));
        }

        [TestCase("import walk; this (Animator):m_Speed -> 1; wait 1s;")]
        [TestCase("import walk; import walk; wait 1s;")]
        public void RejectsMixedImportedMotionsWithFlow(string source) {
            references["walk"] = MakeClip(2);
            Assert.Throws<ParseException>(() => Compile(source));
        }

        [TestCase("missing")]
        [TestCase("null")]
        [TestCase("asset")]
        public void RejectsMissingNullOrWrongMotionReferences(string name) {
            references["null"] = null;
            Assert.Throws<ParseException>(() => Compile($"import {name};"));
        }

        [Test]
        public void RejectsZeroDurationWithWaitBeforeCreatingLayer() {
            var empty = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "p" };
            assets.Add(empty);
            references["empty"] = empty;
            StringAssert.Contains("duration", Assert.Throws<ParseException>(() => Compile("import empty; wait 1s; exit;")).Message);
            Assert.IsEmpty(controller.Layers);
            Assert.IsFalse(controller.Parameters.ContainsKey("p"));
        }

        [Test]
        public void AllowsRepeatedImportedSubtrees() {
            var clip = MakeClip(1);
            var inner = MakeTree("inner", clip, 1);
            var outer = MakeTree("outer", inner, 1);
            outer.children = new[] {
                new ChildMotion { motion = inner, timeScale = 1 },
                new ChildMotion { motion = inner, timeScale = 1 },
            };
            references["tree"] = outer;
            Compile("import tree;");
            var imported = (VirtualBlendTree)aap.RootBlendTree.Children.Single().Motion;
            Assert.AreSame(imported.Children[0].Motion, imported.Children[1].Motion);
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
