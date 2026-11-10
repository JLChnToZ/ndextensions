using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using UnityEngine;
using UnityEngine.Pool;
using UnityEditor;
using UnityEditor.Animations;
using nadena.dev.ndmf.animator;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class CompileContext : IDisposable {
        readonly VirtualAnimatorController controller;
        readonly IReadOnlyDictionary<string, UnityObject> references;
        readonly ObjectPathRemapper pathRemapper;
        readonly DummyClips dummyClips;
        readonly Transform fallbackRoot;
        readonly Dictionary<string, AnimatorControllerParameterType> parameterTypes;
        readonly Dictionary<string, FlowNode> labels;
        readonly List<(FlowNode node, string label, int row, int col)> gotos;
        readonly List<FlowNode> nodes;
        readonly bool writeDefaults;

        static Transform ToTransform(UnityObject target) {
            if (target is Transform transform) return transform;
            if (target is GameObject gameObject) return gameObject.transform;
            if (target is Component component) return component.transform;
            return null;
        }

        static bool ContainsSet(Block block) {
            foreach (var statement in block.statements) {
                if (statement is SetStatement) return true;
                if (statement is IfStatement conditional &&
                    (ContainsSet(conditional.thenBlock) || conditional.elseBlock != null && ContainsSet(conditional.elseBlock)))
                    return true;
            }
            return false;
        }

        static bool IsNumeric(AnimatorControllerParameterType type) => type == AnimatorControllerParameterType.Int || type == AnimatorControllerParameterType.Float;

        static void Throw(Statement statement, string message) => throw new ParseException(statement.row, statement.col, message);

        static void Split(Block block, List<ISegment> segments, List<ActionStatement> actions) {
            void Flush(float? wait = null) {
                if (actions.Count > 0 || wait.HasValue) segments.Add(new ActionSegment(actions.ToArray(), wait));
                actions.Clear();
            }

            foreach (var statement in block.statements) {
                switch (statement) {
                    case ActionStatement action:
                        actions.Add(action);
                        break;
                    case WaitStatement wait:
                        Flush(wait.duration);
                        break;
                    case IfStatement conditional:
                        Flush();
                        segments.Add(new IfSegment(conditional));
                        break;
                    case LabelStatement label:
                        Flush();
                        segments.Add(new LabelSegment(label));
                        break;
                    case GotoStatement jump:
                        segments.Add(new GotoSegment(jump, actions.ToArray()));
                        actions.Clear();
                        break;
                    case ExitStatement exit:
                        segments.Add(new ExitSegment(exit, actions.ToArray()));
                        actions.Clear();
                        break;
                }
            }
            Flush();
        }

        static VirtualStateTransition AddTransition(VirtualState from, VirtualState to, float? exitTime, bool isExit) {
            var transition = VirtualStateTransition.Create();
            transition.Duration = 0;
            transition.HasFixedDuration = true;
            transition.ExitTime = exitTime;
            if (isExit) transition.SetExitDestination();
            else transition.SetDestination(to);
            from.Transitions = from.Transitions.Add(transition);
            return transition;
        }

        static void CopyClauses(List<List<AnimatorCondition>> source, ClauseScope destination, Statement statement) {
            foreach (var clause in source) {
                var copy = destination.Add();
                copy.AddRange(clause);
                if (copy.Count > 64)
                    Throw(statement, "Condition expands to too many transition alternatives.");
            }
        }

        static void AddClause(ClauseScope clauses, AnimatorCondition condition) => clauses.Add().Add(condition);

        static AnimatorCondition MakeCondition(AnimatorConditionMode mode, string parameter, float threshold = 0) => new() {
            mode = mode,
            parameter = parameter,
            threshold = threshold,
        };

        public CompileContext(
            VirtualAnimatorController controller,
            IReadOnlyDictionary<string, UnityObject> references,
            ObjectPathRemapper pathRemapper,
            DummyClips dummyClips,
            Transform fallbackRoot,
            bool writeDefaults
        ) {
            this.controller = controller;
            this.references = references;
            this.pathRemapper = pathRemapper;
            this.dummyClips = dummyClips;
            this.fallbackRoot = fallbackRoot;
            this.writeDefaults = writeDefaults;
            parameterTypes = DictionaryPool<string, AnimatorControllerParameterType>.Get();
            labels = DictionaryPool<string, FlowNode>.Get();
            gotos = ListPool<(FlowNode node, string label, int row, int col)>.Get();
            nodes = ListPool<FlowNode>.Get();
        }

        public void Dispose() {
            ListPool<FlowNode>.Release(nodes);
            ListPool<(FlowNode node, string label, int row, int col)>.Release(gotos);
            DictionaryPool<string, FlowNode>.Release(labels);
            DictionaryPool<string, AnimatorControllerParameterType>.Release(parameterTypes);
        }

        public void Validate(Program program) {
            CollectParameterTypes(program.root);
            ValidateParameterTypes();
            ResolveAnimationTargets(program.root);
#if !VRC_SDK_VRCSDK3
            if (ContainsSet(program.Root))
                throw new NotSupportedException("Parameter-driver instructions require the VRChat SDK3 avatar package.");
#endif
        }

        public void Compile(Program program, VirtualLayer layer) {
            var terminal = AddNode(new FlowNode());
            var entry = BuildBlock(program.root, terminal);
            foreach (var (node, label, row, col) in gotos) {
                if (!labels.TryGetValue(label, out var destination))
                    throw new ParseException(row, col, $"Unknown label '{label}'.");
                node.nextNode = destination;
                node.gotoLabel = null;
            }
            MarkPassThroughGotos();
            MarkInlinedConditions();
            var entryNode = ResolveNode(entry);

            RegisterParameters();
            var machine = layer.StateMachine;
            int index = 0;
            foreach (var node in nodes) {
                if (node.isPassThroughGoto || node.isInlinedCondition || node.isTerminal && node != entryNode) continue;
                var motion = BuildMotion(node);
                node.state = machine.AddState(
                    node.isExit ? "Exit" : node.isTerminal ? "End" : $"Instruction {index++}",
                    motion,
                    new Vector3(index * 180, 0, 0)
                ).WriteDefaults(writeDefaults);
                using (ListPool<SetStatement>.Get(out var assignments)) {
                    if (node.actions != null)
                        foreach (var action in node.actions)
                            if (action is SetStatement assignment)
                                assignments.Add(assignment);
                    if (assignments.Count > 0) AddParameterDrivers(node.state, assignments);
                }
            }
            foreach (var node in nodes)
                if (node.isPassThroughGoto)
                    node.state = ResolveNode(node).state;

            foreach (var node in nodes) EmitTransitions(node);
            machine.DefaultState = entryNode.state;
        }

        FlowNode BuildBlock(Block block, FlowNode continuation) {
            using (ListPool<ISegment>.Get(out var segments))
            using (ListPool<ActionStatement>.Get(out var actions)) {
                Split(block, segments, actions);
                var current = continuation;
                for (int i = segments.Count - 1; i >= 0; i--) {
                    switch (segments[i]) {
                        case ActionSegment action:
                            var actionNode = AddNode(new FlowNode(current, action.waitDuration, action.actions));
                            current = actionNode;
                            break;
                        case IfSegment conditional:
                            var thenNode = BuildBlock(conditional.statement.thenBlock, current);
                            var elseNode = conditional.statement.elseBlock == null ? current : BuildBlock(conditional.statement.elseBlock, current);
                            current = AddNode(new FlowNode(conditional.statement, thenNode, elseNode));
                            break;
                        case LabelSegment label:
                            if (!labels.TryAdd(label.statement.name, current))
                                throw new ParseException(label.statement.row, label.statement.col, $"Duplicate label '{label.statement.name}'.");
                            break;
                        case GotoSegment jump:
                            var gotoNode = AddNode(new FlowNode(jump.statement, jump.actions));
                            gotos.Add((gotoNode, jump.statement.label, jump.statement.row, jump.statement.col));
                            current = gotoNode;
                            break;
                        case ExitSegment exit:
                            var exitNode = AddNode(new FlowNode(exit.statement, exit.actions));
                            current = exitNode;
                            break;
                    }
                }
                return current;
            }
        }

        FlowNode AddNode(FlowNode node) {
            nodes.Add(node);
            return node;
        }

        void MarkPassThroughGotos() {
            foreach (var node in nodes)
                node.isPassThroughGoto = node.isGoto && node.actions.Length == 0;

            foreach (var node in nodes) {
                if (!node.isPassThroughGoto) continue;
                var path = new HashSet<FlowNode>();
                var current = node;
                while (current.isPassThroughGoto && path.Add(current))
                    current = current.nextNode;
                if (current.isPassThroughGoto) current.isPassThroughGoto = false;
            }
        }

        static FlowNode ResolveNode(FlowNode node) {
            while (node != null && node.isPassThroughGoto) node = node.nextNode;
            return node;
        }

        void MarkInlinedConditions() {
            using (HashSetPool<FlowNode>.Get(out var labelTargets)) {
                foreach (var node in labels.Values)
                    if (node != null) labelTargets.Add(node);
                foreach (var node in nodes)
                    if (!node.isTerminal && !node.isPassThroughGoto && node.condition == null)
                        MarkInlinedCondition(node.nextNode, labelTargets);
            }
        }

        void MarkInlinedCondition(FlowNode node, HashSet<FlowNode> labelTargets) {
            node = ResolveNode(node);
            if (node == null || node.isTerminal || node.condition == null || labelTargets.Contains(node) || node.isInlinedCondition)
                return;
            node.isInlinedCondition = true;
            MarkInlinedCondition(node.thenNode, labelTargets);
            MarkInlinedCondition(node.elseNode, labelTargets);
        }

        void EmitTransitions(FlowNode node) {
            if (node.isTerminal || node.isPassThroughGoto || node.isInlinedCondition) return;
            if (node.isExit) {
                AddTransition(node.state, null, null, true);
                return;
            }
            if (node.condition != null) {
                AddConditionalRoute(node.state, node.thenNode, node.condition);
                AddConditionalRoute(node.state, node.elseNode, new NotExpression(node.condition.row, node.condition.col, node.condition));
                return;
            }
            if (node.nextNode == null) return;
            var nextNode = ResolveNode(node.nextNode);
            if (nextNode == null) return;
            if (nextNode.isTerminal) return;
            if (nextNode.isInlinedCondition) {
                EmitInlinedCondition(node.state, nextNode, null,
                    node.exitDuration is { } duration ?
                    node.motionDuration <= 0 ? 1 : Mathf.Clamp01(duration / node.motionDuration) :
                    null
                );
                return;
            }
            AddTransition(node.state, nextNode.state,
                node.exitDuration is not { } exitDuration || node.motionDuration <= 0 ? 1 :
                Mathf.Clamp01(exitDuration / node.motionDuration),
                false
            );
        }

        void EmitInlinedCondition(VirtualState from, FlowNode node, Expression prefix, float? exitTime) {
            var condition = CombineConditions(prefix, node.condition);
            var negatedCondition = CombineConditions(
                prefix,
                new NotExpression(node.condition.row, node.condition.col, node.condition)
            );
            AddConditionalRoute(from, node.thenNode, condition, exitTime);
            AddConditionalRoute(from, node.elseNode, negatedCondition, exitTime);
        }

        void AddConditionalRoute(VirtualState from, FlowNode destination, Expression condition, float? exitTime = null) {
            destination = ResolveNode(destination);
            if (destination == null || destination.isTerminal) return;
            if (destination.isInlinedCondition) {
                EmitInlinedCondition(from, destination, condition, exitTime);
                return;
            }
            AddConditionalTransitions(from, destination.state, condition, false, exitTime);
        }

        static Expression CombineConditions(Expression prefix, Expression condition) => prefix == null
            ? condition
            : new BinaryExpression(prefix.row, prefix.col, prefix, condition, true);

        void AddConditionalTransitions(VirtualState from, VirtualState to, Expression expression, bool negate, float? exitTime = null) {
            using (ToDnfClauses(expression, negate, out var clauses))
                foreach (var clause in clauses)
                    AddTransition(from, to, exitTime, false).Conditions = clause.ToImmutableList();
        }

        ClauseScope ToDnfClauses(Expression expression, bool negate, out List<List<AnimatorCondition>> clauses) {
            if (expression is NotExpression not) return ToDnfClauses(not.operand, !negate, out clauses);
            if (expression is BinaryExpression binary) return CombineDnf(binary, negate, out clauses);

            var result = new ClauseScope(out clauses);
            try {
                switch (expression) {
                    case BooleanConstantExpression constant:
                        if (constant.value != negate) result.Add();
                        break;
                    case BooleanParameterExpression parameter:
                        var parameterType = parameterTypes[parameter.parameter];
                        AddClause(result, parameterType switch {
                            AnimatorControllerParameterType.Bool => new AnimatorCondition {
                                mode = negate ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                                parameter = parameter.parameter,
                            },
                            AnimatorControllerParameterType.Int => new AnimatorCondition {
                                mode = negate ? AnimatorConditionMode.Equals : AnimatorConditionMode.NotEqual,
                                parameter = parameter.parameter,
                                threshold = 0,
                            },
                            _ => new AnimatorCondition {
                                mode = negate ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater,
                                parameter = parameter.parameter,
                                threshold = 0.5f,
                            },
                        });
                        break;
                    case ComparisonExpression comparison:
                        MakeConditions(comparison, negate, result);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown condition expression.");
                }
                return result;
            } catch {
                result.Dispose();
                throw;
            }
        }

        ClauseScope CombineDnf(BinaryExpression binary, bool negate, out List<List<AnimatorCondition>> clauses) {
            using (ToDnfClauses(binary.left, negate, out var left))
            using (ToDnfClauses(binary.right, negate, out var right)) {
                var result = new ClauseScope(out clauses);
                try {
                    var isAnd = binary.IsAnd != negate;
                    if (isAnd) {
                        foreach (var leftClause in left)
                            foreach (var rightClause in right) {
                                var combined = result.Add();
                                combined.AddRange(leftClause);
                                combined.AddRange(rightClause);
                                if (clauses.Count > 64)
                                    Throw(binary, "Condition expands to too many transition alternatives.");
                            }
                    } else {
                        CopyClauses(left, result, binary);
                        CopyClauses(right, result, binary);
                    }
                    return result;
                } catch {
                    result.Dispose();
                    throw;
                }
            }
        }

        void MakeConditions(ComparisonExpression comparison, bool negate, ClauseScope clauses) {
            var type = parameterTypes[comparison.parameter];
            var op = comparison.op;
            if (negate) op = op switch {
                "==" => "!=",
                "!=" => "==",
                "<" => ">=",
                "<=" => ">",
                ">" => "<=",
                ">=" => "<",
                _ => op,
            };
            if (comparison.value.type == ValueType.Boolean) {
                if (type != AnimatorControllerParameterType.Bool)
                    Throw(comparison, "Boolean comparisons require a boolean parameter.");
                var isTrue = comparison.value.value != 0;
                var positive = op == "==" ? isTrue : !isTrue;
                if (op != "==" && op != "!=") Throw(comparison, "Boolean parameters only support == and !=.");
                AddClause(clauses, new AnimatorCondition {
                    mode = positive ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    parameter = comparison.parameter,
                });
                return;
            }
            if (type == AnimatorControllerParameterType.Bool)
                Throw(comparison, "Numeric comparisons require an integer or float parameter.");
            var threshold = comparison.value.value;
            if (op == "<=") {
                threshold += comparison.value.type == ValueType.Integer ? 1 : 0.0001F;
                op = "<";
            } else if (op == ">=") {
                threshold -= comparison.value.type == ValueType.Integer ? 1 : 0.0001F;
                op = ">";
            }
            var mode = op switch {
                "==" => AnimatorConditionMode.Equals,
                "!=" => AnimatorConditionMode.NotEqual,
                "<" => AnimatorConditionMode.Less,
                ">" => AnimatorConditionMode.Greater,
                _ => throw new InvalidOperationException("Unknown comparison operator."),
            };
            AddClause(clauses, MakeCondition(mode, comparison.parameter, threshold));
        }

        VirtualMotion BuildMotion(FlowNode node) {
            bool hasAnimationActions = false;
            if (node.actions != null)
                foreach (var action in node.actions)
                    if (action is AnimationStatement) {
                        hasAnimationActions = true;
                        break;
                    }
            var waitDuration = node.exitDuration ?? 0;
            if (!hasAnimationActions) return dummyClips.Get(Mathf.Max(waitDuration, 0));

            var clip = VirtualClip.Create($"Instruction {node.row}/{node.col}");
            clip.FrameRate = 60;
            using (DictionaryPool<EditorCurveBinding, CurveTrack>.Get(out var tracks))
            using (DictionaryPool<EditorCurveBinding, PointerCurveTrack>.Get(out var pointerTracks)) {
                float duration = waitDuration;
                if (node.actions != null)
                    foreach (var actionStatement in node.actions) {
                        if (!(actionStatement is AnimationStatement action)) continue;
                        if (action.IsPointerCurve) {
                            var binding = EditorCurveBinding.PPtrCurve(action.resolvedPath, action.type, action.property);
                            if (!pointerTracks.TryGetValue(binding, out var pointerTrack)) {
                                pointerTrack = new PointerCurveTrack();
                                pointerTracks.Add(binding, pointerTrack);
                            }
                            pointerTrack.LastValue = action.resolvedRef;
                            pointerTrack.Set(new ObjectReferenceKeyframe { time = pointerTrack.LastTime, value = pointerTrack.LastValue });
                            pointerTrack.HasValue = true;
                            continue;
                        }

                        var floatBinding = EditorCurveBinding.FloatCurve(action.resolvedPath, action.type, action.property);
                        if (!tracks.TryGetValue(floatBinding, out var track)) {
                            track = new CurveTrack();
                            tracks.Add(floatBinding, track);
                        }
                        if (action.easingDuration > 0) {
                            var from = track.HasValue ? track.LastValue : action.value;
                            var start = track.HasValue ? track.LastTime : 0;
                            var end = start + action.easingDuration;
                            var curve = EasingFactory.Create(action.easing, from, action.value, start, end);
                            track.Set(curve[0]);
                            track.Set(curve[1]);
                            track.LastValue = action.value;
                            track.LastTime = end;
                            track.HasValue = true;
                            duration = Mathf.Max(duration, end);
                        } else {
                            var time = track.HasValue ? track.LastTime : 0;
                            track.Set(new Keyframe(time, action.value));
                            track.LastValue = action.value;
                            track.LastTime = time;
                            track.HasValue = true;
                        }
                    }

                if (duration <= 0) duration = 1f / clip.FrameRate;
                foreach (var (binding, track) in tracks) {
                    track.Set(new Keyframe(duration, track.LastValue));
                    clip.SetFloatCurve(binding, new AnimationCurve(track.CopyKeys()));
                }
                foreach (var (binding, track) in pointerTracks) {
                    track.Set(new ObjectReferenceKeyframe { time = duration, value = track.LastValue });
                    clip.SetObjectCurve(binding, track.CopyKeys());
                }
                var settings = clip.Settings;
                settings.stopTime = duration;
                clip.Settings = settings;
                node.motionDuration = duration;
                return clip;
            }
        }

        void AddParameterDrivers(VirtualState state, IEnumerable<SetStatement> assignments) {
#if VRC_SDK_VRCSDK3
            using (ListPool<SetStatement>.Get(out var sharedAssignments))
            using (ListPool<SetStatement>.Get(out var localAssignments)) {
                foreach (var assignment in assignments)
                    (assignment.local ? localAssignments : sharedAssignments).Add(assignment);
                if (sharedAssignments.Count > 0) AddParameterDriver(state, sharedAssignments, false);
                if (localAssignments.Count > 0) AddParameterDriver(state, localAssignments, true);
            }
#endif
        }

        void AddParameterDriver(VirtualState state, IEnumerable<SetStatement> assignments, bool localOnly) {
#if VRC_SDK_VRCSDK3
            var driver = state.WithParameterChange().LocalOnly(localOnly);
            foreach (var assignment in assignments) {
                if (assignment.operation != SetOperation.Assign) {
                    var delta = assignment.value.value * (assignment.operation == SetOperation.Subtract ? -1 : 1);
                    driver.Increment(assignment.parameter, delta);
                    continue;
                }
                switch (assignment.value.type) {
                    case ValueType.Boolean:
                    case ValueType.Integer:
                    case ValueType.Float:
                        driver.Set(assignment.parameter, assignment.value.value);
                        break;
                    case ValueType.Random:
                        driver.Random(assignment.parameter, assignment.value.min, assignment.value.max);
                        break;
                    case ValueType.Copy:
                        if (assignment.min != assignment.max && assignment.value.min != assignment.value.max)
                            driver.Copy(assignment.value.source, assignment.parameter, assignment.value.min, assignment.value.max, assignment.min, assignment.max);
                        else
                            driver.Copy(assignment.value.source, assignment.parameter);
                        break;
                }
            }
#endif
        }

        void RegisterParameters() {
            foreach (var (name, type) in parameterTypes) {
                if (!controller.Parameters.ContainsKey(name))
                    controller.EnsureParameter(name, type, false);
            }
        }

        void ValidateParameterTypes() {
            using (ListPool<KeyValuePair<string, AnimatorControllerParameterType>>.Get(out var snapshot)) {
                foreach (var parameter in parameterTypes)
                    snapshot.Add(parameter);
                foreach (var (name, inferred) in snapshot) {
                    if (!controller.Parameters.TryGetValue(name, out var existing)) continue;
                    var existingType = existing.type;
                    bool bothNumeric = IsNumeric(existingType) && IsNumeric(inferred);
                    if (existingType != inferred && !bothNumeric)
                        throw new InvalidOperationException($"Parameter '{name}' is used with incompatible types.");
                    parameterTypes[name] = existingType;
                }
            }
        }

        void CollectParameterTypes(Block block) {
            foreach (var statement in block.statements) {
                switch (statement) {
                    case SetStatement set:
                        if (set.operation != SetOperation.Assign &&
                            set.value.type != ValueType.Integer && set.value.type != ValueType.Float)
                            Throw(set, "Additive assignments require a numeric literal.");
                        AddParameterType(set.parameter, set.value.type switch {
                            ValueType.Boolean => AnimatorControllerParameterType.Bool,
                            ValueType.Integer => AnimatorControllerParameterType.Int,
                            _ => AnimatorControllerParameterType.Float,
                        }, set);
                        break;
                    case IfStatement conditional:
                        CollectExpressionTypes(conditional.condition);
                        CollectParameterTypes(conditional.thenBlock);
                        if (conditional.elseBlock != null) CollectParameterTypes(conditional.elseBlock);
                        break;
                }
            }
        }

        void CollectExpressionTypes(Expression expression) {
            switch (expression) {
                case BooleanParameterExpression parameter:
                    if (controller.Parameters.TryGetValue(parameter.parameter, out var existing))
                        AddParameterType(parameter.parameter, existing.type, parameter);
                    else
                        AddParameterType(parameter.parameter, AnimatorControllerParameterType.Bool, parameter);
                    break;
                case ComparisonExpression comparison:
                    var type = comparison.value.type switch {
                        ValueType.Boolean => AnimatorControllerParameterType.Bool,
                        ValueType.Integer => AnimatorControllerParameterType.Int,
                        _ => AnimatorControllerParameterType.Float,
                    };
                    AddParameterType(comparison.parameter, type, comparison);
                    break;
                case NotExpression not:
                    CollectExpressionTypes(not.operand);
                    break;
                case BinaryExpression binary:
                    CollectExpressionTypes(binary.left);
                    CollectExpressionTypes(binary.right);
                    break;
            }
        }

        void AddParameterType(string name, AnimatorControllerParameterType type, Statement source) {
            if (parameterTypes.TryGetValue(name, out var existing)) {
                if (existing == type || IsNumeric(existing) && IsNumeric(type)) {
                    if (type == AnimatorControllerParameterType.Float) parameterTypes[name] = type;
                    return;
                }
                Throw(source, $"Parameter '{name}' is used with incompatible types.");
            }
            parameterTypes.Add(name, type);
        }

        Transform TryResolveTarget(string targetStr, out UnityObject target) {
            target = null;
            if (references != null && references.TryGetValue(targetStr, out target)) {
                var transform = ToTransform(target);
                if (transform != null) return transform;
            }
            if (fallbackRoot != null) {
                var transform = fallbackRoot.Find(targetStr);
                if (transform != null) {
                    target = transform;
                    return transform;
                }
            }
            var gameObject = pathRemapper.GetObjectForPath(targetStr);
            if (gameObject != null) {
                target = gameObject;
                return gameObject.transform;
            }
            if (string.Equals(targetStr, "this", StringComparison.Ordinal)) {
                target = fallbackRoot;
                return fallbackRoot;
            }
            return null;
        }

        void ResolveAnimationTargets(Block block) {
            foreach (var statement in block.statements)
                switch (statement) {
                    case AnimationStatement animation:
                        var transform = TryResolveTarget(animation.target, out var target);
                        if (transform == null)
                            throw new ParseException(animation.row, animation.col, $"Could not map transform '{animation.target}' to an animation path.");
                        if (animation.type == typeof(Animator) && transform == fallbackRoot)
                            animation.resolvedPath = ""; // Self modifying animator
                        else
                            animation.resolvedPath = pathRemapper.GetVirtualPathForObject(transform);
                        if (animation.type == null) animation.type = target.GetType();
                        if (animation.IsPointerCurve) {
                            if (references == null || !references.TryGetValue(animation.refName, out var reference) || reference == null)
                                throw new ParseException(animation.row, animation.col, $"Could not resolve reference '{animation.refName}'.");
                            animation.resolvedRef = reference;
                        }
                        break;
                    case IfStatement conditional:
                        ResolveAnimationTargets(conditional.thenBlock);
                        if (conditional.elseBlock != null) ResolveAnimationTargets(conditional.elseBlock);
                        break;
                }
        }

        sealed class CurveTrack {
            readonly SortedDictionary<float, Keyframe> keys = new();
            public bool HasValue;
            public float LastValue;
            public float LastTime;

            public void Set(Keyframe key) => keys[key.time] = key;

            public Keyframe[] CopyKeys() {
                var result = new Keyframe[keys.Count];
                keys.Values.CopyTo(result, 0);
                return result;
            }
        }

        sealed class PointerCurveTrack {
            readonly SortedDictionary<float, ObjectReferenceKeyframe> keys = new();
            public bool HasValue;
            public UnityObject LastValue;
            public float LastTime;

            public void Set(ObjectReferenceKeyframe key) => keys[key.time] = key;

            public ObjectReferenceKeyframe[] CopyKeys() {
                var result = new ObjectReferenceKeyframe[keys.Count];
                keys.Values.CopyTo(result, 0);
                return result;
            }
        }

        readonly struct ClauseScope : IDisposable {
            readonly List<List<AnimatorCondition>> clauses;

            public ClauseScope(out List<List<AnimatorCondition>> clauses) {
                this.clauses = ListPool<List<AnimatorCondition>>.Get();
                clauses = this.clauses;
            }

            public List<AnimatorCondition> Add() {
                var clause = ListPool<AnimatorCondition>.Get();
                clauses.Add(clause);
                return clause;
            }

            public void Dispose() {
                foreach (var clause in clauses) ListPool<AnimatorCondition>.Release(clause);
                ListPool<List<AnimatorCondition>>.Release(clauses);
            }
        }
    }
}
