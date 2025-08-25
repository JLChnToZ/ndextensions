using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Parameter Compressor (Experimental)")]
    public class ParameterCompressor : TagComponent {
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Synchronized)]
        public AnimatorParameterRef[] parameters;
        [Min(0)] public float threshold = 0.2F;
    }
}
