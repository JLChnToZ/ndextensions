namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    readonly struct Value {
        public readonly ValueType type;
        public readonly float value, min, max;
        public readonly string source;

        public Value(bool value) {
            type = ValueType.Boolean;
            this.value = value ? 1 : 0;
            min = max = 0;
            source = null;
        }

        public Value(float value, bool isInteger) {
            type = isInteger ? ValueType.Integer : ValueType.Float;
            this.value = value;
            min = max = 0;
            source = null;
        }

        public Value(float min, float max) {
            type = ValueType.Random;
            value = 0;
            this.min = min;
            this.max = max;
            source = null;
        }

        public Value(string source, float min = 0, float max = 0) {
            type = ValueType.Copy;
            value = 0;
            this.min = min;
            this.max = max;
            this.source = source;
        }
    }
}