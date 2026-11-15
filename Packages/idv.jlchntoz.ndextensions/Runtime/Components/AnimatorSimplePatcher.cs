using System;
using System.Collections.Generic;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Animator Simple Patcher")]
    [HelpURL("https://hackmd.io/@JLChnToZ/animator-simple-patcher")]
    public sealed class AnimatorSimplePatcher : TagComponent {
        #if VRC_SDK_VRCSDK3
        public VRCAvatarDescriptor.AnimLayerType layer = VRCAvatarDescriptor.AnimLayerType.FX;
        #endif
        public int priority;
        [Multiline] public string code;
        public ExtraReference[] extraReferences;
        [NonSerialized] public Exception lastException;

        public void GetAllReferences(Dictionary<string, UnityObject> references) {
            if (extraReferences == null) return;
            foreach (var entry in extraReferences) {
                var name = entry.name;
                if (string.IsNullOrEmpty(name)) {
                    if (entry.reference == null) continue;
                    name = entry.reference.name;
                }
                references.TryAdd(name, entry.reference);
            }
        }

        [Serializable]
        public struct ExtraReference {
            public string name;
            public UnityObject reference;
        }
    }
}
