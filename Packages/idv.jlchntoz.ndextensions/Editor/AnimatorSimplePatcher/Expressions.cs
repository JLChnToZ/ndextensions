namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    abstract class Expression : Statement {
        protected Expression(int row, int col) : base(row, col) { }
    }

    sealed class BinaryExpression : Expression {
        public readonly Expression left;
        public readonly Expression right;
        public readonly bool IsAnd;

        public BinaryExpression(int row, int col, Expression left, Expression right, bool isAnd) : base(row, col) {
            this.left = left;
            this.right = right;
            IsAnd = isAnd;
        }
    }

    sealed class BooleanConstantExpression : Expression {
        public readonly bool value;

        public BooleanConstantExpression(int row, int col, bool value) : base(row, col) => this.value = value;
    }

    sealed class BooleanParameterExpression : Expression {
        public readonly string parameter;

        public BooleanParameterExpression(int row, int col, string parameter) : base(row, col) => this.parameter = parameter;
    }

    sealed class ComparisonExpression : Expression {
        public readonly string parameter;
        public readonly string op;
        public readonly Value value;

        public ComparisonExpression(int row, int col, string parameter, string op, Value value) : base(row, col) {
            this.parameter = parameter;
            this.op = op;
            this.value = value;
        }
    }

    sealed class NotExpression : Expression {
        public readonly Expression operand;

        public NotExpression(int row, int col, Expression operand) : base(row, col) => this.operand = operand;
    }
}
