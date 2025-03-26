using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    using static MecanimUtils;

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

        public static OverrideHumanDescription Default {
            get {
                var result = new OverrideHumanDescription {
                    mode = OverrideMode.Default,
                    humanLimits = new OverrideHumanLimits[HumanTrait.BoneCount],
                };
                result.ResetToDefault();
                return result;
            }
        }

        public void CopyFrom(HumanDescription desc) {
            armStretch = desc.armStretch;
            upperArmTwist = desc.upperArmTwist;
            lowerArmTwist = desc.lowerArmTwist;
            legStretch = desc.legStretch;
            lowerLegTwist = desc.lowerLegTwist;
            upperLegTwist = desc.upperLegTwist;
            feetSpacing = desc.feetSpacing;
            hasTranslationDoF = desc.hasTranslationDoF;
        }

        public void ResetToDefault() {
            armStretch = defaultHumanDescription.armStretch;
            upperArmTwist = defaultHumanDescription.upperArmTwist;
            lowerArmTwist = defaultHumanDescription.lowerArmTwist;
            legStretch = defaultHumanDescription.legStretch;
            lowerLegTwist = defaultHumanDescription.lowerLegTwist;
            upperLegTwist = defaultHumanDescription.upperLegTwist;
            feetSpacing = defaultHumanDescription.feetSpacing;
            hasTranslationDoF = defaultHumanDescription.hasTranslationDoF;
        }

        public void EnsureHumanLimits() {
            int boneCount = HumanTrait.BoneCount;
            if (humanLimits == null)
                humanLimits = new OverrideHumanLimits[boneCount];
            else if (humanLimits.Length != boneCount)
                Array.Resize(ref humanLimits, boneCount);
        }
    }

    [Serializable]
    public struct OverrideHumanLimits {
        public OverrideMode mode;
        public Vector3 min, max, center;

        public OverrideHumanLimits(HumanLimit limit, OverrideMode overrideMode = OverrideMode.Override) {
            mode = limit.useDefaultValues ? OverrideMode.Default : overrideMode;
            min = limit.min;
            max = limit.max;
            center = limit.center;
        }
    }

    public enum OverrideMode : byte {
        [InspectorName("Inherit from existing avatar")]
        Inherit,
        [InspectorName("Use default values")]
        Default,
        [InspectorName("Override with custom values")]
        Override,
    }

    public static class OverrideHumanDescriptionUtils {
        public static OverrideHumanDescription Resolve(this OverrideHumanDescription? overrideHuman, Avatar reference = null) {
            var desc = overrideHuman ?? OverrideHumanDescription.Default;
            if (reference == null) return desc;
            var srcDesc = reference.humanDescription;
            switch (desc.mode) {
                case OverrideMode.Inherit:
                    desc.CopyFrom(srcDesc);
                    break;
                case OverrideMode.Default:
                    desc.ResetToDefault();
                    break;
            }
            desc.EnsureHumanLimits();
            var humanNames = HumanBoneNames;
            for (int i = 0; i < desc.humanLimits.Length; i++) {
                ref var limit = ref desc.humanLimits[i];
                if (limit.mode != OverrideMode.Inherit) continue;
                var humanName = humanNames[i];
                bool matched = false;
                if (srcDesc.human != null)
                    foreach (var otherHuman in srcDesc.human)
                        if (otherHuman.humanName == humanName) {
                            limit = new OverrideHumanLimits(otherHuman.limit);
                            matched = true;
                            break;
                        }
                if (!matched) limit.mode = OverrideMode.Default;
            }
            return desc;
        }
    }
}
