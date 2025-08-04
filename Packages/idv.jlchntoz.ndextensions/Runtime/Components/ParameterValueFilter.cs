using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Parameter Value Filter")]
    public sealed class ParameterValueFilter : TagComponent {
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Float)]
        public AnimatorParameterRef parameter;
        public float minValue = 0.0F;
        public float maxValue = 1.0F;
        public SmoothType smoothType = SmoothType.Exponential;
        [Min(0)] public float smoothValue = 0.01F;
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Float)]
        public AnimatorParameterRef smoothParameter;
        public bool timeBased = false;
        public bool remapValues = false;
        public float remapMin = 0.0F;
        public float remapMax = 1.0F;

        public SmoothFactor SmoothFactor => smoothParameter.IsValid ?
            new(smoothParameter.name, timeBased) :
            new(smoothValue, timeBased);
    }
}