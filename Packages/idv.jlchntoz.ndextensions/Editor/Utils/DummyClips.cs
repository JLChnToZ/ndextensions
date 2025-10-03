using System;
using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class DummyClips {
        static DummyClips instance;
        readonly Dictionary<float, VirtualClip> clips = new();

        public static DummyClips Instance => instance ??= new DummyClips();

        public static DummyClips For(BuildContext buildContext) => buildContext.GetState<DummyClips>();

        public VirtualClip Get(float time = 0F) {
            if (!clips.TryGetValue(time, out var clip))
                clips[time] = clip = VirtualClip
                .Create($"Dummy Clip {time:0.###}s")
                .SetConstantClip<GameObject>($"__dummy_{Guid.NewGuid():N}", "m_IsActive", 1, time);
            return clip;
        }
    }
}