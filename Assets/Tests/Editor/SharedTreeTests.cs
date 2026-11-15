using System.Collections.Generic;
using System.Linq;
using JLChnToZ.NDExtensions.Editors;
using JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Tests {
    public class SharedTreeTests {
        GameObject root;
        VirtualAnimatorController controller;
        CloneContext cloneContext;
        AAPContext aap;

        [SetUp]
        public void SetUp() {
            root = new GameObject("root");
            cloneContext = new CloneContext(GenericPlatformAnimatorBindings.Instance);
            controller = VirtualAnimatorController.Create(cloneContext);
            aap = AAPContext.ForController(new BuildContext(root, null, isClone: true), controller);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        void Compile(string source, int priority = 17) {
            using var program = Parser.Parse(source);
            var references = new Dictionary<string, Object> { ["this"] = root.transform };
            using var context = new CompileContext(controller, references, null, DummyClips.Instance, root.transform, false, aap, cloneContext);
            context.Compile(program, priority, "Patcher");
        }

        [Test]
        public void SingleTreesJoinExistingAapLayer() {
            var aapRoot = aap.RootBlendTree;
            var originalCount = controller.Layers.Count();
            var weight = aap.GetTempParameter(1);
            Compile("tree (speed) { 0: {} }");
            Compile("tree direct { weight: {} }");

            Assert.AreEqual(originalCount, controller.Layers.Count());
            Assert.AreEqual(2, aapRoot.Children.Count);
            Assert.AreEqual(weight, aapRoot.Children[0].DirectBlendParameter);
            Assert.AreEqual(weight, aapRoot.Children[1].DirectBlendParameter);
            Assert.AreEqual("speed", ((VirtualBlendTree)aapRoot.Children[0].Motion).BlendParameter);
            Assert.AreEqual("weight", ((VirtualBlendTree)aapRoot.Children[1].Motion).Children[0].DirectBlendParameter);
            Assert.AreEqual(1f, controller.Parameters[weight].defaultFloat);
        }

        [Test]
        public void RootTreeParametersAreRegisteredBeforeAllocatingSharedConstant() {
            Compile("tree (\"__AAP/Const_1\") { 0: {} }");
            var child = aap.RootBlendTree.Children[0];
            Assert.AreEqual("__AAP/Const_1", ((VirtualBlendTree)child.Motion).BlendParameter);
            Assert.AreNotEqual("__AAP/Const_1", child.DirectBlendParameter);
            Assert.AreEqual(0f, controller.Parameters["__AAP/Const_1"].defaultFloat);
            Assert.AreEqual(1f, controller.Parameters[child.DirectBlendParameter].defaultFloat);
        }

        [Test]
        public void CompositionReusesAapConstantWithoutCreatingAapLayer() {
            var weight = aap.GetTempParameter(1);
            Compile("tree (a) { 0: {} } tree (b) { 0: {} }");
            Assert.AreEqual(1, controller.Layers.Count());
            var tree = (VirtualBlendTree)controller.Layers.Single().StateMachine.DefaultState.Motion;
            Assert.AreEqual(2, tree.Children.Count);
            Assert.IsTrue(tree.Children.All(child => child.DirectBlendParameter == weight));
        }

        [TestCase("tree (p) { 0: {} } wait 1s; exit;")]
        [TestCase("tree (p) { 0: {} } set flag = true;")]
        [TestCase("tree (p) { 0: {} } this (Animator):m_Speed -> 1;")]
        [TestCase("this (Animator):m_Speed -> 1;")]
        public void OtherProgramsKeepTheirOwnLayer(string source) {
            Compile(source);
            Assert.AreEqual(1, controller.Layers.Count());
            Assert.AreEqual("Patcher", controller.Layers.Single().Name);
        }
    }
}
