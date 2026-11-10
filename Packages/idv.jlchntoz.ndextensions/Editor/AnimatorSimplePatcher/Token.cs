namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    readonly struct Token {
        public readonly StackParser.TokenType type;
        public readonly string text;
        public readonly int row, col;

        public Token(StackParser.TokenType type, string text, int row, int col) {
            this.type = type;
            this.text = text;
            this.row = row;
            this.col = col;
        }
    }
}
