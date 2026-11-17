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

        static VirtualStateTransition Connect(VirtualState from, VirtualState to) {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(to);
            from.Transitions = from.Transitions.Add(transition);
            return transition;
        }

        VirtualStateMachine Diamond() {
            var machine = VirtualStateMachine.Create(cloneContext);
            var end = machine.AddState("end");
            var right = machine.AddState("right");
            var left = machine.AddState("left");
            var start = machine.AddState("start");
            machine.DefaultState = start;
            Connect(start, left);
            Connect(start, right);
            Connect(left, end);
            Connect(right, end);
            return machine;
        }

        static bool SegmentHitsRectangle(Vector2 a, Vector2 b, Vector3 position) {
            float first = 0, last = 1;
            foreach (var axis in new[] {
                (a.x, b.x - a.x, position.x, position.x + 200),
                (a.y, b.y - a.y, position.y, position.y + 60),
            }) {
                var (origin, delta, lower, upper) = axis;
                if (Mathf.Abs(delta) < 0.0001f) {
                    if (origin < lower || origin > upper) return false;
                    continue;
                }
                float t1 = (lower - origin) / delta, t2 = (upper - origin) / delta;
                first = Mathf.Max(first, Mathf.Min(t1, t2));
                last = Mathf.Min(last, Mathf.Max(t1, t2));
                if (first > last) return false;
            }
            return true;
        }

        static int StateEdgeCollisions(VirtualStateMachine machine, Vector3[] positions) {
            int collisions = 0;
            var states = machine.States;
            for (int i = 0; i < states.Count; i++)
                foreach (var transition in states[i].State.Transitions) {
                    int target = -1;
                    for (int j = 0; j < states.Count; j++)
                        if (states[j].State == transition.DestinationState) target = j;
                    if (target < 0 || target == i) continue;
                    var a = (Vector2)positions[i] + new Vector2(100, 30);
                    var b = (Vector2)positions[target] + new Vector2(100, 30);
                    for (int j = 0; j < states.Count; j++)
                        if (j != i && j != target && SegmentHitsRectangle(a, b, positions[j]))
                            collisions++;
                }
            return collisions;
        }

        static float Side(Vector2 a, Vector2 b, Vector2 p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static int StateEdgeCrossings(VirtualStateMachine machine, Vector3[] positions) {
            var states = machine.States;
            var edges = new HashSet<(int from, int to)>();
            for (int i = 0; i < states.Count; i++)
                foreach (var transition in states[i].State.Transitions)
                    for (int j = 0; j < states.Count; j++)
                        if (transition.DestinationState == states[j].State) edges.Add((i, j));
            var list = edges.ToArray();
            int crossings = 0;
            for (int i = 0; i < list.Length; i++)
                for (int j = i + 1; j < list.Length; j++) {
                    var a = list[i];
                    var b = list[j];
                    if (a.from == b.from || a.from == b.to || a.to == b.from || a.to == b.to) continue;
                    var p = (Vector2)positions[a.from];
                    var q = (Vector2)positions[a.to];
                    var r = (Vector2)positions[b.from];
                    var s = (Vector2)positions[b.to];
                    if (Side(p, q, r) * Side(p, q, s) < 0 && Side(r, s, p) * Side(r, s, q) < 0)
                        crossings++;
                }
            return crossings;
        }

        static void AssertLayoutClear(VirtualStateMachine machine) {
            var positions = machine.States.Select(child => child.Position)
                .Concat(new[] { machine.EntryPosition, machine.ExitPosition, machine.AnyStatePosition }).ToArray();
            for (int i = 0; i < positions.Length; i++) {
                Assert.IsFalse(float.IsNaN(positions[i].x) || float.IsInfinity(positions[i].x));
                Assert.IsFalse(float.IsNaN(positions[i].y) || float.IsInfinity(positions[i].y));
                for (int j = 0; j < i; j++)
                    Assert.IsTrue(Mathf.Abs(positions[i].x - positions[j].x) >= 200 ||
                        Mathf.Abs(positions[i].y - positions[j].y) >= 60,
                        $"Nodes {i} and {j} overlap: {positions[i]}, {positions[j]}");
            }
        }

        [Test]
        public void LayoutSeparatesDiamondBranchesAndIsDeterministic() {
            var first = Diamond();
            var second = Diamond();
            var oldPositions = first.States.Select((_, i) => new Vector3((i + 1) * 180, 0, 0)).ToArray();
            int oldCollisions = StateEdgeCollisions(first, oldPositions);

            first.AutoLayout();
            second.AutoLayout();

            var positions = first.States.Select(child => child.Position).ToArray();
            Assert.Greater(oldCollisions, 0);
            Assert.Less(StateEdgeCollisions(first, positions), oldCollisions);
            Assert.AreEqual(0, StateEdgeCollisions(first, positions));
            CollectionAssert.AreEqual(positions, second.States.Select(child => child.Position).ToArray());
            Assert.AreEqual(first.EntryPosition, second.EntryPosition);
            Assert.AreEqual(first.ExitPosition, second.ExitPosition);
            Assert.AreEqual(first.AnyStatePosition, second.AnyStatePosition);
            AssertLayoutClear(first);
        }

        [Test]
        public void LayoutReleasesPooledCollectionsAfterFailure() {
            var invalid = Diamond();
            invalid.States = invalid.States.Add(invalid.States[0]);
            var pooled = UnityEngine.Pool.DictionaryPool<VirtualState, int>.Get();
            UnityEngine.Pool.DictionaryPool<VirtualState, int>.Release(pooled);
            bool failed = false;
            try {
                invalid.AutoLayout();
            } catch (System.ArgumentException) {
                failed = true;
            }
            Assert.IsTrue(failed);
            using (UnityEngine.Pool.DictionaryPool<VirtualState, int>.Get(out var reused)) {
                Assert.AreSame(pooled, reused);
                Assert.IsEmpty(reused);
            }

            var valid = Diamond();
            valid.AutoLayout();
            AssertLayoutClear(valid);
            var positions = new Vector3[valid.States.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = valid.States[i].Position;
            Assert.AreEqual(0, StateEdgeCollisions(valid, positions));
        }

        VirtualStateMachine CrossedBranches(bool duplicate) {
            var machine = VirtualStateMachine.Create(cloneContext);
            var start = machine.AddState("start");
            var left = machine.AddState("left");
            var right = machine.AddState("right");
            var upper = machine.AddState("upper");
            var lower = machine.AddState("lower");
            var end = machine.AddState("end");
            machine.DefaultState = start;
            Connect(start, left);
            Connect(start, right);
            Connect(left, lower);
            if (duplicate) Connect(left, lower);
            Connect(right, upper);
            Connect(upper, end);
            Connect(lower, end);
            return machine;
        }

        [Test]
        public void LayoutReducesCrossingsAndIgnoresDuplicateAlternatives() {
            var machine = CrossedBranches(false);
            var duplicate = CrossedBranches(true);
            var naive = new[] {
                new Vector3(0, 55), new Vector3(260, 0), new Vector3(260, 110),
                new Vector3(520, 0), new Vector3(520, 110), new Vector3(780, 55),
            };
            Assert.Greater(StateEdgeCrossings(machine, naive), 0);

            machine.AutoLayout();
            duplicate.AutoLayout();

            var positions = machine.States.Select(child => child.Position).ToArray();
            Assert.AreEqual(0, StateEdgeCrossings(machine, positions));
            CollectionAssert.AreEqual(positions, duplicate.States.Select(child => child.Position).ToArray());
            AssertLayoutClear(machine);
        }

        [TestCase(3, true, false)]
        [TestCase(4, true, false)]
        [TestCase(3, false, false)]
        [TestCase(3, true, true)]
        public void LayoutSeparatesStatesWhenLongTransitionsCrossThem(int count, bool loop, bool bidirectional) {
            var machine = VirtualStateMachine.Create(cloneContext);
            var states = new VirtualState[count];
            for (int i = 0; i < count; i++) states[i] = machine.AddState($"state {i}");
            machine.DefaultState = states[0];
            for (int i = 1; i < count; i++) Connect(states[i - 1], states[i]);
            if (loop) Connect(states[count - 1], states[0]);
            else Connect(states[0], states[count - 1]);
            if (bidirectional) {
                for (int i = 1; i < count; i++) Connect(states[i], states[i - 1]);
                Connect(states[0], states[count - 1]);
            }
            var straight = new Vector3[count];
            for (int i = 0; i < count; i++) straight[i] = new Vector3(i * 260, 0, 0);
            Assert.Greater(StateEdgeCollisions(machine, straight), 0);

            machine.AutoLayout();

            var positions = machine.States.Select(child => child.Position).ToArray();
            Assert.AreEqual(0, StateEdgeCollisions(machine, positions));
            AssertLayoutClear(machine);
            var transitions = states.SelectMany(state => state.Transitions).ToArray();
            machine.AutoLayout();
            CollectionAssert.AreEqual(positions, machine.States.Select(child => child.Position).ToArray());
            CollectionAssert.AreEqual(transitions, states.SelectMany(state => state.Transitions).ToArray());
            Assert.AreSame(states[0], machine.DefaultState);
        }

        [Test]
        public void LayoutChoosesDirectionFromGraphShape() {
            var linear = VirtualStateMachine.Create(cloneContext);
            var chain = Enumerable.Range(0, 8).Select(i => linear.AddState($"chain {i}")).ToArray();
            linear.DefaultState = chain[0];
            for (int i = 1; i < chain.Length; i++) Connect(chain[i - 1], chain[i]);
            linear.AutoLayout();
            var linearEntry = linear.States[0].Position;
            Assert.Less(linear.EntryPosition.y, linearEntry.y);
            Assert.AreEqual(linearEntry.x, linear.EntryPosition.x);
            Assert.IsTrue(linear.States.All(child => child.Position.x == linearEntry.x));

            var isolated = VirtualStateMachine.Create(cloneContext);
            var start = isolated.AddState("start");
            isolated.DefaultState = start;
            for (int i = 0; i < 12; i++) isolated.AddState($"isolated {i}");
            isolated.AutoLayout();
            var isolatedEntry = isolated.States[0].Position;
            Assert.Less(isolated.EntryPosition.x, isolatedEntry.x);
            Assert.AreEqual(isolatedEntry.y, isolated.EntryPosition.y);
            AssertLayoutClear(isolated);
        }

        [Test]
        public void LayoutPositionsSurviveControllerCommit() {
            var machine = Diamond();
            machine.AutoLayout();
            var layer = controller.AddLayer(new LayerPriority(0), "layout");
            layer.StateMachine = machine;
            var animator = root.AddComponent<Animator>();
            var build = new BuildContext(root, null, isClone: true);
            var service = new VirtualControllerContext();
            service.OnActivate(build);
            service.Controllers[animator] = controller;
            service.OnDeactivate(build);

            var committed = (AnimatorController)animator.runtimeAnimatorController;
            var actual = committed.layers.Single().stateMachine;
            CollectionAssert.AreEqual(machine.States.Select(child => child.Position).ToArray(),
                actual.states.Select(child => child.position).ToArray());
            Assert.AreEqual(machine.EntryPosition, actual.entryPosition);
            Assert.AreEqual(machine.ExitPosition, actual.exitPosition);
            Assert.AreEqual(machine.AnyStatePosition, actual.anyStatePosition);
            Assert.AreEqual(machine.DefaultState.Name, actual.defaultState.name);
            foreach (var child in actual.states) {
                assets.AddRange(child.state.transitions);
                assets.Add(child.state);
            }
            assets.Add(actual);
            assets.Add(committed);
        }

        [Test]
        public void LayoutHandlesCompiledLoopsBranchesAndUnreachableStates() {
            var machine = Compile(@"start:
                this (Animator):m_Speed -> 1;
                if (A && B) { this (Animator):m_Speed -> 2; wait 1s; goto start; }
                else { this (Animator):m_Speed -> 3; exit; }
                unused:
                this (Animator):m_Speed -> 4; exit;").StateMachine;
            var before = machine.States.Select(child => child.Position).ToArray();
            var transitions = machine.States.SelectMany(child => child.State.Transitions).ToArray();
            var defaultState = machine.DefaultState;

            Assert.IsTrue(transitions.Any(transition => transition.DestinationState == defaultState));
            Assert.IsTrue(transitions.Any(transition => transition.IsExit));
            Assert.IsTrue(transitions.Any(transition => transition.Conditions.Count > 1));
            AssertLayoutClear(machine);
            machine.AutoLayout();
            CollectionAssert.AreEqual(before, machine.States.Select(child => child.Position).ToArray());
            CollectionAssert.AreEqual(transitions, machine.States.SelectMany(child => child.State.Transitions).ToArray());
            Assert.AreSame(defaultState, machine.DefaultState);
        }

        [Test]
        public void LayoutHandlesSingleStateAndLargeCycles() {
            var single = Compile("this (Animator):m_Speed -> 1;").StateMachine;
            Assert.AreEqual(1, single.States.Count);
            AssertLayoutClear(single);

            var machine = VirtualStateMachine.Create(cloneContext);
            var states = Enumerable.Range(0, 300).Select(i => machine.AddState($"state {i}")).ToArray();
            machine.DefaultState = states[0];
            for (int i = 0; i < states.Length; i++) Connect(states[i], states[(i + 1) % states.Length]);
            Connect(states[0], states[1]);
            Connect(states[0], states[0]);
            var straight = new Vector3[states.Length];
            for (int i = 0; i < straight.Length; i++) straight[i] = new Vector3(0, i * 110, 0);
            int straightCollisions = StateEdgeCollisions(machine, straight);
            machine.AutoLayout();
            var positions = machine.States.Select(child => child.Position).ToArray();
            AssertLayoutClear(machine);
            Assert.Less(StateEdgeCollisions(machine, positions), straightCollisions);
            machine.AutoLayout();
            CollectionAssert.AreEqual(positions, machine.States.Select(child => child.Position).ToArray());
            Assert.AreEqual(3, states[0].Transitions.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LayoutProjectsNestedConnectionsAndOptionallyLayoutsContents(bool layoutContents) {
            var machine = VirtualStateMachine.Create(cloneContext);
            var start = machine.AddState("start");
            var end = machine.AddState("end");
            machine.DefaultState = start;
            var sub = machine.AddStateMachine("sub", new Vector3(-500, -500));
            var innerStart = sub.AddState("inner start", position: new Vector3(-300, -300));
            var nested = sub.AddStateMachine("nested", new Vector3(-400, -400));
            var nestedState = nested.AddState("nested state", position: new Vector3(-200, -200));
            sub.DefaultState = innerStart;
            nested.DefaultState = nestedState;
            Connect(start, nestedState);
            innerStart.ConnectTo(nested);
            sub.ConnectTo(end, machine);
            var originalInner = sub.States[0].Position;
            var originalNested = sub.StateMachines[0].Position;
            var originalNestedState = nested.States[0].Position;

            machine.AutoLayout(layoutContents);

            var startPosition = machine.States[0].Position;
            var subPosition = machine.StateMachines[0].Position;
            var endPosition = machine.States[1].Position;
            Assert.AreNotEqual(new Vector3(-500, -500), subPosition);
            Assert.IsTrue(startPosition.x < subPosition.x && subPosition.x < endPosition.x ||
                startPosition.y < subPosition.y && subPosition.y < endPosition.y);
            if (layoutContents) {
                Assert.AreNotEqual(originalInner, sub.States[0].Position);
                Assert.AreNotEqual(originalNested, sub.StateMachines[0].Position);
                Assert.AreNotEqual(originalNestedState, nested.States[0].Position);
            } else {
                Assert.AreEqual(originalInner, sub.States[0].Position);
                Assert.AreEqual(originalNested, sub.StateMachines[0].Position);
                Assert.AreEqual(originalNestedState, nested.States[0].Position);
            }
            var positions = machine.States.Select(child => child.Position).ToArray();
            machine.AutoLayout(layoutContents);
            CollectionAssert.AreEqual(positions, machine.States.Select(child => child.Position).ToArray());
            Assert.AreEqual(subPosition, machine.StateMachines[0].Position);
        }

        [Test]
        public void LayoutProjectsNestedSourcesAndStateMachineDestinations() {
            var machine = VirtualStateMachine.Create(cloneContext);
            var start = machine.AddState("start");
            machine.DefaultState = start;
            var first = machine.AddStateMachine("first");
            var second = machine.AddStateMachine("second");
            var nested = first.AddStateMachine("nested");
            var inner = nested.AddState("inner");
            start.ConnectTo(first);
            inner.ConnectTo(second);

            machine.AutoLayout();

            var a = machine.States[0].Position;
            var b = machine.StateMachines[0].Position;
            var c = machine.StateMachines[1].Position;
            Assert.IsTrue(a.x < b.x && b.x < c.x || a.y < b.y && b.y < c.y);
        }

        [Test]
        public void LayoutHandlesMachinesWithoutDirectStates() {
            var machine = VirtualStateMachine.Create(cloneContext);
            var first = machine.AddStateMachine("first", new Vector3(-100, -100));
            var second = machine.AddStateMachine("second", new Vector3(-200, -200));
            second.ConnectTo(first, machine);
            var entry = VirtualTransition.Create();
            entry.SetDestination(second);
            machine.EntryTransitions = machine.EntryTransitions.Add(entry);

            machine.AutoLayout(true);

            var a = machine.StateMachines[0].Position;
            var b = machine.StateMachines[1].Position;
            Assert.AreNotEqual(new Vector3(-100, -100), a);
            Assert.AreNotEqual(new Vector3(-200, -200), b);
            Assert.IsTrue(b.x < a.x || b.y < a.y);
            Assert.IsTrue(machine.EntryPosition.x < b.x && machine.EntryPosition.y == b.y ||
                machine.EntryPosition.y < b.y && machine.EntryPosition.x == b.x);
            Assert.AreEqual(entry, machine.EntryTransitions.Single());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LayoutProjectsTransitionsAcrossEveryParentBoundary(bool incoming) {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var outside = rootMachine.AddState("outside");
            rootMachine.DefaultState = outside;
            var child = rootMachine.AddStateMachine("child");
            var nested = child.AddStateMachine("nested");
            var inside = nested.AddState("inside");
            nested.DefaultState = inside;
            var originalParentPosition = new Vector3(-600, -600);
            child.ParentStateMachinePosition = originalParentPosition;
            nested.ParentStateMachinePosition = originalParentPosition;
            if (incoming) Connect(outside, inside);
            else Connect(inside, outside);

            rootMachine.AutoLayout(true);

            var rootPosition = rootMachine.States[0].Position;
            var childPosition = rootMachine.StateMachines[0].Position;
            var nestedPosition = child.StateMachines[0].Position;
            var statePosition = nested.States[0].Position;
            Assert.AreNotEqual(originalParentPosition, child.ParentStateMachinePosition);
            Assert.AreNotEqual(originalParentPosition, nested.ParentStateMachinePosition);
            if (incoming) {
                AssertLayoutBefore(rootPosition, childPosition);
                AssertLayoutBefore(child.ParentStateMachinePosition, nestedPosition);
                AssertLayoutBefore(nested.ParentStateMachinePosition, statePosition);
            } else {
                AssertLayoutBefore(childPosition, rootPosition);
                AssertLayoutBefore(nestedPosition, child.ParentStateMachinePosition);
                AssertLayoutBefore(statePosition, nested.ParentStateMachinePosition);
            }
        }

        static void AssertLayoutBefore(Vector3 source, Vector3 target) =>
            Assert.IsTrue(source.x < target.x || source.y < target.y,
                $"Expected {source} before {target}.");

        [Test]
        public void LayoutProjectsParentStateMachineTransitionsIntoChildren() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var source = rootMachine.AddStateMachine("source");
            var child = rootMachine.AddStateMachine("child");
            var inner = child.AddState("inner");
            child.DefaultState = inner;
            var originalParentPosition = new Vector3(-600, -600);
            child.ParentStateMachinePosition = originalParentPosition;
            source.ConnectTo(inner, rootMachine);

            rootMachine.AutoLayout(true);

            AssertLayoutBefore(rootMachine.StateMachines[0].Position, rootMachine.StateMachines[1].Position);
            Assert.AreNotEqual(originalParentPosition, child.ParentStateMachinePosition);
            AssertLayoutBefore(child.ParentStateMachinePosition, child.States[0].Position);
        }

        [Test]
        public void LayoutProjectsChildMachineTransitionsToAnAncestor() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var outside = rootMachine.AddState("outside");
            rootMachine.DefaultState = outside;
            var child = rootMachine.AddStateMachine("child");
            var nested = child.AddStateMachine("nested");
            var originalParentPosition = new Vector3(-600, -600);
            child.ParentStateMachinePosition = originalParentPosition;
            nested.ConnectTo(outside, child);

            rootMachine.AutoLayout(true);

            AssertLayoutBefore(rootMachine.StateMachines[0].Position, rootMachine.States[0].Position);
            Assert.AreNotEqual(originalParentPosition, child.ParentStateMachinePosition);
            AssertLayoutBefore(child.StateMachines[0].Position, child.ParentStateMachinePosition);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void LayoutProjectsConnectionsBetweenDeepSiblingBranches(bool reverse, bool machineSource) {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var common = rootMachine.AddStateMachine("common");
            var left = new VirtualStateMachine[4];
            var right = new VirtualStateMachine[4];
            var originalParentPosition = new Vector3(-600, -600);
            common.ParentStateMachinePosition = originalParentPosition;
            left[0] = common.AddStateMachine("left");
            right[0] = common.AddStateMachine("right");
            for (int i = 0; i < left.Length; i++) {
                left[i].ParentStateMachinePosition = originalParentPosition;
                right[i].ParentStateMachinePosition = originalParentPosition;
                if (i + 1 == left.Length) continue;
                left[i + 1] = left[i].AddStateMachine($"left {i}");
                right[i + 1] = right[i].AddStateMachine($"right {i}");
            }
            var source = reverse ? right : left;
            var target = reverse ? left : right;
            var targetState = target[3].AddState("target");
            target[3].DefaultState = targetState;
            VirtualState sourceState = null;
            if (machineSource)
                source[3].ConnectTo(targetState, source[2]);
            else {
                sourceState = source[3].AddState("source");
                source[3].DefaultState = sourceState;
                Connect(sourceState, targetState);
            }

            rootMachine.AutoLayout(true);

            var sourceNode = common.StateMachines[reverse ? 1 : 0].Position;
            var targetNode = common.StateMachines[reverse ? 0 : 1].Position;
            AssertRankBefore(common, sourceNode, targetNode);
            Assert.AreEqual(originalParentPosition, common.ParentStateMachinePosition);
            for (int i = 0; i < source.Length - 1; i++) {
                Assert.AreNotEqual(originalParentPosition, source[i].ParentStateMachinePosition);
                AssertRankBefore(source[i], source[i].StateMachines[0].Position, source[i].ParentStateMachinePosition);
                Assert.AreNotEqual(originalParentPosition, target[i].ParentStateMachinePosition);
                AssertRankBefore(target[i], target[i].ParentStateMachinePosition, target[i].StateMachines[0].Position);
            }
            Assert.AreNotEqual(originalParentPosition, target[3].ParentStateMachinePosition);
            AssertRankBefore(target[3], target[3].ParentStateMachinePosition, target[3].States[0].Position);
            if (machineSource)
                Assert.AreEqual(originalParentPosition, source[3].ParentStateMachinePosition);
            else {
                Assert.AreSame(sourceState, source[3].DefaultState);
                AssertRankBefore(source[3], source[3].States[0].Position, source[3].ParentStateMachinePosition);
            }
        }

        static void AssertRankBefore(VirtualStateMachine machine, Vector3 source, Vector3 target) {
            if (machine.EntryPosition.x == 0)
                Assert.Less(source.x, target.x);
            else
                Assert.Less(source.y, target.y);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LayoutProjectsAncestorStateToNonDefaultDescendantState(bool nested) {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var ancestor = rootMachine.AddStateMachine("ancestor", new Vector3(-500, -500));
            var source = ancestor.AddState("source", position: new Vector3(-400, -400));
            ancestor.DefaultState = source;
            var child = ancestor.AddStateMachine("child", new Vector3(-300, -300));
            var grandchild = child.AddStateMachine("grandchild", new Vector3(-200, -200));
            var defaultState = grandchild.AddState("default", position: new Vector3(-100, -100));
            var target = grandchild.AddState("target", position: new Vector3(-150, -150));
            grandchild.DefaultState = defaultState;
            var transition = Connect(source, target);
            var oldParentPosition = new Vector3(-600, -600);
            child.ParentStateMachinePosition = oldParentPosition;
            grandchild.ParentStateMachinePosition = oldParentPosition;

            rootMachine.AutoLayout(nested);

            Assert.AreNotEqual(new Vector3(-500, -500), rootMachine.StateMachines[0].Position);
            Assert.AreSame(target, transition.DestinationState);
            Assert.AreSame(defaultState, grandchild.DefaultState);
            if (!nested) {
                Assert.AreEqual(new Vector3(-400, -400), ancestor.States[0].Position);
                Assert.AreEqual(new Vector3(-300, -300), ancestor.StateMachines[0].Position);
                Assert.AreEqual(new Vector3(-200, -200), child.StateMachines[0].Position);
                Assert.AreEqual(new Vector3(-150, -150), grandchild.States[1].Position);
                Assert.AreEqual(oldParentPosition, child.ParentStateMachinePosition);
                Assert.AreEqual(oldParentPosition, grandchild.ParentStateMachinePosition);
            } else {
                AssertRankBefore(ancestor, ancestor.States[0].Position, ancestor.StateMachines[0].Position);
                Assert.AreNotEqual(oldParentPosition, child.ParentStateMachinePosition);
                AssertRankBefore(child, child.ParentStateMachinePosition, child.StateMachines[0].Position);
                Assert.AreNotEqual(oldParentPosition, grandchild.ParentStateMachinePosition);
                AssertRankBefore(grandchild, grandchild.ParentStateMachinePosition, grandchild.States[1].Position);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LayoutProjectsDeepStateToMachineInAnotherBranch(bool reverse) {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var common = rootMachine.AddStateMachine("common");
            var left = common.AddStateMachine("left");
            var right = common.AddStateMachine("right");
            var sourceBranch = reverse ? right : left;
            var targetBranch = reverse ? left : right;
            var sourceChild = sourceBranch.AddStateMachine("source child");
            var source = sourceChild.AddState("source");
            sourceChild.DefaultState = source;
            var targetChild = targetBranch.AddStateMachine("target child");
            var target = targetChild.AddStateMachine("target");
            var defaultState = target.AddState("default");
            target.DefaultState = defaultState;
            var oldParentPosition = new Vector3(-600, -600);
            sourceBranch.ParentStateMachinePosition = oldParentPosition;
            sourceChild.ParentStateMachinePosition = oldParentPosition;
            targetBranch.ParentStateMachinePosition = oldParentPosition;
            targetChild.ParentStateMachinePosition = oldParentPosition;
            target.ParentStateMachinePosition = oldParentPosition;
            var transition = source.ConnectTo(target);

            rootMachine.AutoLayout(true);

            AssertRankBefore(common, common.StateMachines[reverse ? 1 : 0].Position,
                common.StateMachines[reverse ? 0 : 1].Position);
            Assert.AreNotEqual(oldParentPosition, sourceBranch.ParentStateMachinePosition);
            AssertRankBefore(sourceBranch, sourceBranch.StateMachines[0].Position, sourceBranch.ParentStateMachinePosition);
            Assert.AreNotEqual(oldParentPosition, sourceChild.ParentStateMachinePosition);
            AssertRankBefore(sourceChild, sourceChild.States[0].Position, sourceChild.ParentStateMachinePosition);
            Assert.AreNotEqual(oldParentPosition, targetBranch.ParentStateMachinePosition);
            AssertRankBefore(targetBranch, targetBranch.ParentStateMachinePosition, targetBranch.StateMachines[0].Position);
            Assert.AreNotEqual(oldParentPosition, targetChild.ParentStateMachinePosition);
            AssertRankBefore(targetChild, targetChild.ParentStateMachinePosition, targetChild.StateMachines[0].Position);
            Assert.AreEqual(oldParentPosition, target.ParentStateMachinePosition);
            Assert.AreNotEqual(Vector3.zero, target.States[0].Position);
            Assert.AreSame(target, transition.DestinationStateMachine);
            Assert.AreSame(defaultState, target.DefaultState);
        }

        [Test]
        public void LayoutWithoutNestedContentsDoesNotMoveParentNode() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var outside = rootMachine.AddState("outside");
            rootMachine.DefaultState = outside;
            var child = rootMachine.AddStateMachine("child", new Vector3(-500, -500));
            var inside = child.AddState("inside");
            child.ParentStateMachinePosition = new Vector3(-600, -600);
            Connect(outside, inside);

            rootMachine.AutoLayout();

            Assert.AreNotEqual(new Vector3(-500, -500), rootMachine.StateMachines[0].Position);
            Assert.AreEqual(new Vector3(-600, -600), child.ParentStateMachinePosition);
        }

        [Test]
        public void LayoutKeepsExitTransitionsSeparateFromParentConnections() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var child = rootMachine.AddStateMachine("child");
            var inside = child.AddState("inside");
            child.DefaultState = inside;
            var exit = VirtualStateTransition.Create();
            exit.SetExitDestination();
            inside.Transitions = inside.Transitions.Add(exit);
            var originalParentPosition = new Vector3(-600, -600);
            child.ParentStateMachinePosition = originalParentPosition;
            var machineExit = VirtualTransition.Create();
            machineExit.SetExitDestination();
            rootMachine.StateMachineTransitions = rootMachine.StateMachineTransitions.Add(child,
                System.Collections.Immutable.ImmutableList.Create(machineExit));

            rootMachine.AutoLayout(true);

            Assert.AreEqual(originalParentPosition, child.ParentStateMachinePosition);
            Assert.AreNotEqual(child.States[0].Position, child.ExitPosition);
            Assert.AreNotEqual(rootMachine.StateMachines[0].Position, rootMachine.ExitPosition);
            Assert.IsTrue(exit.IsExit);
            Assert.IsTrue(machineExit.IsExit);
        }

        [Test]
        public void LayoutHandlesEmptyMachinesWithAndWithoutExternalTransitions() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var outside = rootMachine.AddState("outside");
            rootMachine.DefaultState = outside;
            var empty = rootMachine.AddStateMachine("empty", new Vector3(-500, -500));
            var isolated = rootMachine.AddStateMachine("isolated", new Vector3(-400, -400));
            var oldParentPosition = new Vector3(-600, -600);
            empty.ParentStateMachinePosition = oldParentPosition;
            isolated.ParentStateMachinePosition = oldParentPosition;
            var oldEntryPosition = isolated.EntryPosition;
            var entry = VirtualTransition.Create();
            entry.SetDestination(outside);
            empty.EntryTransitions = empty.EntryTransitions.Add(entry);
            outside.ConnectTo(empty);

            rootMachine.AutoLayout(true);

            Assert.AreNotEqual(new Vector3(-500, -500), rootMachine.StateMachines[0].Position);
            Assert.AreNotEqual(oldParentPosition, empty.ParentStateMachinePosition);
            Assert.AreEqual(oldParentPosition, isolated.ParentStateMachinePosition);
            Assert.AreEqual(oldEntryPosition, isolated.EntryPosition);
            Assert.IsEmpty(empty.States);
            Assert.IsEmpty(empty.StateMachines);
        }

        [Test]
        public void LayoutTraversesDeepNestedMachinesWithoutRecursion() {
            var rootMachine = VirtualStateMachine.Create(cloneContext);
            var current = rootMachine;
            for (int i = 0; i < 400; i++)
                current = current.AddStateMachine($"level {i}", new Vector3(-100, -100));
            var state = current.AddState("last", position: new Vector3(-100, -100));
            current.DefaultState = state;

            rootMachine.AutoLayout(true);

            Assert.AreNotEqual(new Vector3(-100, -100), current.States[0].Position);
        }

        [Test]
        public void LayoutRejectsCyclicMachineHierarchies() {
            var machine = VirtualStateMachine.Create(cloneContext);
            machine.StateMachines = machine.StateMachines.Add(new VirtualStateMachine.VirtualChildStateMachine {
                StateMachine = machine,
            });

            Assert.Throws<System.ArgumentException>(() => machine.AutoLayout(true));
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
