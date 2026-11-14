using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine.Pool;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    sealed class Parser : IDisposable {
        static readonly char[] symbols = "!&|=<>+-*/(){}[],:.;@".ToCharArray();
        readonly List<Token> tokens;
        readonly List<Block> blocks;
        int position;

        static bool IsCompoundSymbol(char first, char second) => (first, second) switch {
            ('&', '&') or ('|', '|') or ('=', '=') or ('!', '=') or ('<', '=') or ('>', '=') or ('-', '>') => true,
            _ => false,
        };

        public static Program Parse(string source) {
            using var parser = new Parser(source);
            var root = parser.ParseBlock(false);
            if (!parser.AtEnd) Error(parser.Current, "Unexpected token.");
            return new Program(root, parser.blocks);
        }

        Parser(string source) {
            tokens = ListPool<Token>.Get();
            blocks = ListPool<Block>.Get();
            foreach (var (row, col, type, text) in StackParser.Tokenize(source, symbols)) {
                if (type == StackParser.TokenType.Symbol) {
                    for (int i = 0; i < text.Length;) {
                        var length = i + 1 < text.Length && IsCompoundSymbol(text[i], text[i + 1]) ? 2 : 1;
                        tokens.Add(new Token(type, text.Substring(i, length), row, col));
                        i += length;
                    }
                } else {
                    tokens.Add(new Token(type, text, row, col));
                }
            }
        }

        public void Dispose() => ListPool<Token>.Release(tokens);

        Block ParseBlock(bool isBraced) {
            var block = new Block();
            blocks.Add(block);
            while (!AtEnd && !(isBraced && IsSymbol("}")))
                block.statements.Add(ParseStatement());
            if (isBraced) ExpectSymbol("}");
            return block;
        }

        Statement ParseStatement() {
            var token = Current;
            if (IsName(token, "if")) return ParseIf();
            if (IsName(token, "wait")) return ParseWait();
            if (IsName(token, "set")) return ParseSet();
            if (IsName(token, "goto")) return ParseGoto();
            if (IsTreeStart()) return ParseTree();
            if (IsName(token, "exit")) {
                position++;
                MatchSymbol(";");
                return new ExitStatement(token.row, token.col);
            }
            if (IsSymbol("}")) Error(token, "Unexpected closing brace.");

            if (token.type == StackParser.TokenType.Identifier && PeekSymbol(1, ":") && IsLabelBoundary(position + 2, token.row)) {
                position += 2;
                MatchSymbol(";");
                return new LabelStatement(token.row, token.col, token.text);
            }

            return ParseAnimation();
        }

        bool IsTreeStart() {
            if (!IsName(Current, "tree")) return false;
            if (position + 1 >= tokens.Count) return false;
            if (IsName(tokens[position + 1], "direct")) return true;
            if (!PeekSymbol(1, "(")) return false;
            for (var i = position + 2; i < tokens.Count; i++)
                if (tokens[i].type == StackParser.TokenType.Symbol && tokens[i].text == ")")
                    return i + 1 >= tokens.Count || tokens[i + 1].type != StackParser.TokenType.Symbol || tokens[i + 1].text != ":";
            return true;
        }

        TreeStatement ParseTree() {
            var start = Current;
            position++;
            TreeKind kind;
            string x = null, y = null;
            bool normalized = false;
            if (IsName(Current, "direct")) {
                kind = TreeKind.Direct;
                position++;
                if (IsName(Current, "normalized")) {
                    normalized = true;
                    position++;
                }
            } else {
                ExpectSymbol("(");
                x = ParseName("Expected a blend parameter.").text;
                if (MatchSymbol(",")) y = ParseName("Expected a second blend parameter.").text;
                ExpectSymbol(")");
                kind = y == null ? TreeKind.OneDimensional : TreeKind.FreeformDirectional;
                if (IsName(Current, "simple") || IsName(Current, "cartesian")) {
                    if (y == null) Error(Current, "A 2D blend mode requires two parameters.");
                    kind = IsName(Current, "simple") ? TreeKind.SimpleDirectional : TreeKind.FreeformCartesian;
                    position++;
                }
            }
            var tree = new TreeStatement(start.row, start.col, kind, x, y, normalized);
            ExpectSymbol("{");
            while (!AtEnd && !IsSymbol("}")) {
                string parameter = null;
                float threshold = 0, coordinateY = 0;
                if (kind == TreeKind.Direct) {
                    if (Current.type == StackParser.TokenType.Number) {
                        threshold = ParseNumber(Current);
                        position++;
                    } else {
                        parameter = ParseName("Expected a direct blend parameter or constant weight.").text;
                    }
                } else if (kind == TreeKind.OneDimensional) {
                    threshold = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a blend threshold."));
                } else {
                    ExpectSymbol("(");
                    threshold = ParseNumber(Expect(StackParser.TokenType.Number, "Expected an X coordinate."));
                    ExpectSymbol(",");
                    coordinateY = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a Y coordinate."));
                    ExpectSymbol(")");
                }
                ExpectSymbol(":");
                TreeStatement nested = null;
                AnimationStatement[] animations = null;
                if (IsTreeStart()) {
                    nested = ParseTree();
                } else {
                    ExpectSymbol("{");
                    var actions = new List<AnimationStatement>();
                    while (!AtEnd && !IsSymbol("}")) {
                        if (IsTreeStart() || IsName(Current, "set") || IsName(Current, "if") ||
                            IsName(Current, "wait") || IsName(Current, "goto") || IsName(Current, "exit"))
                            Error(Current, "A blend-tree animation body may contain only animation actions.");
                        actions.Add((AnimationStatement)ParseAnimation());
                    }
                    ExpectSymbol("}");
                    animations = actions.ToArray();
                }
                tree.children.Add(new TreeChild(parameter, threshold, coordinateY, nested, animations));
            }
            ExpectSymbol("}");
            return tree;
        }

        Statement ParseIf() {
            var start = Current;
            position++;
            ExpectSymbol("(");
            var condition = ParseOr();
            ExpectSymbol(")");
            var thenBlock = ParseIfBody();
            Block elseBlock = null;
            if (IsName(Current, "else")) {
                position++;
                elseBlock = ParseIfBody();
            }
            return new IfStatement(start.row, start.col, condition, thenBlock, elseBlock);
        }

        Block ParseIfBody() {
            if (MatchSymbol("{")) return ParseBlock(true);
            var block = new Block();
            blocks.Add(block);
            block.statements.Add(ParseStatement());
            return block;
        }

        Statement ParseWait() {
            var start = Current;
            position++;
            var duration = ParseDuration();
            if (duration <= 0) Error(start, "Wait duration must be greater than zero.");
            MatchSymbol(";");
            return new WaitStatement(start.row, start.col, duration);
        }

        Statement ParseSet() {
            var start = Current;
            float min = 0, max = 0;
            position++;
            var parameter = ParseName("Expected a parameter name after 'set'.");
            var operation = SetOperation.Assign;
            if (MatchSymbol("+")) operation = SetOperation.Add;
            else if (MatchSymbol("-")) operation = SetOperation.Subtract;
            if (MatchSymbol("(")) {
                min = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a number for min."));
                ExpectSymbol(",");
                max = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a number for max."));
                ExpectSymbol(")");
            }
            ExpectSymbol("=");
            var value = ParseValue();
            var local = IsName(Current, "local");
            if (local) position++;
            ExpectSymbol(";");
            return new SetStatement(start.row, start.col, parameter.text, operation, value, min, max, local);
        }

        Statement ParseGoto() {
            var start = Current;
            position++;
            var label = ParseName("Expected a label name after 'goto'.");
            MatchSymbol(";");
            return new GotoStatement(start.row, start.col, label.text);
        }

        Statement ParseAnimation() {
            var start = Current;
            var target = ParseName("Expected a transform reference.");
            Type type = null;
            var sb = new StringBuilder();
            if (MatchSymbol("(")) {
                var nameToken = ParseName("Expected a type name after '('.");
                sb.Append(nameToken.text);
                if (nameToken.type == StackParser.TokenType.Identifier)
                    while (MatchSymbol("."))
                        sb.Append(".").Append(ParseName("Expected a type component after ' .'.", true).text);
                ExpectSymbol(")");
                type = TypeDB.TryGetType(sb.ToString());
                sb.Clear();
            }
            ExpectSymbol(":");
            var propertyToken = ParseName("Expected an animation property after ':'.");
            sb.Append(propertyToken.text);
            if (propertyToken.type == StackParser.TokenType.Identifier)
                while (true) {
                    if (MatchSymbol(".")) {
                        sb.Append(".").Append(ParseName("Expected a property component after '.'.", true).text);
                    } else if (MatchSymbol("[")) {
                        var index = Expect(StackParser.TokenType.Number, "Expected an array index.");
                        ExpectSymbol("]");
                        sb.Append("[").Append(index.text).Append("]");
                    } else {
                        break;
                    }
                }
            var property = sb.ToString();
            ExpectSymbol("->");
            var keyframes = new List<AnimationKeyframe> { ParseAnimationValue() };
            var first = keyframes[0];
            if (IsAnimationTimeStart()) {
                var (time, absolute) = ParseAnimationTime();
                keyframes[0] = new AnimationKeyframe(
                    first.value, first.refName, null, time, true, absolute
                );
            } else if (!IsSymbol("->") && !IsSymbol(";")) {
                Error(Current, "Expected a keyframe time or '->' after the first animation value.");
            }

            var previousTime = keyframes[0].hasTime ? keyframes[0].time : 0;
            while (MatchSymbol("->")) {
                var next = ParseAnimationValue();
                if (next.IsPointerKey != first.IsPointerKey)
                    Error(Current, "An animation statement cannot mix numeric and object-reference keyframes.");
                EasingFactory.EasingType? easing = null;
                if (!next.IsPointerKey && TryParseEasing(out var selectedEasing, out _))
                    easing = selectedEasing;
                if (!IsAnimationTimeStart())
                    Error(Current, "Expected a keyframe time after the animation value.");
                var (time, absolute) = ParseAnimationTime();
                var keyTime = absolute ? time : previousTime + time;
                if (float.IsNaN(keyTime) || float.IsInfinity(keyTime) || keyTime <= previousTime)
                    Error(Current, "Animation keyframe times must increase.");
                keyframes.Add(new AnimationKeyframe(
                    next.value, next.refName, easing, time, true, absolute
                ));
                previousTime = keyTime;
            }
            ExpectSymbol(";");
            return new AnimationStatement(start.row, start.col, target.text, type, property, keyframes.ToArray());
        }

        AnimationKeyframe ParseAnimationValue() {
            if (Current.type == StackParser.TokenType.Number) {
                var value = ParseNumber(Current);
                position++;
                return new AnimationKeyframe(value, null, null, 0, false, false);
            }
            if (Current.type == StackParser.TokenType.Identifier ||
                Current.type == StackParser.TokenType.SingleQuotedString ||
                Current.type == StackParser.TokenType.DoubleQuotedString) {
                var referenceName = Current.text;
                position++;
                return new AnimationKeyframe(0, referenceName, null, 0, false, false);
            }
            Error(Current, "Expected a numeric value or object reference after '->'.");
            return null;
        }

        bool TryParseEasing(out EasingFactory.EasingType easing, out Token token) {
            token = Current;
            if (token.type != StackParser.TokenType.Identifier) {
                easing = default;
                return false;
            }
            if (!Enum.TryParse(token.text, true, out easing))
                Error(token, "Unknown easing mode.");
            position++;
            return true;
        }

        bool IsAnimationTimeStart() => IsSymbol("@") || Current.type == StackParser.TokenType.Number;

        (float time, bool absolute) ParseAnimationTime() {
            var absolute = MatchSymbol("@");
            var time = ParseDuration();
            if (time < 0)
                Error(Previous, "Animation time must not be negative.");
            return (time, absolute);
        }

        Value ParseValue() {
            if (IsName(Current, "random")) {
                var start = Current;
                position++;
                ExpectSymbol("(");
                var min = ParseNumber(Expect(StackParser.TokenType.Number, "Expected the random minimum."));
                ExpectSymbol(",");
                var max = ParseNumber(Expect(StackParser.TokenType.Number, "Expected the random maximum."));
                ExpectSymbol(")");
                if (min > max) Error(start, "Random minimum must not exceed its maximum.");
                return new Value(min, max);
            }
            if (IsName(Current, "true") || IsName(Current, "false")) {
                var value = IsName(Current, "true");
                position++;
                return new Value(value);
            }
            if (Current.type == StackParser.TokenType.SingleQuotedString ||
                Current.type == StackParser.TokenType.DoubleQuotedString ||
                Current.type == StackParser.TokenType.Identifier) {
                var value = Current.text;
                position++;
                if (MatchSymbol("(")) {
                    var x = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a number for remap x."));
                    ExpectSymbol(",");
                    var y = ParseNumber(Expect(StackParser.TokenType.Number, "Expected a number for remap y."));
                    ExpectSymbol(")");
                    return new Value(value, x, y);
                }
                return new Value(value);
            }
            var token = Expect(StackParser.TokenType.Number, "Expected a number, boolean, or random(min, max).");
            return new Value(ParseNumber(token), IsIntegerToken(token.text));
        }

        Expression ParseOr() {
            var expression = ParseAnd();
            while (MatchSymbol("||")) {
                var op = Previous;
                expression = new BinaryExpression(op.row, op.col, expression, ParseAnd(), false);
            }
            return expression;
        }

        Expression ParseAnd() {
            var expression = ParseUnary();
            while (MatchSymbol("&&")) {
                var op = Previous;
                expression = new BinaryExpression(op.row, op.col, expression, ParseUnary(), true);
            }
            return expression;
        }

        Expression ParseUnary() {
            if (MatchSymbol("!")) {
                var op = Previous;
                return new NotExpression(op.row, op.col, ParseUnary());
            }
            if (MatchSymbol("(")) {
                var expression = ParseOr();
                ExpectSymbol(")");
                return expression;
            }

            var parameter = ParseName("Expected a parameter or grouped condition.");
            if (IsName(parameter, "true") || IsName(parameter, "false"))
                return new BooleanConstantExpression(parameter.row, parameter.col, IsName(parameter, "true"));

            if (Current.type == StackParser.TokenType.Symbol && IsComparisonOperator(Current.text)) {
                var op = Current;
                position++;
                var value = ParseConditionValue();
                if (value.type == ValueType.Random) Error(op, "Random values cannot be used in a condition.");
                return new ComparisonExpression(parameter.row, parameter.col, parameter.text, op.text, value);
            }
            return new BooleanParameterExpression(parameter.row, parameter.col, parameter.text);
        }

        Value ParseConditionValue() {
            if (IsName(Current, "true") || IsName(Current, "false")) {
                var value = IsName(Current, "true");
                position++;
                return new Value(value);
            }
            var token = Expect(StackParser.TokenType.Number, "Expected a boolean or numeric comparison value.");
            return new Value(ParseNumber(token), IsIntegerToken(token.text));
        }

        float ParseDuration() {
            var token = Expect(StackParser.TokenType.Number, "Expected a duration.");
            var duration = ParseNumber(token);
            if (IsName(Current, "ms")) {
                position++;
                duration /= 1000f;
            } else if (IsName(Current, "s")) {
                position++;
            }
            return duration;
        }

        bool IsLabelBoundary(int index, int row) {
            if (index >= tokens.Count) return true;
            var next = tokens[index];
            return next.row > row || next.text == "}" || next.text == ";";
        }

        Token ParseName(string message, bool mustUnquoted = false) {
            var token = Current;
            if (mustUnquoted && token.type != StackParser.TokenType.Identifier)
                Error(token, message);
            else if (!mustUnquoted && token.type != StackParser.TokenType.Identifier &&
                token.type != StackParser.TokenType.SingleQuotedString &&
                token.type != StackParser.TokenType.DoubleQuotedString)
                Error(token, message);
            position++;
            return token;
        }

        Token Expect(StackParser.TokenType type, string message) {
            var token = Current;
            if (token.type != type) Error(token, message);
            position++;
            return token;
        }

        void ExpectSymbol(string symbol) {
            if (!MatchSymbol(symbol)) Error(Current, $"Expected '{symbol}'.");
        }

        bool MatchSymbol(string symbol) {
            if (!IsSymbol(symbol)) return false;
            position++;
            return true;
        }

        bool PeekSymbol(int offset, string symbol) => position + offset < tokens.Count && tokens[position + offset].type == StackParser.TokenType.Symbol && tokens[position + offset].text == symbol;

        bool IsSymbol(string symbol) => Current.type == StackParser.TokenType.Symbol && Current.text == symbol;

        bool IsName(Token token, string name) => token.type == StackParser.TokenType.Identifier && string.Equals(token.text, name, StringComparison.OrdinalIgnoreCase);

        Token Current => position < tokens.Count ? tokens[position] : new Token(StackParser.TokenType.Init, "<end>", tokens.Count == 0 ? 0 : tokens[tokens.Count - 1].row, tokens.Count == 0 ? 0 : tokens[tokens.Count - 1].col + 1);
        Token Previous => tokens[position - 1];
        bool AtEnd => position >= tokens.Count;

        static bool IsComparisonOperator(string op) => op == "==" || op == "!=" || op == "<" || op == "<=" || op == ">" || op == ">=";

        static bool IsIntegerToken(string text) => text.IndexOf('.') < 0 && text.IndexOf('e') < 0 && text.IndexOf('E') < 0;

        static float ParseNumber(Token token) {
            if (!float.TryParse(token.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                Error(token, $"Invalid or out-of-range numeric literal. Received '{token.text}'.");
            return value;
        }

        static void Error(Token token, string message) => throw new ParseException(token.row, token.col, message);
    }
}
