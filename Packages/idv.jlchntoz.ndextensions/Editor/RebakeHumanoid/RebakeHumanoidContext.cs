using UnityEngine;
using System.Runtime.CompilerServices;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    class RebakeHumanoidContext : IExtensionContext {
        static readonly ConditionalWeakTable<BuildContext, Context> innerContexts = new();
        Vector3 orgPos;
        Quaternion orgRot;
        Context innerContext;

        public float BareFeetOffset {
            get => innerContext.bareFeetOffset;
            set => innerContext.bareFeetOffset = value;
        }

        public void OnActivate(BuildContext context) {
            innerContext = innerContexts.GetOrCreateValue(context);
            var transform = context.AvatarRootTransform;
            transform.GetPositionAndRotation(out orgPos, out orgRot);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public void OnDeactivate(BuildContext context) {
            innerContext = null;
            context.AvatarRootTransform.SetPositionAndRotation(orgPos, orgRot);
        }

        class Context {
            public float bareFeetOffset;
        }
    }
}