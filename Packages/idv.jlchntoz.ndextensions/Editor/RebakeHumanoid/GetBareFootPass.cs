using UnityEngine;
using nadena.dev.ndmf;

namespace JLChnToZ.NDExtensions.Editors {
    class GetBareFootPass : Pass<GetBareFootPass> {
        protected override void Execute(BuildContext ctx) {
            if (!ctx.AvatarRootObject.TryGetComponent(out RebakeHumanoid declaration) ||
                declaration.floorAdjustment != FloorAdjustmentMode.BareFeetToGround)
                return;
            var extCtx = ctx.Extension<RebakeHumanoidContext>();
            float yOffset = 0F;
            if (declaration.rendererWithBareFeet != null)
                yOffset = SoleResolver.FindOffset(declaration, ctx.AvatarRootObject, new[] { declaration.rendererWithBareFeet });
            else {
                var leftFoot = declaration.GetBoneTransform(HumanBodyBones.LeftToes);
                var rightFoot = declaration.GetBoneTransform(HumanBodyBones.RightToes);
                if (leftFoot == null || rightFoot == null) {
                    leftFoot = declaration.GetBoneTransform(HumanBodyBones.LeftFoot);
                    rightFoot = declaration.GetBoneTransform(HumanBodyBones.RightFoot);
                }
                if (leftFoot != null) {
                    if (rightFoot != null)
                        yOffset = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
                    else
                        yOffset = leftFoot.position.y;
                } else if (rightFoot != null)
                    yOffset = rightFoot.position.y;
            }
            if (float.IsFinite(yOffset)) extCtx.BareFeetOffset = yOffset;
        }
    }
}