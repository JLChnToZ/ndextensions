using JLChnToZ.NDExtensions.Editors;
using JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter;
using NUnit.Framework;

namespace JLChnToZ.NDExtensions.Tests {
    public class AnimatorSimplePatcherParserTests {
        [Test]
        public void ParsesAssignmentsRangesDurationsAndQuotedNames() {
            using var program = Parser.Parse(@"
                // This is a comment.
                set ""My Flag"" = true;
                set Count -= 2;
                set Destination(0, 1) = ""Source Name""(-1, 1) local;
                set RandomValue = random(-2, 3);
                wait 250ms;
                ""target/path"" (Animator):m_Speed -> -1.5e-1 -> 0 CubicOut 0.5s;");
            var statements = program.root.statements;
            Assert.AreEqual(6, statements.Count);
            var flag = (SetStatement)statements[0];
            Assert.AreEqual("My Flag", flag.parameter);
            Assert.AreEqual(ValueType.Boolean, flag.value.type);
            var decrement = (SetStatement)statements[1];
            Assert.AreEqual(SetOperation.Subtract, decrement.operation);
            Assert.AreEqual(ValueType.Integer, decrement.value.type);
            var copy = (SetStatement)statements[2];
            Assert.AreEqual(ValueType.Copy, copy.value.type);
            Assert.AreEqual("Source Name", copy.value.source);
            Assert.AreEqual(-1f, copy.value.min);
            Assert.AreEqual(1f, copy.max);
            Assert.IsTrue(copy.local);
            Assert.AreEqual(ValueType.Random, ((SetStatement)statements[3]).value.type);
            Assert.AreEqual(0.25f, ((WaitStatement)statements[4]).duration);
            var animation = (AnimationStatement)statements[5];
            Assert.AreEqual("target/path", animation.target);
            Assert.AreEqual(-0.15f, animation.keyframes[0].value, 0.00001f);
            Assert.AreEqual(2, animation.keyframes.Length);
            Assert.AreEqual(0.5f, animation.keyframes[1].time);
            Assert.AreEqual(EasingFactory.EasingType.CubicOut, animation.keyframes[1].easing);
        }

        [Test]
        public void ParsesChainedAnimationKeyframesWithRelativeAndAbsoluteTimes() {
            using var program = Parser.Parse(@"this (Animator):m_Speed -> 0 250ms -> 1 CubicOut @1s -> 0 500ms;");
            var animation = (AnimationStatement)program.root.statements[0];
            Assert.AreEqual(3, animation.keyframes.Length);
            Assert.AreEqual(0.25f, animation.keyframes[0].time);
            Assert.IsFalse(animation.keyframes[0].absoluteTime);
            Assert.AreEqual(1f, animation.keyframes[1].time);
            Assert.IsTrue(animation.keyframes[1].absoluteTime);
            Assert.AreEqual(EasingFactory.EasingType.CubicOut, animation.keyframes[1].easing);
            Assert.AreEqual(0.5f, animation.keyframes[2].time);
            Assert.IsFalse(animation.keyframes[2].absoluteTime);
        }

        [Test]
        public void ParsesChainedReferenceKeyframes() {
            using var program = Parser.Parse(@"this (Animator):m_Controller -> asset -> this @1s;");
            var animation = (AnimationStatement)program.root.statements[0];
            Assert.IsTrue(animation.IsPointerCurve);
            Assert.AreEqual("asset", animation.keyframes[0].refName);
            Assert.AreEqual("this", animation.keyframes[1].refName);
            Assert.AreEqual(1f, animation.keyframes[1].time);
        }

        [Test]
        public void ParsesConditionPrecedenceAndElseIf() {
            using var program = Parser.Parse(@"if (A || B && !C) exit; else if (D >= 2) goto end; else wait 1s;
                end:
                exit;");
            var conditional = (IfStatement)program.root.statements[0];
            var or = (BinaryExpression)conditional.condition;
            Assert.IsFalse(or.IsAnd);
            Assert.AreEqual("A", ((BooleanParameterExpression)or.left).parameter);
            var and = (BinaryExpression)or.right;
            Assert.IsTrue(and.IsAnd);
            Assert.AreEqual("B", ((BooleanParameterExpression)and.left).parameter);
            Assert.AreEqual("C", ((BooleanParameterExpression)((NotExpression)and.right).operand).parameter);
            var elseIf = (IfStatement)conditional.elseBlock.statements[0];
            Assert.AreEqual(ComparisonOperator.EqualsOrGreater, ((ComparisonExpression)elseIf.condition).op);
            Assert.IsInstanceOf<GotoStatement>(elseIf.thenBlock.statements[0]);
            Assert.IsInstanceOf<WaitStatement>(elseIf.elseBlock.statements[0]);
        }

        [Test]
        public void ParsesSameLineStatementAfterTerminatedLabel() {
            using var program = Parser.Parse("done:; exit;");

            Assert.IsInstanceOf<LabelStatement>(program.root.statements[0]);
            Assert.IsInstanceOf<ExitStatement>(program.root.statements[1]);
        }

        [Test]
        public void ParsesImportsAndPreservesImportAsTargetOrLabel() {
            using var program = Parser.Parse(@"IMPORT ""Walk Clip"";
                tree (speed) { 0: import walk; }
                import (Animator):m_Speed -> 1;
                import:; exit;");
            Assert.AreEqual("Walk Clip", ((ImportStatement)program.root.statements[0]).referenceName);
            Assert.AreEqual("walk", ((TreeStatement)program.root.statements[1]).children[0].import.referenceName);
            Assert.AreEqual("import", ((AnimationStatement)program.root.statements[2]).target);
            Assert.AreEqual("import", ((LabelStatement)program.root.statements[3]).name);
        }

        [TestCase("import;")]
        [TestCase("import walk")]
        [TestCase("tree (speed) { 0: import walk }")]
        [TestCase("tree (speed) { 0: { import walk; } }")]
        public void RejectsMalformedImports(string source) {
            Assert.Throws<ParseException>(() => Parser.Parse(source));
        }

        [TestCase("wait 0s;")]
        [TestCase("set P = random(2, 1);")]
        [TestCase("if (A > random(0, 1)) exit;")]
        [TestCase("if (A) { exit;")]
        [TestCase("this (Animator):m_Speed -> 1 CubicOut 0s;")]
        [TestCase("this (Animator):m_Speed -> 1 CubicOut 0.5s;")]
        [TestCase("this (Animator):m_Speed -> 1 Linear;")]
        [TestCase("this (Animator):m_Speed -> 0 @1s -> 1 @1s;")]
        [TestCase("this (Animator):m_Speed -> 0 -> 1;")]
        [TestCase("this (Animator):m_Speed -> 0 -> asset 1s;")]
        [TestCase("set P = Source(0, );")]
        public void RejectsInvalidSyntax(string source) {
            Assert.Throws<ParseException>(() => Parser.Parse(source));
        }
    }
}
