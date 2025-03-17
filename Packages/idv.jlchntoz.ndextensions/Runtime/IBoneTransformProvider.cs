using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public interface IBoneTransformProvider {
        Transform GetBoneTransform(HumanBodyBones bone);
    }

    [Serializable]
    public struct OverrideHumanDescription {
        public OverrideMode mode;
        [Range(0, 1)] public float armStretch;
        [Range(0, 1)] public float upperArmTwist;
        [Range(0, 1)] public float lowerArmTwist;
        [Range(0, 1)] public float legStretch;
        [Range(0, 1)] public float lowerLegTwist;
        [Range(0, 1)] public float upperLegTwist;
        [Range(0, 1)] public float feetSpacing;
        public bool hasTranslationDoF;
        public OverrideHumanLimits[] humanLimits;
    }

    [Serializable]
    public struct OverrideHumanLimits {
        public OverrideMode mode;
        public Vector3 min, max, center;
    }

    public enum OverrideMode : byte {
        [InspectorName("Inherit from existing avatar")]
        Inherit,
        [InspectorName("Use default values")]
        Default,
        [InspectorName("Override with custom values")]
        Override,
    }
}
