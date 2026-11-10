using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    public static class EasingFactory {
        public static AnimationCurve Create(EasingType easingType, float from = 0, float to = 1, float timeStart = 0, float timeEnd = 1) {
            var keyframes = new Keyframe[] { new(0, 0, 1, 1), new(1, 1, 1, 1) };
            ApplyEasing(keyframes, 0, easingType);
            ScaleKeyframe(ref keyframes[0], from, to, timeStart, timeEnd);
            ScaleKeyframe(ref keyframes[1], from, to, timeStart, timeEnd);
            return new(keyframes);
        }

        public static void ApplyEasing(AnimationCurve curve, int offset, EasingType preset) {
            var keyframe1 = curve[offset];
            var keyframe2 = curve[offset + 1];
            ApplyEasing(ref keyframe1, ref keyframe2, preset);
            curve.MoveKey(offset, keyframe1);
            curve.MoveKey(offset + 1, keyframe2);
        }

        public static void ApplyEasing(AnimationCurve curve, int offset, float x1, float y1, float x2, float y2) {
            var keyframe1 = curve[offset];
            var keyframe2 = curve[offset + 1];
            ApplyEasing(ref keyframe1, ref keyframe2, x1, y1, x2, y2);
            curve.MoveKey(offset, keyframe1);
            curve.MoveKey(offset + 1, keyframe2);
        }

        public static void ApplyEasing(Keyframe[] keyframes, int offset, EasingType preset) =>
            ApplyEasing(ref keyframes[offset], ref keyframes[offset + 1], preset);

        public static void ApplyEasing(Keyframe[] keyframes, int offset, float x1, float y1, float x2, float y2) =>
            ApplyEasing(ref keyframes[offset], ref keyframes[offset + 1], x1, y1, x2, y2);

        static void ApplyEasing(ref Keyframe keyframe1, ref Keyframe keyframe2, EasingType preset) {
            switch (preset) {
                case EasingType.QuadraticIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.11F, 0, 0.5F, 1); break;
                case EasingType.QuadraticOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.5F, 0, 0.89F, 1); break;
                case EasingType.QuadraticInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.45F, 0, 0.55F, 1); break;
                case EasingType.CubicIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.32F, 0, 0.67F, 1); break;
                case EasingType.CubicOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.33F, 0, 0.68F, 1); break;
                case EasingType.CubicInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.65F, 0, 0.35F, 1); break;
                case EasingType.QuarticIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.5F, 0, 0.75F, 1); break;
                case EasingType.QuarticOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.25F, 0, 0.5F, 1); break;
                case EasingType.QuarticInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.76F, 0, 0.24F, 1); break;
                case EasingType.QuinticIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.64F, 0, 0.78F, 1); break;
                case EasingType.QuinticOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.22F, 0, 0.36F, 1); break;
                case EasingType.QuinticInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.83F, 0, 0.17F, 1); break;
                case EasingType.ExponentialIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.7F, 0, 0.84F, 1); break;
                case EasingType.ExponentialOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.16F, 0, 0.3F, 1); break;
                case EasingType.ExponentialInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.87F, 0, 0.13F, 1); break;
                case EasingType.EaseIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.42F, 0, 1, 1); break;
                case EasingType.EaseOut: ApplyEasing(ref keyframe1, ref keyframe2, 0, 0, 0.58F, 1); break;
                case EasingType.EaseInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.42F, 0, 0.58F, 1); break;
                case EasingType.SineIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.12F, 0, 0.39F, 1); break;
                case EasingType.SineOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.61F, 0, 0.88F, 1); break;
                case EasingType.SineInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.37F, 0, 0.63F, 1); break;
                case EasingType.CircularIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.55F, 0, 1, 0.45F); break;
                case EasingType.CircularOut: ApplyEasing(ref keyframe1, ref keyframe2, 0, 0.55F, 0.45F, 1); break;
                case EasingType.CircularInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.85F, 0, 0.15F, 1); break;
                case EasingType.BackIn: ApplyEasing(ref keyframe1, ref keyframe2, 0.36F, 0, 0.66F, -0.56F); break;
                case EasingType.BackOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.34F, 1.56F, 0.64F, 1); break;
                case EasingType.BackInOut: ApplyEasing(ref keyframe1, ref keyframe2, 0.68F, -0.6F, 0.32F, 1.6F); break;
            }
        }

        static void ApplyEasing(ref Keyframe keyframe1, ref Keyframe keyframe2, float x1, float y1, float x2, float y2) {
            ApplyBezierToKeyframe(ref keyframe1, Vector2.zero, new(x1, y1), true);
            ApplyBezierToKeyframe(ref keyframe2, Vector2.one, new(x2, y2), false);
        }

        static void ApplyBezierToKeyframe(ref Keyframe keyframe, Vector2 wayPoint, Vector2 control, bool isIn) {
            var delta = control - wayPoint;
            float tangent = delta.x == 0 ? 1 : delta.y == 0 ? 0 : delta.y / delta.x;
            if (isIn) {
                keyframe.outWeight = control.x;
                keyframe.outTangent = tangent;
                keyframe.weightedMode |= WeightedMode.Out;
            } else {
                keyframe.inWeight = 1 - control.x;
                keyframe.inTangent = tangent;
                keyframe.weightedMode |= WeightedMode.In;
            }
        }

        static void ScaleKeyframe(ref Keyframe keyframe, float from, float to, float timeStart, float timeEnd) {
            keyframe.time = Mathf.Lerp(timeStart, timeEnd, keyframe.time);
            keyframe.value = Mathf.Lerp(from, to, keyframe.value);
        }

        [Flags]
        public enum EasingType : ushort {
            None = 0x0,
            In = 0x1,
            Out = 0x2,
            InOut = In | Out,
            Linear = 0x0,
            Quadratic = 0x4,
            Cubic = 0x8,
            Quartic = 0x10,
            Quintic = 0x20,
            Exponential = 0x40,
            Ease = 0x80,
            Sine = 0x100,
            Circular = 0x200,
            Back = 0x400,
            QuadraticIn = Quadratic | In,
            QuadraticOut = Quadratic | Out,
            QuadraticInOut = Quadratic | InOut,
            CubicIn = Cubic | In,
            CubicOut = Cubic | Out,
            CubicInOut = Cubic | InOut,
            QuarticIn = Quartic | In,
            QuarticOut = Quartic | Out,
            QuarticInOut = Quartic | InOut,
            QuinticIn = Quintic | In,
            QuinticOut = Quintic | Out,
            QuinticInOut = Quintic | InOut,
            ExponentialIn = Exponential | In,
            ExponentialOut = Exponential | Out,
            ExponentialInOut = Exponential | InOut,
            EaseIn = Ease | In,
            EaseOut = Ease | Out,
            EaseInOut = Ease | InOut,
            SineIn = Sine | In,
            SineOut = Sine | Out,
            SineInOut = Sine | InOut,
            CircularIn = Circular | In,
            CircularOut = Circular | Out,
            CircularInOut = Circular | InOut,
            BackIn = Back | In,
            BackOut = Back | Out,
            BackInOut = Back | InOut,
        }
    }
}