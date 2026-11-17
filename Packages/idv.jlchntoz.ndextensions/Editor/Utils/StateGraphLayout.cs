using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using nadena.dev.ndmf.animator;

namespace JLChnToZ.NDExtensions.Editors {
    static class StateGraphLayout {
        const float columnSpacing = 260, rowSpacing = 110;
        const float nodeWidth = 200, nodeHeight = 60;
        const int scoreLimit = 32768;
        const int sweeps = 4;
        const int refinementLimit = 128;

        public static void AutoLayout(this VirtualStateMachine machine, bool nested = false) {
            using (ListPool<VirtualStateMachine>.Get(out var pending))
            using (ListPool<VirtualStateMachine>.Get(out var hierarchy))
            using (HashSetPool<VirtualStateMachine>.Get(out var visited)) {
                pending.Add(machine);
                while (pending.Count > 0) {
                    int last = pending.Count - 1;
                    var current = pending[last];
                    pending.RemoveAt(last);
                    if (!visited.Add(current))
                        throw new System.ArgumentException("A state machine appears more than once in the hierarchy.");
                    hierarchy.Add(current);
                    foreach (var child in current.StateMachines) pending.Add(child.StateMachine);
                }
                LayoutMachine(machine, hierarchy);
                if (nested)
                    for (int i = 1; i < hierarchy.Count; i++)
                        LayoutMachine(hierarchy[i], hierarchy);
            }
        }

