using UnityEngine;
using System.Runtime.CompilerServices;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidContext : IExtensionContext {
        float bareFeetOffset;
        Vector3 orgPos;
        Quaternion orgRot;

        public float BareFeetOffset {
            get => bareFeetOffset;
            set => bareFeetOffset = value;
        }

        public void OnActivate(BuildContext context) {
            var transform = context.AvatarRootTransform;
            transform.GetPositionAndRotation(out orgPos, out orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public void OnDeactivate(BuildContext context) {
            context.AvatarRootTransform.SetPositionAndRotation(orgPos, orgRot);
        }
    }
}