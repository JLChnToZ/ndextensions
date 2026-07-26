using UnityEngine;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidContext : IExtensionContext {
        float bareFeetOffset;
        string[] paths;
        Vector3 orgPos;
        Quaternion orgRot;

        public float BareFeetOffset {
            get => bareFeetOffset;
            set => bareFeetOffset = value;
        }

        public string[] Paths {
            get => paths;
            set => paths = value;
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