        static void LayoutMachine(VirtualStateMachine machine, List<VirtualStateMachine> hierarchy) {
            var states = machine.States;
            var machines = machine.StateMachines;
            int nodeCount = states.Count + machines.Count;

            using (DictionaryPool<VirtualState, int>.Get(out var indices))
            using (DictionaryPool<VirtualStateMachine, int>.Get(out var machineIndices))
            using (ListPool<Edge>.Get(out var edges))
            using (HashSetPool<(int, int)>.Get(out var uniqueEdges))
            using (ListPool<SortedDictionary<int, List<int>>>.Get(out var levels))
            using (ListPool<VirtualStateMachine>.Get(out var pending)) {
                List<int>[] outgoing = null, incoming = null;
                List<List<int>> groups = null;
                try {
                    for (int i = 0; i < states.Count; i++) indices.Add(states[i].State, i);
                    for (int i = 0; i < machines.Count; i++) {
                        int index = states.Count + i;
                        pending.Add(machines[i].StateMachine);
                        while (pending.Count > 0) {
                            int last = pending.Count - 1;
                            var child = pending[last];
                            pending.RemoveAt(last);
                            machineIndices.Add(child, index);
                            foreach (var state in child.States) indices.Add(state.State, index);
                            foreach (var nested in child.StateMachines) pending.Add(nested.StateMachine);
                        }
                    }
                    bool hasParent = HasExternalTransitions(machine, hierarchy, indices, machineIndices);
                    int parent = nodeCount;
                    int count = nodeCount + (hasParent ? 1 : 0);
                    if (count == 0) return;
                    int exit = count, entryNode = count + 1, any = count + 2;
                    int entry = machine.DefaultState != null ? indices[machine.DefaultState] : 0;
                    outgoing = NewAdjacency(count);
                    incoming = NewAdjacency(count);
                    foreach (var current in hierarchy) {
                        bool isRoot = current == machine;
                        bool currentIsInside = isRoot || machineIndices.TryGetValue(current, out _);
                        int source = isRoot ? entryNode :
                            currentIsInside ? machineIndices[current] : parent;
                        foreach (var state in current.States) {
                            bool inside = indices.TryGetValue(state.State, out int stateSource);
                            if (!inside && !hasParent) continue;
                            foreach (var transition in state.State.Transitions)
                                AddTransition(inside ? stateSource : parent, transition, inside && isRoot,
                                    inside, hasParent, parent, indices, machineIndices,
                                    edges, uniqueEdges, outgoing, incoming, exit);
                        }
                        if (!currentIsInside && !hasParent) continue;
                        foreach (var transition in current.EntryTransitions)
                            AddTransition(source, transition, false, currentIsInside, hasParent, parent, indices, machineIndices,
                                edges, uniqueEdges, outgoing, incoming, exit);
                        source = isRoot ? any : source;
                        foreach (var transition in current.AnyStateTransitions)
                            AddTransition(source, transition, false, currentIsInside, hasParent, parent, indices, machineIndices,
                                edges, uniqueEdges, outgoing, incoming, exit);
                        foreach (var child in current.StateMachines) {
                            bool inside = machineIndices.TryGetValue(child.StateMachine, out int childSource);
                            if (!inside && !hasParent) continue;
                            if (current.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                                foreach (var transition in transitions)
                                    AddTransition(inside ? childSource : parent, transition, inside && isRoot,
                                        inside, hasParent, parent, indices,
                                        machineIndices, edges, uniqueEdges, outgoing, incoming, exit);
                        }
                    }
                    if (machine.DefaultState == null) {
                        foreach (var edge in edges)
                            if (edge.from == entryNode) {
                                entry = edge.to;
                                break;
                            }
                    } else if (uniqueEdges.Add((entryNode, entry)))
                        edges.Add(new Edge(entryNode, entry));

                    var ranks = GetRanks(outgoing, incoming, entry);
                    groups = GetGroups(outgoing, incoming, entry);
                    foreach (var group in groups) {
                        var byRank = CollectionPool<SortedDictionary<int, List<int>>, KeyValuePair<int, List<int>>>.Get();
                        levels.Add(byRank);
                        foreach (int node in group) {
                            if (!byRank.TryGetValue(ranks[node], out var level))
                                byRank.Add(ranks[node], level = ListPool<int>.Get());
                            level.Add(node);
                        }
                    }
                    OrderLevels(levels, outgoing, incoming, count);
                    var horizontal = CreateCandidate(levels, edges, count, entry, true);
                    var vertical = CreateCandidate(levels, edges, count, entry, false);
                    RefineCandidate(horizontal, edges, count, true);
                    RefineCandidate(vertical, edges, count, false);
                    var chosen = Better(vertical, horizontal) ? vertical : horizontal;

                    var updated = states.ToBuilder();
                    for (int i = 0; i < states.Count; i++) {
                        var child = updated[i];
                        child.Position = chosen.positions[i];
                        updated[i] = child;
                    }
                    machine.States = updated.ToImmutable();
                    var updatedMachines = machines.ToBuilder();
                    for (int i = 0; i < machines.Count; i++) {
                        var child = updatedMachines[i];
                        child.Position = chosen.positions[states.Count + i];
                        updatedMachines[i] = child;
                    }
                    machine.StateMachines = updatedMachines.ToImmutable();
                    machine.ExitPosition = chosen.positions[exit];
                    machine.EntryPosition = chosen.positions[entryNode];
                    machine.AnyStatePosition = chosen.positions[any];
                    if (hasParent) machine.ParentStateMachinePosition = chosen.positions[parent];
                } finally {
                    ReleaseAdjacency(outgoing);
                    ReleaseAdjacency(incoming);
                    if (groups != null) {
                        foreach (var group in groups) ListPool<int>.Release(group);
                        ListPool<List<int>>.Release(groups);
                    }
                    foreach (var byRank in levels) {
                        foreach (var level in byRank.Values) ListPool<int>.Release(level);
                        CollectionPool<SortedDictionary<int, List<int>>, KeyValuePair<int, List<int>>>.Release(byRank);
                    }
                }
            }
        }

        static bool HasExternalTransitions(
            VirtualStateMachine machine, List<VirtualStateMachine> hierarchy,
            Dictionary<VirtualState, int> indices, Dictionary<VirtualStateMachine, int> machineIndices
        ) {
            foreach (var current in hierarchy) {
                foreach (var state in current.States)
                    foreach (var transition in state.State.Transitions)
                        if (CrossesBoundary(indices.ContainsKey(state.State), transition, indices, machineIndices))
                            return true;
                bool inside = current == machine || machineIndices.ContainsKey(current);
                foreach (var transition in current.EntryTransitions)
                    if (CrossesBoundary(inside, transition, indices, machineIndices)) return true;
                foreach (var transition in current.AnyStateTransitions)
                    if (CrossesBoundary(inside, transition, indices, machineIndices)) return true;
                foreach (var child in current.StateMachines)
                    if (current.StateMachineTransitions.TryGetValue(child.StateMachine, out var transitions))
                        foreach (var transition in transitions)
                            if (CrossesBoundary(machineIndices.ContainsKey(child.StateMachine), transition, indices, machineIndices))
                                return true;
            }
            return false;
        }

        static bool CrossesBoundary(
            bool sourceInside, VirtualTransitionBase transition,
            Dictionary<VirtualState, int> indices, Dictionary<VirtualStateMachine, int> machineIndices
        ) {
            if (transition.IsExit) return false;
            if (transition.DestinationState != null)
                return sourceInside != indices.ContainsKey(transition.DestinationState);
            if (transition.DestinationStateMachine != null)
                return sourceInside != machineIndices.ContainsKey(transition.DestinationStateMachine);
            return false;
        }

        static void AddTransition(
            int source, VirtualTransitionBase transition, bool includeExit, bool sourceInside, bool hasParent, int parent,
            Dictionary<VirtualState, int> indices, Dictionary<VirtualStateMachine, int> machineIndices,
            List<Edge> edges, HashSet<(int, int)> uniqueEdges, List<int>[] outgoing, List<int>[] incoming, int exit
        ) {
            int target;
            if (transition.IsExit) {
                if (!includeExit) return;
                target = exit;
            } else if (transition.DestinationState != null && indices.TryGetValue(transition.DestinationState, out var stateIndex))
                target = stateIndex;
            else if (transition.DestinationStateMachine != null &&
                machineIndices.TryGetValue(transition.DestinationStateMachine, out var machineIndex))
                target = machineIndex;
            else if (hasParent && sourceInside &&
                (transition.DestinationState != null || transition.DestinationStateMachine != null))
                target = parent;
            else return;
            if (!sourceInside && target == parent) return;
            if (!uniqueEdges.Add((source, target))) return;
            edges.Add(new Edge(source, target));
            if (source >= exit || target == exit || target == source) return;
            outgoing[source].Add(target);
            incoming[target].Add(source);
        }

        static List<int>[] NewAdjacency(int count) {
            var result = new List<int>[count];
            for (int i = 0; i < count; i++) result[i] = ListPool<int>.Get();
            return result;
        }

        static void ReleaseAdjacency(List<int>[] adjacency) {
            if (adjacency == null) return;
            foreach (var neighbors in adjacency) ListPool<int>.Release(neighbors);
        }

        static int[] GetRanks(List<int>[] outgoing, List<int>[] incoming, int entry) {
            int count = outgoing.Length;
            using (ListPool<int>.Get(out var order))
            using (ListPool<(int node, int next)>.Get(out var stack))
            using (ListPool<int>.Get(out var pending))
            using (ListPool<int>.Get(out var queue))
            using (HashSetPool<(int, int)>.Get(out var unique))
            using (CollectionPool<SortedSet<int>, int>.Get(out var ready)) {
                var visited = new bool[count];
                for (int start = 0; start < count; start++) {
                    if (visited[start]) continue;
                    visited[start] = true;
                    stack.Add((start, 0));
                    while (stack.Count > 0) {
                        int last = stack.Count - 1;
                        var (node, next) = stack[last];
                        stack.RemoveAt(last);
                        if (next == outgoing[node].Count) {
                            order.Add(node);
                            continue;
                        }
                        stack.Add((node, next + 1));
                        int child = outgoing[node][next];
                        if (visited[child]) continue;
                        visited[child] = true;
                        stack.Add((child, 0));
                    }
                }

                var component = new int[count];
                for (int i = 0; i < count; i++) component[i] = -1;
                int componentCount = 0;
                for (int i = order.Count - 1; i >= 0; i--) {
                    int start = order[i];
                    if (component[start] >= 0) continue;
                    component[start] = componentCount;
                    pending.Add(start);
                    while (pending.Count > 0) {
                        int last = pending.Count - 1;
                        int node = pending[last];
                        pending.RemoveAt(last);
                        foreach (int child in incoming[node]) {
                            if (component[child] >= 0) continue;
                            component[child] = componentCount;
                            pending.Add(child);
                        }
                    }
                    componentCount++;
                }

                List<int>[] members = null, componentEdges = null;
                try {
                    members = NewAdjacency(componentCount);
                    for (int i = 0; i < count; i++) members[component[i]].Add(i);
                    componentEdges = NewAdjacency(componentCount);
                    var indegree = new int[componentCount];
                    for (int i = 0; i < count; i++)
                        foreach (int target in outgoing[i]) {
                            int from = component[i], to = component[target];
                            if (from == to || !unique.Add((from, to))) continue;
                            componentEdges[from].Add(to);
                            indegree[to]++;
                        }
                    for (int i = 0; i < componentCount; i++)
                        if (indegree[i] == 0) ready.Add(i);
                    var componentRanks = new int[componentCount];
                    while (ready.Count > 0) {
                        int current = ready.Min;
                        ready.Remove(current);
                        foreach (int target in componentEdges[current]) {
                            componentRanks[target] = Mathf.Max(
                                componentRanks[target],
                                componentRanks[current] + members[current].Count
                            );
                            if (--indegree[target] == 0) ready.Add(target);
                        }
                    }

                    var result = new int[count];
                    var ranked = new bool[count];
                    for (int c = 0; c < componentCount; c++) {
                        int start = members[c][0];
                        if (component[entry] == c) start = entry;
                        else
                            foreach (int node in members[c]) {
                                bool hasExternalSource = false;
                                foreach (int source in incoming[node])
                                    if (component[source] != c) {
                                        hasExternalSource = true;
                                        break;
                                    }
                                if (!hasExternalSource) continue;
                                start = node;
                                break;
                            }
                        int offset = 0;
                        ranked[start] = true;
                        queue.Clear();
                        queue.Add(start);
                        for (int head = 0; head < queue.Count; head++) {
                            int node = queue[head];
                            result[node] = componentRanks[c] + offset++;
                            foreach (int target in outgoing[node]) {
                                if (component[target] != c || ranked[target]) continue;
                                ranked[target] = true;
                                queue.Add(target);
                            }
                        }
                    }
                    return result;
                } finally {
                    ReleaseAdjacency(members);
                    ReleaseAdjacency(componentEdges);
                }
            }
        }

        static List<List<int>> GetGroups(List<int>[] outgoing, List<int>[] incoming, int entry) {
            int count = outgoing.Length;
            var groups = ListPool<List<int>>.Get();
            bool completed = false;
            try {
                using (ListPool<int>.Get(out var pending)) {
                    var seen = new bool[count];
                    for (int n = -1; n < count; n++) {
                        int start = n < 0 ? entry : n;
                        if (seen[start]) continue;
                        var group = ListPool<int>.Get();
                        groups.Add(group);
                        seen[start] = true;
                        pending.Clear();
                        pending.Add(start);
                        for (int head = 0; head < pending.Count; head++) {
                            int node = pending[head];
                            group.Add(node);
                            EnqueueUnseen(outgoing[node], seen, pending);
                            EnqueueUnseen(incoming[node], seen, pending);
                        }
                        group.Sort();
                    }
                }
                completed = true;
                return groups;
            } finally {
                if (!completed) {
                    foreach (var group in groups) ListPool<int>.Release(group);
                    ListPool<List<int>>.Release(groups);
                }
            }
        }

        static void EnqueueUnseen(List<int> neighbors, bool[] seen, List<int> pending) {
            foreach (int neighbor in neighbors) {
                if (seen[neighbor]) continue;
                seen[neighbor] = true;
                pending.Add(neighbor);
            }
        }

        static void OrderLevels(List<SortedDictionary<int, List<int>>> groups, List<int>[] outgoing, List<int>[] incoming, int count) {
            var position = new int[count];
            var oldPosition = new int[count];
            var means = new float[count];
            var comparer = new LevelComparer(means, oldPosition);
            foreach (var group in groups) using (ListPool<List<int>>.Get(out var levels)) {
                levels.AddRange(group.Values);
                for (int sweep = 0; sweep < sweeps; sweep++) {
                    foreach (var level in levels)
                        for (int i = 0; i < level.Count; i++) position[level[i]] = i;
                    bool forward = sweep % 2 == 0;
                    for (int step = 0; step < levels.Count; step++) {
                        var level = levels[forward ? step : levels.Count - step - 1];
                        var neighbors = forward ? incoming : outgoing;
                        for (int i = 0; i < level.Count; i++) {
                            int node = level[i];
                            oldPosition[node] = i;
                            means[node] = Mean(neighbors[node], position, i);
                        }
                        level.Sort(comparer);
                        for (int i = 0; i < level.Count; i++) position[level[i]] = i;
                    }
                }
            }
        }

        static float Mean(List<int> neighbors, int[] position, int fallback) {
            if (neighbors.Count == 0) return fallback;
            double sum = 0;
            foreach (int neighbor in neighbors) sum += position[neighbor];
            return (float)(sum / neighbors.Count);
        }

        static Candidate CreateCandidate(List<SortedDictionary<int, List<int>>> groups, List<Edge> edges, int count, int entry, bool horizontal) {
            var result = new Candidate(count);
            int groupOffset = 0;
            float maxX = 0, maxY = 0;
            foreach (var group in groups) {
                int maxLevel = 0;
                foreach (var level in group.Values) maxLevel = Mathf.Max(maxLevel, level.Count);
                foreach (var pair in group)
                    for (int i = 0; i < pair.Value.Count; i++) {
                        float cross = groupOffset + (maxLevel - pair.Value.Count) * 0.5f + i;
                        var position = horizontal
                            ? new Vector3(columnSpacing * (pair.Key + 1), rowSpacing * (cross + 1), 0)
                            : new Vector3(columnSpacing * (cross + 1), rowSpacing * (pair.Key + 1), 0);
                        result.positions[pair.Value[i]] = position;
                        maxX = Mathf.Max(maxX, position.x);
                        maxY = Mathf.Max(maxY, position.y);
                    }
                groupOffset += maxLevel + 1;
            }
            var entryPosition = result.positions[entry];
            result.positions[count + 1] = horizontal ?
                new Vector3(0, entryPosition.y, 0) :
                new Vector3(entryPosition.x, 0, 0);
            result.positions[count + 2] = horizontal ?
                new Vector3(0, entryPosition.y - rowSpacing, 0) :
                new Vector3(entryPosition.x - columnSpacing, 0, 0);

            float exitCross = 0;
            int exits = 0;
            foreach (var edge in edges)
                if (edge.to == count) {
                    exitCross += horizontal ?
                        result.positions[edge.from].y :
                        result.positions[edge.from].x;
                    exits++;
                }
            exitCross = exits == 0 ? (horizontal ? entryPosition.y : entryPosition.x) : exitCross / exits;
            result.positions[count] = horizontal ?
                new Vector3(maxX + columnSpacing, exitCross, 0) :
                new Vector3(exitCross, maxY + rowSpacing, 0);
            Score(result, edges);
            return result;
        }

        static void RefineCandidate(Candidate candidate, List<Edge> edges, int count, bool horizontal) {
            if (candidate.collisions == 0 && candidate.crossings == 0) return;
            var trial = new Candidate(candidate.positions);
            int attempts = 0;
            bool improved;
            do {
                improved = false;
                for (int i = 0; i < count && attempts < refinementLimit; i++) {
                    var original = candidate.positions[i];
                    var bestPosition = original;
                    int oldCollisions = candidate.collisions, oldCrossings = candidate.crossings;
                    for (int direction = -1; direction <= 1 && attempts < refinementLimit; direction += 2) {
                        attempts++;
                        candidate.positions[i] = original + (horizontal ?
                            new Vector3(0, direction * rowSpacing, 0) :
                            new Vector3(direction * columnSpacing, 0, 0));
                        if (OverlapsAnotherNode(candidate.positions, i)) continue;
                        Score(trial, edges);
                        if (!Better(trial, candidate) ||
                            trial.collisions == oldCollisions && trial.crossings == oldCrossings) continue;
                        bestPosition = candidate.positions[i];
                        candidate.collisions = trial.collisions;
                        candidate.crossings = trial.crossings;
                        candidate.aspect = trial.aspect;
                    }
                    candidate.positions[i] = bestPosition;
                    if (bestPosition != original) improved = true;
                }
            } while (improved && attempts < refinementLimit &&
                (candidate.collisions > 0 || candidate.crossings > 0));
        }

        static bool OverlapsAnotherNode(Vector3[] positions, int index) {
            for (int i = 0; i < positions.Length; i++)
                if (i != index &&
                    Mathf.Abs(positions[i].x - positions[index].x) < nodeWidth &&
                    Mathf.Abs(positions[i].y - positions[index].y) < nodeHeight)
                    return true;
            return false;
        }

        static void Score(Candidate candidate, List<Edge> edges) {
            var positions = candidate.positions;
            candidate.collisions = 0;
            candidate.crossings = 0;
            int checks = 0;
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++) {
                var edge = edges[edgeIndex % 2 == 0 ? edgeIndex / 2 : edges.Count - 1 - edgeIndex / 2];
                if (edge.from == edge.to) continue;
                var a = Center(positions[edge.from]);
                var b = Center(positions[edge.to]);
                for (int i = 0; i < positions.Length && checks < scoreLimit; i++) {
                    checks++;
                    if (i == edge.from || i == edge.to) continue;
                    if (IntersectsNode(a, b, positions[i])) candidate.collisions++;
                }
                if (checks >= scoreLimit) break;
            }
            checks = 0;
            for (int i = 0; i < edges.Count && checks < scoreLimit; i++)
                for (int j = i + 1; j < edges.Count && checks < scoreLimit; j++) {
                    var a = edges[i];
                    var b = edges[j];
                    checks++;
                    if (a.from == a.to ||
                        b.from == b.to ||
                        a.from == b.from ||
                        a.from == b.to ||
                        a.to == b.from ||
                        a.to == b.to) continue;
                    if (Crosses(
                        Center(positions[a.from]),
                        Center(positions[a.to]),
                        Center(positions[b.from]),
                        Center(positions[b.to])
                    )) candidate.crossings++;
                }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var position in positions) {
                minX = Mathf.Min(minX, position.x);
                minY = Mathf.Min(minY, position.y);
                maxX = Mathf.Max(maxX, position.x + nodeWidth);
                maxY = Mathf.Max(maxY, position.y + nodeHeight);
            }
            float width = maxX - minX, height = maxY - minY;
            candidate.aspect = Mathf.Max(width / height, height / width);
        }

