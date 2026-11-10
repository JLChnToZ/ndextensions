using nadena.dev.ndmf.animator;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class FlowNode {
        public readonly ActionStatement[] actions;
        public readonly int row, col;
        public readonly float? exitDuration;
        public readonly Expression condition;
        public readonly FlowNode thenNode, elseNode;
        public FlowNode nextNode;
        public string gotoLabel;
        public readonly bool isExit;
        public readonly bool isGoto;
        public bool isPassThroughGoto;
        public bool isInlinedCondition;
        public readonly bool isTerminal;
        public VirtualState state;
        public float motionDuration;

        public FlowNode() {
            isTerminal = true;
        }

        private FlowNode(Statement statement) {
            row = statement.row;
            col = statement.col;
        }

        public FlowNode(FlowNode nextNode, float? exitDuration, ActionStatement[] actions) {
            this.nextNode = nextNode;
            this.exitDuration = exitDuration;
            this.actions = actions;
        }

        public FlowNode(IfStatement statement, FlowNode thenNode, FlowNode elseNode) : this(statement) {
            condition = statement.condition;
            this.thenNode = thenNode;
            this.elseNode = elseNode;
        }

        public FlowNode(GotoStatement statement, ActionStatement[] actions) : this(statement) {
            gotoLabel = statement.label;
            this.actions = actions;
            isGoto = true;
        }

        public FlowNode(ExitStatement statement, ActionStatement[] actions) : this(statement) {
            isExit = true;
            this.actions = actions;
        }
    }
}
