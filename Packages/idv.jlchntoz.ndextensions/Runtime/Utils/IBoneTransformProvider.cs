using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public interface IBoneTransformProvider {
        Transform GetBoneTransform(HumanBodyBones bone);
    }
}
