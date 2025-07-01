using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.NDExtensions.Editors {
    public class AnimationRelocator {
        readonly Transform root;
        readonly Dictionary<RuntimeAnimatorController, AnimatorOverrideController> controllerOverrides = new();
        readonly Dictionary<Component, HashSet<AnimationClip>> component2Anim = new();
        readonly Dictionary<AnimationClip, HashSet<AnimatorController>> dependencies = new();
        readonly Dictionary<AnimationClip, AnimationClip> clonedClips = new();
        readonly HashSet<AnimationClip> clonedClipsSet = new();

        public RuntimeAnimatorController this[RuntimeAnimatorController controller] =>
            controller != null &&
            controllerOverrides.TryGetValue(controller, out var overrideController) &&
            overrideController != null ? overrideController : controller;

        public AnimationClip this[AnimationClip clip] =>
            clip != null && clonedClips.TryGetValue(clip, out var clone) ? clone : clip;

        public ICollection<AnimationClip> OriginalClips => dependencies.Keys;

        public ICollection<AnimationClip> ClonedClips => clonedClips.Values;

        public ICollection<RuntimeAnimatorController> OriginalControllers => controllerOverrides.Keys;

        public ICollection<AnimatorOverrideController> OverrideControllers => controllerOverrides.Values;

        public AnimationRelocator(Transform root) => this.root = root;

        public void AddController(RuntimeAnimatorController baseController) {
            if (baseController == null || controllerOverrides.ContainsKey(baseController)) return;
            controllerOverrides[baseController] = null;
            var pending = new Stack<(RuntimeAnimatorController controller, UnityObject target)>();
            Stack<AnimatorOverrideController> overrideStack = null;
            HashSet<AnimationClip> allClips = null;
            AnimatorController rootController = null;
            pending.Push((baseController, baseController));
            while (pending.TryPop(out var entry)) {
                if (entry.target == null) continue;
                if (entry.target is AnimatorController controller) {
                    rootController = controller;
                    foreach (var layer in controller.layers)
                        pending.Push((controller, layer.stateMachine));
                    continue;
                }
                if (entry.target is AnimatorOverrideController overrideController) {
                    pending.Push((overrideController, overrideController.runtimeAnimatorController));
                    if (overrideStack == null) overrideStack = new();
                    if (allClips == null) allClips = new();
                    overrideStack.Push(overrideController);
                    continue;
                }
                if (entry.target is AnimatorStateMachine stateMachine) {
                    foreach (var subState in stateMachine.states)
                        pending.Push((entry.controller, subState.state));
                    foreach (var subStateMachine in stateMachine.stateMachines)
                        pending.Push((entry.controller, subStateMachine.stateMachine));
                    continue;
                }
                if (entry.target is AnimatorState state) {
                    pending.Push((entry.controller, state.motion));
                    continue;
                }
                if (entry.target is BlendTree blendTree) {
                    foreach (var childMotion in blendTree.children)
                        pending.Push((entry.controller, childMotion.motion));
                    continue;
                }
                if (entry.target is AnimationClip clip) {
                    allClips?.Add(clip);
                    if (!dependencies.TryGetValue(clip, out var controllers))
                        dependencies[clip] = controllers = new();
                    controllers.Add(entry.controller as AnimatorController);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        RecordClipUsage(clip, binding);
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        RecordClipUsage(clip, binding);
                    continue;
                }
            }
            if (overrideStack == null) return;
            var clipsStack = new Stack<AnimationClip>();
            while (overrideStack.TryPop(out var controller)) {
                foreach (var clip in allClips)
                    clipsStack.Push(clip);
                while (clipsStack.TryPop(out var clip)) {
                    var overrided = controller[clip];
                    if (overrided == null || overrided == clip) continue;
                    allClips.Add(overrided);
                    if (!dependencies.TryGetValue(overrided, out var controllers))
                        dependencies[overrided] = controllers = new();
                    controllers.Add(rootController);
                }
            }
        }

        void RecordClipUsage(AnimationClip clip, EditorCurveBinding binding) {
            var path = binding.path;
            if (string.IsNullOrEmpty(path)) return;
            var bone = root.Find(path);
            if (bone == null) return;
            var type = binding.type;
            if (!type.IsSubclassOf(typeof(Component))) return;
            var target = bone.GetComponent(type);
            if (target == null) return;
            if (!component2Anim.TryGetValue(target, out var clips))
                component2Anim[bone] = clips = new();
            clips.Add(clip);
        }

        public bool HasRelevantClips(Component component) =>
            component != null && component2Anim.ContainsKey(component);

        public IEnumerable<AnimationClip> GetRelevantClipsForEdit(Component component) {
            if (component == null || !component2Anim.TryGetValue(component, out var animSet))
                yield break;
            foreach (var clip in animSet) yield return GetClone(clip);
        }

        public AnimationClip GetClone(AnimationClip clip) {
            if (clonedClipsSet.Contains(clip)) return clip;
            if (clonedClips.TryGetValue(clip, out var clone)) return clone;
            clonedClips[clip] = clone = Instantiate(clip);
            clone.name = $"{clip.name} Modified";
            if (dependencies.TryGetValue(clip, out var depdControllers))
                foreach (var controller in depdControllers) {
                    if (controller == null) continue;
                    if (!controllerOverrides.TryGetValue(controller, out var overrideController) || overrideController == null)
                        controllerOverrides[controller] = overrideController = new AnimatorOverrideController {
                            name = $"{controller.name} Override",
                            runtimeAnimatorController = controller,
                        };
                    overrideController[clip] = clone;
                    clonedClipsSet.Add(clone);
                }
            return clone;
        }
    }
}