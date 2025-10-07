using System;
using nadena.dev.ndmf.animator;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
#endif

namespace JLChnToZ.NDExtensions.Editors {
    readonly struct TranslateRotate {
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly Vector3 scale;
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
                    scale = transform.localScale;
                } else {
#if UNITY_2021_3_OR_NEWER
                    transform.GetPositionAndRotation(out position, out rotation);
#else
                    position = transform.position;
                    rotation = transform.rotation;
#endif
                    scale = transform.lossyScale;
                }
            } else if (component is BoxCollider bc) {
                if (isLocal) {
                    position = bc.center;
                    rotation = Quaternion.identity;
                    scale = bc.size;
                } else {
                    position = bc.transform.TransformPoint(bc.center);
                    rotation = bc.transform.rotation;
                    scale = Vector3.Scale(bc.transform.lossyScale, bc.size);
                }
            } else if (component is SphereCollider sc) {
                if (isLocal) {
                    position = sc.center;
                    rotation = Quaternion.identity;
                    scale = Vector3.one * sc.radius;
                } else {
                    position = sc.transform.TransformPoint(sc.center);
                    rotation = sc.transform.rotation;
                    scale = sc.transform.TransformVector(Vector3.one * sc.radius);
                }
            } else if (component is CapsuleCollider cc) {
                scale = cc.direction switch {
                    0 => new Vector3(cc.height, cc.radius, cc.radius),
                    1 => new Vector3(cc.radius, cc.height, cc.radius),
                    _ => new Vector3(cc.radius, cc.radius, cc.height)
                };
                if (isLocal) {
                    position = cc.center;
                    rotation = Quaternion.identity;
                } else {
                    position = cc.transform.TransformPoint(cc.center);
                    rotation = cc.transform.rotation;
                    scale = cc.transform.TransformVector(scale);
                }
            }
#if VRC_SDK_VRCSDK3
            else if (component is ContactBase contact) {
                switch (contact.shapeType) {
                    case ContactBase.ShapeType.Capsule:
                        scale = new Vector3(contact.radius, contact.height, contact.radius);
                        break;
                    case ContactBase.ShapeType.Sphere:
                        scale = Vector3.one * contact.radius;
                        break;
                    default:
                        scale = Vector3.one;
                        break;
                }
                if (isLocal) {
                    position = contact.position;
                    rotation = contact.rotation;
                } else {
                    transform = contact.GetRootTransform();
                    position = transform.TransformPoint(contact.position);
                    rotation = transform.rotation * contact.rotation;
                    scale = transform.TransformVector(scale);
                }
            } else if (component is VRCPhysBoneColliderBase pbc) {
                switch (pbc.shapeType) {
                    case VRCPhysBoneColliderBase.ShapeType.Capsule:
                        scale = new Vector3(pbc.radius, pbc.height, pbc.radius);
                        break;
                    case VRCPhysBoneColliderBase.ShapeType.Sphere:
                        scale = Vector3.one * pbc.radius;
                        break;
                    default:
                        scale = Vector3.one;
                        break;
                }
                if (isLocal) {
                    position = pbc.position;
                    rotation = pbc.rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    position = transform.TransformPoint(pbc.position);
                    rotation = transform.rotation * pbc.rotation;
                    scale = transform.TransformVector(scale);
                }
            } else if (component is VRCConstraintBase vrcConstraint) {
                var source = vrcConstraint.Sources[refId];
                if (isLocal) {
                    position = source.ParentPositionOffset;
                    rotation = Quaternion.identity;
                    scale = Vector3.one;
                } else {
                    transform = source.SourceTransform;
                    position = transform.TransformPoint(source.ParentPositionOffset);
                    rotation = transform.rotation;
                    scale = transform.lossyScale;
                }
            }