        static Vector2 Center(Vector3 position) =>
            new Vector2(position.x + nodeWidth * 0.5f, position.y + nodeHeight * 0.5f);

        static bool IntersectsNode(Vector2 a, Vector2 b, Vector3 position) {
            float min = 0, max = 1;
            return Clip(a.x, b.x - a.x, position.x, position.x + nodeWidth, ref min, ref max) &&
                Clip(a.y, b.y - a.y, position.y, position.y + nodeHeight, ref min, ref max);
        }

        static bool Clip(float origin, float delta, float lower, float upper, ref float min, ref float max) {
            if (Mathf.Approximately(delta, 0)) return origin >= lower && origin <= upper;
            float first = (lower - origin) / delta, second = (upper - origin) / delta;
            if (first > second) (first, second) = (second, first);
            min = Mathf.Max(min, first);
            max = Mathf.Min(max, second);
            return min <= max;
        }

        static float Side(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;

        static bool Better(Candidate a, Candidate b) =>
            a.collisions != b.collisions ? a.collisions < b.collisions :
            a.crossings != b.crossings ? a.crossings < b.crossings :
            a.aspect < b.aspect;

        readonly struct Edge {
            public readonly int from, to;

            public Edge(int from, int to) {
                this.from = from;
                this.to = to;
            }
        }

        sealed class Candidate {
            public readonly Vector3[] positions;
            public int collisions, crossings;
            public float aspect;

            public Candidate(int count) {
                positions = new Vector3[count + 3];
            }

            public Candidate(Vector3[] positions) {
                this.positions = positions;
            }
        }

        sealed class LevelComparer : IComparer<int> {
            readonly float[] means;
            readonly int[] oldPosition;

            public LevelComparer(float[] means, int[] oldPosition) {
                this.means = means;
                this.oldPosition = oldPosition;
            }

            public int Compare(int a, int b) {
                int comparison = means[a].CompareTo(means[b]);
                return comparison != 0 ? comparison : oldPosition[a].CompareTo(oldPosition[b]);
            }
        }
    }
}
