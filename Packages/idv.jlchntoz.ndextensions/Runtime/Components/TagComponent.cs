using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public abstract partial class TagComponent : MonoBehaviour { }

#if VRC_SDK_VRCSDK3
    public partial class TagComponent : VRC.SDKBase.IEditorOnly { }
#endif
}