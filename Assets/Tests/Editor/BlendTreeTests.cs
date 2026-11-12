using System;
using System.Collections.Generic;
using System.Linq;
using JLChnToZ.NDExtensions.Editors;
using JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Tests {
    public class BlendTreeTests {
        GameObject root;
        VirtualAnimatorController controller;
        AAPContext aap;
        VirtualLayer layer;

        [SetUp]
        public void SetUp() {
            root = new GameObject("root");
            controller = VirtualAnimatorController.Create(new CloneContext(GenericPlatformAnimatorBindings.Instance));
            aap = AAPContext.ForController(new BuildContext(root, null, isClone: true), controller);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        void Compile(string source) {
            using var parser = new Parser(source);
            using var program = parser.Parse();
            var references = new Dictionary<string, Object> { ["this"] = root.transform };
            using var context = new CompileContext(controller, references, null, DummyClips.Instance, root.transform, true, aap);
            context.Validate(program);
            context.Compile(program, 0, "test");
            layer = controller.Layers.Single();
        }

        [Test]
        public void CompilesNestedTreesAndAnimationCurves() {
            Compile(@"tree direct {
                weight: tree (speed) {
                    -1: { this (Animator):m_Speed -> -1; }
                    1: { this (Animator):m_Speed -> 1; }
                }
                direction: tree (x, y) simple { (0, 1): {} }
                free: tree (x, y) { (-0.5, 1e-1): {} }
                cart: tree (x, y) cartesian { (1, -1): {} }
            }");
            var sharedRoot = (VirtualBlendTree)layer.StateMachine.DefaultState.Motion;
            var direct = (VirtualBlendTree)sharedRoot.Children.Single().Motion;
            Assert.AreEqual(BlendTreeType.Direct, direct.BlendType);
            Assert.IsFalse(direct.NormalizedBlendValues);
            Assert.AreEqual("weight", direct.Children[0].DirectBlendParameter);
            var one = (VirtualBlendTree)direct.Children[0].Motion;
            Assert.AreEqual(BlendTreeType.Simple1D, one.BlendType);
            Assert.IsFalse(one.UseAutomaticThresholds);
            Assert.AreEqual(-1f, one.Children[0].Threshold);
            Assert.AreEqual(1f, one.Children[1].Threshold);
            var clip = (VirtualClip)one.Children[0].Motion;
            Assert.AreEqual(-1f, clip.GetFloatCurve("", typeof(Animator), "m_Speed").Evaluate(0));
            var simple = (VirtualBlendTree)direct.Children[1].Motion;
            var free = (VirtualBlendTree)direct.Children[2].Motion;
            var cart = (VirtualBlendTree)direct.Children[3].Motion;
            Assert.AreEqual(BlendTreeType.SimpleDirectional2D, simple.BlendType);
            Assert.AreEqual(BlendTreeType.FreeformDirectional2D, free.BlendType);
            Assert.AreEqual(new Vector2(-0.5f, 0.1f), free.Children[0].Position);
            Assert.AreEqual(BlendTreeType.FreeformCartesian2D, cart.BlendType);
            Assert.AreEqual(new Vector2(1, -1), cart.Children[0].Position);
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["speed"].type);
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["weight"].type);
        }

        [Test]
        public void SupportsConstantAndDynamicDirectWeights() {
            Compile(@"tree direct {
                0.25: {}
                weight: {}
                0.25: {}
                -0.5: {}
            }");
            var sharedRoot = (VirtualBlendTree)layer.StateMachine.DefaultState.Motion;
            var direct = (VirtualBlendTree)sharedRoot.Children.Single().Motion;
            var constantWeight = direct.Children[0].DirectBlendParameter;
            Assert.AreEqual(aap.GetTempParameter(0.25f), constantWeight);
            Assert.AreEqual(0.25f, controller.Parameters[constantWeight].defaultFloat);
            Assert.AreEqual("weight", direct.Children[1].DirectBlendParameter);
            Assert.AreEqual(constantWeight, direct.Children[2].DirectBlendParameter);
            var negativeWeight = direct.Children[3].DirectBlendParameter;
            Assert.AreEqual(aap.GetTempParameter(-0.5f), negativeWeight);
            Assert.AreEqual(-0.5f, controller.Parameters[negativeWeight].defaultFloat);
        }

        [Test]
        public void NormalizesOnlyDirectTreesWithTheModifier() {
            Compile(@"tree direct normalized {
                normalizedChild: tree direct NORMALIZED { a: {} b: {} }
                unnormalizedChild: tree direct { c: {} }
            }");
            var sharedRoot = (VirtualBlendTree)layer.StateMachine.DefaultState.Motion;
            var direct = (VirtualBlendTree)sharedRoot.Children.Single().Motion;
            Assert.IsFalse(sharedRoot.NormalizedBlendValues);
            Assert.IsTrue(direct.NormalizedBlendValues);
            Assert.IsTrue(((VirtualBlendTree)direct.Children[0].Motion).NormalizedBlendValues);
            Assert.IsFalse(((VirtualBlendTree)direct.Children[1].Motion).NormalizedBlendValues);
            Assert.AreEqual("a", ((VirtualBlendTree)direct.Children[0].Motion).Children[0].DirectBlendParameter);
        }

        [Test]
        public void ComposesMotionsAndAvoidsParameterCollisions() {
            controller.EnsureParameter("__AAP/Const_1", AnimatorControllerParameterType.Float);
            Compile(@"tree (a) { 0: {} }
                this (Animator):m_Speed -> 1;
                tree (b) { 0: {} }");
            var wrapper = (VirtualBlendTree)layer.StateMachine.DefaultState.Motion;
            Assert.AreEqual(BlendTreeType.Direct, wrapper.BlendType);
            Assert.AreEqual(3, wrapper.Children.Count);
            var weight = wrapper.Children[0].DirectBlendParameter;
            Assert.AreNotEqual("__AAP/Const_1", weight);
            Assert.AreEqual(1f, controller.Parameters[weight].defaultFloat);
            foreach (var child in wrapper.Children) Assert.AreEqual(weight, child.DirectBlendParameter);
            Assert.AreEqual(1, controller.Layers.Count());
        }

        [Test]
        public void WaitUsesLongestDescendantDuration() {
            Compile(@"tree (speed) {
                0: { this (Animator):m_Speed -> 0 -> 1 CubicOut 2s; }
                1: {}
            }
            wait 1s; exit;");
            var state = layer.StateMachine.DefaultState;
            Assert.AreEqual(0.5f, state.Transitions[0].ExitTime.Value, 0.0001f);
        }

        [Test]
        public void RejectsMixedMotionsWhenProgramHasFlow() {
            Assert.Throws<ParseException>(() => Compile(
                "tree (p) { 0: {} } this (Animator):m_Speed -> 1; wait 1s;"));
        }

        [Test]
        public void AllowsTreesInSeparateFlowSegments() {
            Compile("tree (p) { 0: {} } wait 1s; tree (q) { 0: {} }");
            var first = layer.StateMachine.DefaultState;
            Assert.AreEqual("p", ((VirtualBlendTree)first.Motion).BlendParameter);
            Assert.AreEqual(1f, first.Transitions[0].ExitTime.Value);
        }

        [Test]
        public void PromotesExistingIntegerTreeParametersToFloat() {
            controller.EnsureParameter("p", AnimatorControllerParameterType.Int);
            Compile("tree (p) { 0: {} }");
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["p"].type);
        }

        [Test]
        public void PromotesBooleanTreeParametersToFloat() {
            controller.EnsureParameter("p", AnimatorControllerParameterType.Bool);
            Compile("tree (p) { 0: {} }");
            Assert.AreEqual(AnimatorControllerParameterType.Float, controller.Parameters["p"].type);
        }

        [Test]
        public void ResolvesReferencesInsideNestedTrees() {
            Assert.Throws<ParseException>(() => Compile(
                "tree direct { weight: tree (p) { 0: { this (Animator):m_Speed -> missing; } } }"));
        }

        [TestCase("tree (p) { nope: {} }")]
        [TestCase("tree (x, y) { (0): {} }")]
        [TestCase("tree (p) simple { 0: {} }")]
        [TestCase("tree direct { p: { wait 1s; } }")]
        [TestCase("tree (p) normalized { 0: {} }")]
        [TestCase("tree direct normalized normalized { p: {} }")]
        public void RejectsMalformedTrees(string source) {
            using var parser = new Parser(source);
            Assert.Throws<ParseException>(() => parser.Parse());
        }

        [Test]
        public void TreeCanStillBeAnAnimationTarget() {
            using var parser = new Parser("tree (Animator):m_Speed -> 1;");
            using var program = parser.Parse();
            Assert.IsInstanceOf<AnimationStatement>(program.root.statements[0]);
        }
    }
}
