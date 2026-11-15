using System.Linq;
using System.Text.RegularExpressions;
using JLChnToZ.NDExtensions.Editors;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Tests {
    public class AnimatorSimplePatcherProviderTests {
        GameObject root;
        AnimatorSimplePatcher patcher;

        [SetUp]
        public void SetUp() {
            root = new GameObject("root");
            patcher = root.AddComponent<AnimatorSimplePatcher>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void ReportsParametersFromNestedStatementsAndTrees() {
            patcher.code = @"
                set Flag = true;
                set Count = 1;
                set Count = 1.5;
                set Mixed = true;
                set Mixed = 1;
                set Destination(0, 1) = Source(-1, 1);
                if (Enabled && !(Count > 0 || Flag == false)) {
                    tree direct { Weight: tree (X, Y) { (0, 0): {} } }
                } else { set Other = random(0, 1); }";

            var parameters = new AnimatorSimplePatcherProvider(patcher).GetSuppliedParameters().ToDictionary(p => p.OriginalName);
            CollectionAssert.AreEquivalent(
                new[] { "Flag", "Count", "Mixed", "Destination", "Source", "Enabled", "Weight", "X", "Y", "Other" },
                parameters.Keys);
            Assert.AreEqual(AnimatorControllerParameterType.Bool, parameters["Flag"].ParameterType);
            Assert.AreEqual(AnimatorControllerParameterType.Float, parameters["Count"].ParameterType);
            Assert.AreEqual(AnimatorControllerParameterType.Int, parameters["Mixed"].ParameterType);
            Assert.AreEqual(AnimatorControllerParameterType.Float, parameters["Destination"].ParameterType);
            Assert.IsNull(parameters["Source"].ParameterType);
            Assert.IsNull(parameters["Enabled"].ParameterType);
            Assert.AreEqual(AnimatorControllerParameterType.Float, parameters["Weight"].ParameterType);
            Assert.IsTrue(parameters.Values.All(p => p.IsAnimatorOnly && !p.WantSynced && p.Source == patcher));
        }

        [Test]
        public void EmptyCodeReportsNoParameters() {
            Assert.IsEmpty(new AnimatorSimplePatcherProvider(patcher).GetSuppliedParameters());
        }

        [Test]
        public void ReportsNestedImportedTreeParametersAndPromotesTypes() {
            var clip = new AnimationClip { name = "clip" };
            var nested = new BlendTree { blendType = BlendTreeType.Simple1D, blendParameter = "Speed" };
            var tree = new BlendTree { name = "Imported", blendType = BlendTreeType.Direct };
            try {
                nested.children = new[] { new ChildMotion { motion = clip, timeScale = 1 } };
                tree.children = new[] { new ChildMotion { motion = nested, timeScale = 1, directBlendParameter = "Weight" } };
                patcher.extraReferences = new[] { new AnimatorSimplePatcher.ExtraReference { reference = tree } };
                patcher.code = "set Speed = true; tree (Other) { 0: import Imported; }";
                var parameters = new AnimatorSimplePatcherProvider(patcher).GetSuppliedParameters().ToDictionary(p => p.OriginalName);
                CollectionAssert.AreEquivalent(new[] { "Speed", "Weight", "Other" }, parameters.Keys);
                Assert.AreEqual(AnimatorControllerParameterType.Float, parameters["Speed"].ParameterType);
                Assert.AreEqual(AnimatorControllerParameterType.Float, parameters["Weight"].ParameterType);
            } finally {
                Object.DestroyImmediate(tree);
                Object.DestroyImmediate(nested);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void MalformedCodeReportsParseError() {
            patcher.code = "set Value =";
            LogAssert.Expect(LogType.Exception, new Regex("ParseException:.*Expected a number"));
            Assert.IsEmpty(new AnimatorSimplePatcherProvider(patcher).GetSuppliedParameters());
            Assert.IsInstanceOf<ParseException>(patcher.lastException);
            StringAssert.Contains("Expected a number", patcher.lastException.Message);
        }
    }
}
