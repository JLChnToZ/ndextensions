namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    abstract class Statement {
        public readonly int row, col;

        protected Statement(int row, int col) {
            this.row = row;
            this.col = col;
        }
    }

    sealed class ExitStatement : Statement {
        public ExitStatement(int row, int col) : base(row, col) { }
    }

    sealed class IfStatement : Statement {
        public readonly Expression condition;
        public readonly Block thenBlock, elseBlock;

        public IfStatement(int row, int col, Expression condition, Block thenBlock, Block elseBlock) : base(row, col) {
            this.condition = condition;
            this.thenBlock = thenBlock;
            this.elseBlock = elseBlock;
        }
    }

    sealed class LabelStatement : Statement {
        public readonly string name;

        public LabelStatement(int row, int col, string name) : base(row, col) => this.name = name;
    }

    sealed class WaitStatement : Statement {
        public readonly float duration;

        public WaitStatement(int row, int col, float duration) : base(row, col) => this.duration = duration;
    }

    sealed class GotoStatement : Statement {
        public readonly string label;

        public GotoStatement(int row, int col, string label) : base(row, col) => this.label = label;
    }
}