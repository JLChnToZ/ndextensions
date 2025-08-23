using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Parameter Compressor (Experimental)")]
    public class ParameterCompressor : TagComponent {
        [AnimatorParameterRefUsage(EnforcedType = ParameterType.Synchronized)]
        public AnimatorParameterRef[] parameters;
    }
}
