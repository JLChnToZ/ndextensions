using System;
using UnityEngine;

namespace JLChnToZ.NDExtensions {
    [Serializable]
    public struct AnimatorParameterRef : IEquatable<AnimatorParameterRef> {
        public string name;
        public AnimatorControllerParameterType type;
        public Component source;

        public readonly bool IsValid => !string.IsNullOrEmpty(name) &&
            (type == AnimatorControllerParameterType.Float ||
             type == AnimatorControllerParameterType.Int ||
             type == AnimatorControllerParameterType.Bool ||
             type == AnimatorControllerParameterType.Trigger);

        public readonly bool Equals(AnimatorParameterRef other) =>
            name == other.name &&
            type == other.type &&
            source == other.source;

        public override readonly bool Equals(object obj) =>
            obj is AnimatorParameterRef other && Equals(other);

        public override readonly int GetHashCode() =>
            HashCode.Combine(name, type, source);

#if UNITY_EDITOR
        public static implicit operator AnimatorControllerParameter(AnimatorParameterRef parameter) => new() {
            name = parameter.name,
            type = parameter.type,
        };
#endif
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class AnimatorParameterRefUsageAttribute : PropertyAttribute {
        public Type[] IgnoreComponents { get; set; }
        public ParameterType EnforcedType { get; set; }

        public AnimatorParameterRefUsageAttribute() { }
    }

    [Flags]
    public enum ParameterType {
        None = 0,
        Float = 0x1,
        Int = 0x2,
        Bool = 0x4,
        Trigger = 0x8,
        AllTypes = Float | Int | Bool | Trigger,
        Synchronized = 0x10,
        NonSynchronized = 0x20,
        SynchronizeSpecified = Synchronized | NonSynchronized,
    }

    public static class AnimatorParameterRefExtensions {
        public static bool Has(this ParameterType type, AnimatorControllerParameterType targetType) => targetType switch {
            AnimatorControllerParameterType.Float => (type & ParameterType.Float) == ParameterType.Float,
            AnimatorControllerParameterType.Int => (type & ParameterType.Int) == ParameterType.Int,
            AnimatorControllerParameterType.Bool => (type & ParameterType.Bool) == ParameterType.Bool,
            AnimatorControllerParameterType.Trigger => (type & ParameterType.Trigger) == ParameterType.Trigger,
            _ => false,
        };

        public static bool? RequireSynchronize(this ParameterType type) {
            if ((type & ParameterType.Synchronized) == ParameterType.Synchronized) return true;
            if ((type & ParameterType.NonSynchronized) == ParameterType.NonSynchronized) return false;
            return null;
        }

        public static bool MatchesSynchronize(this ParameterType type, bool synchronized) {
            if ((type & ParameterType.Synchronized) == ParameterType.Synchronized) return synchronized;
            if ((type & ParameterType.NonSynchronized) == ParameterType.NonSynchronized) return !synchronized;
            return true;
        }

        public static AnimatorControllerParameterType ToAnimatorControllerParameterType(this ParameterType type) => (type & ParameterType.AllTypes) switch {
            ParameterType.Float => AnimatorControllerParameterType.Float,
            ParameterType.Int => AnimatorControllerParameterType.Int,
            ParameterType.Bool => AnimatorControllerParameterType.Bool,
            ParameterType.Trigger => AnimatorControllerParameterType.Trigger,
            _ => throw new ArgumentException("Ambiguous ParameterType for AnimatorControllerParameterType conversion.", nameof(type)),
        };

        public static ParameterType ToParameterType(this AnimatorControllerParameterType type) => type switch {
            AnimatorControllerParameterType.Float => ParameterType.Float,
            AnimatorControllerParameterType.Int => ParameterType.Int,
            AnimatorControllerParameterType.Bool => ParameterType.Bool,
            AnimatorControllerParameterType.Trigger => ParameterType.Trigger,
            _ => throw new ArgumentException("Unsupported AnimatorControllerParameterType for conversion to ParameterType.", nameof(type)),
        };
    }
}