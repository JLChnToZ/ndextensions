using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Deep Cleanup")]
    public sealed partial class DeepCleanup : MonoBehaviour { }

#if VRC_SDK_VRCSDK3
    public partial class DeepCleanup : VRC.SDKBase.IEditorOnly { }
#endif
}