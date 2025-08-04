using System;

namespace JLChnToZ.NDExtensions {
    public readonly struct SmoothFactor : IEquatable<SmoothFactor> {
        public readonly string propertyName;
        public readonly float constantValue;
        public readonly bool timeBased;

        public SmoothFactor(string propertyName, bool timeBased = false) {
            this.propertyName = propertyName;
            this.timeBased = timeBased;
            constantValue = 0;
        }

        public SmoothFactor(float constantValue, bool timeBased = false) {
            propertyName = null;
            this.constantValue = constantValue;
            this.timeBased = timeBased;
        }

        public readonly bool Equals(SmoothFactor other) =>
            propertyName == other.propertyName &&
            constantValue == other.constantValue &&
            timeBased == other.timeBased;

        public readonly override bool Equals(object obj) =>
            obj is SmoothFactor other && Equals(other);

        public readonly override int GetHashCode() =>
            HashCode.Combine(propertyName, constantValue, timeBased);
    }

    public enum SmoothType {
        None,
        Linear,
        Exponential,
    }
}