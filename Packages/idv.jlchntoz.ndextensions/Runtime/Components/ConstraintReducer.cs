using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [AddComponentMenu("JLChnToZ/Non-Destructive Extensions/Constraint Reducer")]
    public sealed partial class ConstraintReducer : MonoBehaviour { }

#if VRC_SDK_VRCSDK3
    public partial class ConstraintReducer : VRC.SDKBase.IEditorOnly { }
#endif
}