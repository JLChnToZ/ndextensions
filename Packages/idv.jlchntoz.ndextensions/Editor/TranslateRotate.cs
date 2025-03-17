using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    readonly struct TranslateRotate {
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly bool isLocal;

        public TranslateRotate(Component component, bool isLocal = false, int refId = -1) {
            if (component is Transform transform) {
                if (isLocal) {
#if UNITY_2021_3_OR_NEWER
                    transform.GetLocalPositionAndRotation(out position, out rotation);
#else
                    position = transform.localPosition;
                    rotation = transform.localRotation;
#endif
                } else {
#if UNITY_2021_3_OR_NEWER
                    transform.GetPositionAndRotation(out position, out rotation);
#else
                    position = transform.position;
                    rotation = transform.rotation;
#endif
                }
            } else if (component is BoxCollider bc) {
                if (isLocal) {
                    position = bc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = bc.transform.TransformPoint(bc.center);
                    rotation = bc.transform.rotation;
                }
            } else if (component is SphereCollider sc) {
                if (isLocal) {
                    position = sc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = sc.transform.TransformPoint(sc.center);
                    rotation = sc.transform.rotation;
                }
            } else if (component is CapsuleCollider cc) {
                if (isLocal) {
                    position = cc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = cc.transform.TransformPoint(cc.center);
                    rotation = cc.transform.rotation;
                }
            }
#if VRC_SDK_VRCSDK3
            else if (component is ContactBase contact) {
                if (isLocal) {
                    position = contact.position;
                    rotation = contact.rotation;
                } else {
                    transform = contact.GetRootTransform();
                    position = transform.TransformPoint(contact.position);
                    rotation = transform.rotation * contact.rotation;
                }
            } else if (component is VRCPhysBoneColliderBase pbc) {
                if (isLocal) {
                    position = pbc.position;
                    rotation = pbc.rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    position = transform.TransformPoint(pbc.position);
                    rotation = transform.rotation * pbc.rotation;
                }
            } else if (component is VRCConstraintBase vrcConstraint) {
                var source = vrcConstraint.Sources[refId];
                if (isLocal) {
                    position = source.ParentPositionOffset;
                    rotation = Quaternion.identity;
                } else {
                    transform = source.SourceTransform;
                    position = transform.TransformPoint(source.ParentPositionOffset);
                    rotation = transform.rotation;
                }
            }
#endif
            else {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
            this.isLocal = isLocal;
        }

        public void ApplyTo(Component component, int refId = -1) {
            if (component is Transform transform) {
                if (isLocal) {
#if UNITY_2021_3_OR_NEWER
                    transform.SetLocalPositionAndRotation(position, rotation);
#else
                    transform.localPosition = position;
                    transform.localRotation = rotation;
#endif
                } else
                    transform.SetPositionAndRotation(position, rotation);
                return;
            }
            if (component is BoxCollider bc) {
                if (isLocal) bc.center = position;
                else bc.center = bc.transform.InverseTransformPoint(position);
                return;
            }
            if (component is SphereCollider sc) {
                if (isLocal) sc.center = position;
                else sc.center = sc.transform.InverseTransformPoint(position);
                return;
            }
            if (component is CapsuleCollider cc) {
                if (isLocal) cc.center = position;
                else cc.center = cc.transform.InverseTransformPoint(position);
                return;
            }
#if VRC_SDK_VRCSDK3
            if (component is ContactBase contact) {
                if (isLocal) {
                    contact.position = position;
                    contact.rotation = rotation;
                } else {
                    transform = contact.GetRootTransform();
                    contact.position = transform.InverseTransformPoint(position);
                    contact.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                }
                return;
            }
            if (component is VRCPhysBoneColliderBase pbc) {
                if (isLocal) {
                    pbc.position = position;
                    pbc.rotation = rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    pbc.position = transform.InverseTransformPoint(position);
                    pbc.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                }
                return;
            }
            if (component is VRCConstraintBase vrcConstraint) {
                var src = vrcConstraint.Sources[refId];
                if (isLocal)
                    src.ParentPositionOffset = position;
                else {
                    transform = src.SourceTransform;
                    src.ParentPositionOffset = transform.InverseTransformPoint(position);
                    src.ParentRotationOffset += (Quaternion.Inverse(transform.rotation) * rotation).eulerAngles;
                }
                vrcConstraint.Sources[refId] = src;
                return;
            }
#endif
        }
    }
}