#endif
            else {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
            }
            this.isLocal = isLocal;
        }

        public void ApplyTo(Component component, int refId = -1, AnimationIndex animIndex = null, Transform root = null, bool restoreScale = true) {
            if (component is Transform transform) {
                if (animIndex != null)
                    animIndex.SetTransformPositionAndRotation(root, transform, position, rotation, null, isLocal);
                else if (isLocal) {
#if UNITY_2021_3_OR_NEWER
                    transform.SetLocalPositionAndRotation(position, rotation);
#else
                    transform.localPosition = position;
                    transform.localRotation = rotation;
#endif
                    if (restoreScale) transform.localScale = scale;
                } else {
                    transform.SetPositionAndRotation(position, rotation);
                    if (restoreScale) transform.localScale = SafeDivide(scale, transform.lossyScale);
                }
                return;
            }
            if (component is BoxCollider bc) {
                if (isLocal) {
                    bc.center = position;
                    if (restoreScale) bc.size = scale;
                } else {
                    bc.center = bc.transform.InverseTransformPoint(position);
                    if (restoreScale) bc.size = SafeDivide(scale, bc.transform.lossyScale);
                }
                return;
            }
            if (component is SphereCollider sc) {
                var s = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                if (isLocal) {
                    sc.center = position;
                    sc.radius = s;
                } else {
                    sc.center = sc.transform.InverseTransformPoint(position);
                    if (restoreScale) {
                        var lossyScale = sc.transform.lossyScale;
                        sc.radius = s / Mathf.Max(lossyScale.x, Mathf.Max(lossyScale.y, lossyScale.z));
                    }
                }
                return;
            }
            if (component is CapsuleCollider cc) {
                var s = scale;
                if (isLocal) {
                    cc.center = position;
                } else {
                    cc.center = cc.transform.InverseTransformPoint(position);
                    if (restoreScale) s = SafeDivide(scale, cc.transform.lossyScale);
                }
                if (restoreScale)
                    switch (cc.direction) {
                        case 0:
                            cc.height = s.x;
                            cc.radius = Mathf.Max(s.y, s.z);
                            break;
                        case 1:
                            cc.height = s.y;
                            cc.radius = Mathf.Max(s.x, s.z);
                            break;
                        default:
                            cc.height = s.z;
                            cc.radius = Mathf.Max(s.x, s.y);
                            break;
                    }
                return;
            }
#if VRC_SDK_VRCSDK3
            if (component is ContactBase contact) {
                var s = scale;
                if (isLocal) {
                    contact.position = position;
                    contact.rotation = rotation;
                } else {
                    transform = contact.GetRootTransform();
                    contact.position = transform.InverseTransformPoint(position);
                    contact.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                    if (restoreScale) s = SafeDivide(scale, transform.lossyScale);
                }
                if (restoreScale)
                    switch (contact.shapeType) {
                        case ContactBase.ShapeType.Capsule:
                            contact.height = s.y;
                            contact.radius = Mathf.Max(s.x, s.z);
                            break;
                        case ContactBase.ShapeType.Sphere:
                            contact.radius = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                            break;
                    }
                return;
            }
            if (component is VRCPhysBoneColliderBase pbc) {
                var s = scale;
                if (isLocal) {
                    pbc.position = position;
                    pbc.rotation = rotation;
                } else {
                    transform = pbc.GetRootTransform();
                    pbc.position = transform.InverseTransformPoint(position);
                    pbc.rotation = Quaternion.Inverse(transform.rotation) * rotation;
                    if (restoreScale) {
                        var lossyScale = transform.lossyScale;
                        s = SafeDivide(scale, lossyScale);
                    }
                }
                if (restoreScale)
                    switch (pbc.shapeType) {
                        case VRCPhysBoneColliderBase.ShapeType.Capsule:
                            pbc.height = s.y;
                            pbc.radius = Mathf.Max(s.x, s.z);
                            break;
                        case VRCPhysBoneColliderBase.ShapeType.Sphere:
                            pbc.radius = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                            break;
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

        static Vector3 SafeDivide(Vector3 a, Vector3 b) => new(
            b.x != 0 ? a.x / b.x : 0,
            b.y != 0 ? a.y / b.y : 0,
            b.z != 0 ? a.z / b.z : 0
        );
    }
}