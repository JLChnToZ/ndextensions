namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    interface ISegment { }

    sealed class ActionSegment : ISegment {
        public readonly ActionStatement[] actions;
        public readonly float? waitDuration;

        public ActionSegment(ActionStatement[] actions, float? waitDuration = null) {
            this.actions = actions;
            this.waitDuration = waitDuration;
        }
    }

    sealed class ExitSegment : ISegment {
        public readonly ExitStatement statement;
        public readonly ActionStatement[] actions;

        public ExitSegment(ExitStatement statement, ActionStatement[] actions) {
            this.statement = statement;
            this.actions = actions;
        }
    }

    sealed class GotoSegment : ISegment {
        public readonly GotoStatement statement;
        public readonly ActionStatement[] actions;

        public GotoSegment(GotoStatement statement, ActionStatement[] actions) {
            this.statement = statement;
            this.actions = actions;
        }
    }

    sealed class IfSegment : ISegment {
        public readonly IfStatement statement;

        public IfSegment(IfStatement statement) => this.statement = statement;
    }

    sealed class LabelSegment : ISegment {
        public readonly LabelStatement statement;

        public LabelSegment(LabelStatement statement) => this.statement = statement;
    }
}