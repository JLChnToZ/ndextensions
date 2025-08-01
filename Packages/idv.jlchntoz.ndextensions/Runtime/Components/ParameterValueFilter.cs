using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Parameter Value Filter")]
    public sealed class ParameterValueFilter : TagComponent {
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Float)]
        public AnimatorParameterRef parameter;
        public float minValue = 0.0F;
        public float maxValue = 1.0F;
        [Range(0, 1)] public float smoothValue = 0.01F;
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Float)]
        public AnimatorParameterRef smoothParameter;
        public bool remapValues = false;
        public float remapMin = 0.0F;
        public float remapMax = 1.0F;
    }
}