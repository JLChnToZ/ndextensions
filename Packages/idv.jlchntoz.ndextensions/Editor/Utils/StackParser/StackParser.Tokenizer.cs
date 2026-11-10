using System;
using System.Text;
using System.Collections.Generic;

namespace JLChnToZ.NDExtensions.Editors {
    public partial class StackParser {
        public static IEnumerable<(int row, int col, TokenType tokenType, string token)> Tokenize(string text, char[] symbolOverride = null) {
            var token = new StringBuilder();
            var cToken = new StringBuilder();
            var tokenType = TokenType.Init;
            int escLength = 0, temp = 0, row = 1, col = 0, t = -1;
            for (int i = 0; i < text.Length; i++) {
                var c = text[i];
                switch (c) {
                    case '\r':
                        if (i + 1 < text.Length && text[i + 1] == '\n') {
                            i++;
                            c = '\n';
                        }
                        goto case '\n';
                    case '\n':
                        if (i > t) {
                            row++;
                            col = 0;
                            t = i;
                        }
                        break;
                    default:
                        if (i > t) {
                            col++;
                            t = i;
                        }
                        break;
                }
                switch (tokenType & TokenType.BaseWithEscapedMask) {
                    case TokenType.Init:
                        if (IsEmptySeparator(c)) continue;
                        switch (c) {
                            case '\\': tokenType = TokenType.EscapedIdentifier; continue;
                            case '\'': tokenType = TokenType.SingleQuotedString; continue;
                            case '"': tokenType = TokenType.DoubleQuotedString; continue;
                        }
                        if (char.IsDigit(c)) {
                            tokenType = TokenType.Number;
                            goto case TokenType.Number;
                        }
                        token.Append(c);
                        if (c < 0x7F && (symbolOverride != null ? Array.IndexOf(symbolOverride, c) >= 0 : char.IsSymbol(c)))
                            tokenType = TokenType.Symbol;
                        else
                            tokenType = TokenType.Identifier;
                        break;
                    case TokenType.Identifier:
                        if (c == '\\') {
                            tokenType |= TokenType.Escaped;
                            continue;
                        }
                        if (IsIdentifier(c)) {
                            token.Append(c);
                            continue;
                        }
                        if (!IsEmptySeparator(c)) i--;
                        yield return (row, col, tokenType, token.ToString());
                        token.Clear();
                        tokenType = TokenType.Init;
                        break;
                    case TokenType.SingleQuotedString:
                        switch (c) {
                            case '\\':
                                tokenType |= TokenType.Escaped;
                                continue;
                            case '\'':
                                yield return (row, col, tokenType, token.ToString());
                                token.Clear();
                                tokenType = TokenType.Init;
                                continue;
                        }
                        token.Append(c);
                        break;
                    case TokenType.DoubleQuotedString:
                        switch (c) {
                            case '\\':
                                tokenType |= TokenType.Escaped;
                                continue;
                            case '"':
                                yield return (row, col, tokenType, token.ToString());
                                token.Clear();
                                tokenType = TokenType.Init;
                                continue;
                        }
                        token.Append(c);
                        break;
                    case TokenType.EscapedIdentifier:
                    case TokenType.EscapedSingleQuotedString:
                    case TokenType.EscapedDoubleQuotedString:
                        switch (TokenizeEscapes(ref token, ref cToken, ref tokenType, ref escLength, ref temp, c)) {
                            case TokenProcessResult.Continue: continue;
                            case TokenProcessResult.LookBackward: i--; break;
                        }
                        break;
                    case TokenType.Number:
                        switch (TokenizeNumber(ref token, ref tokenType, c)) {
                            case TokenProcessResult.Continue: continue;
                            case TokenProcessResult.LookBackward: i--; break;
                        }
                        yield return (row, col, tokenType, token.ToString());
                        token.Clear();
                        tokenType = TokenType.Init;
                        break;
                    case TokenType.Symbol:
                        if (token.Length > 0) {
                            if (char.IsDigit(c)) {
                                switch (Peek(in token, ^1)) {
                                    case '+':
                                    case '-':
                                        token.Remove(token.Length - 1, 1);
                                        if (token.Length > 0) {
                                            yield return (row, col, tokenType, token.ToString());
                                            token.Clear();
                                        }
                                        tokenType = TokenType.Number;
                                        i -= 2;
                                        continue;
                                    case '.':
                                        if (token.Length > 1)
                                            switch (Peek(in token, ^2)) {
                                                case '+':
                                                case '-':
                                                    token.Remove(token.Length - 2, 2);
                                                    if (token.Length > 0) {
                                                        yield return (row, col, tokenType, token.ToString());
                                                        token.Clear();
                                                    }
                                                    tokenType = TokenType.Number;
                                                    i -= 3;
                                                    continue;
                                            }
                                        goto case '+';
                                }
                            } else if ((c == '/' || c == '*') && Peek(in token, ^1) == '/') {
                                token.Remove(token.Length - 1, 1);
                                if (token.Length > 0) {
                                    yield return (row, col, tokenType, token.ToString());
                                    token.Clear();
                                }
                                switch (c) {
                                    case '/': tokenType = TokenType.SingleLineComments; break;
                                    case '*': tokenType = TokenType.MultiLineComments; break;
                                }
                                i--;
                                continue;
                            }
                        }
                        if (!IsIdentifier(c) && (symbolOverride == null || Array.IndexOf(symbolOverride, c) >= 0)) {
                            token.Append(c);
                            break;
                        }
                        if (!IsEmptySeparator(c)) i--;
                        yield return (row, col, tokenType, token.ToString());
                        token.Clear();
                        tokenType = TokenType.Init;
                        break;
                    case TokenType.SingleLineComments:
                        if (c == '\n') tokenType = TokenType.Init;
                        break;
                    case TokenType.MultiLineComments:
                        switch (c) {
                            case '*':
                                if (token.Length == 0)
                                    token.Append(c);
                                break;
                            case '/':
                                if (token.Length > 0) {
                                    if (Peek(in token, ^1) == '*')
                                        tokenType = TokenType.Init;
                                    token.Clear();
                                }
                                break;
                            default:
                                if (token.Length > 0)
                                    token.Clear();
                                break;
                        }
                        break;
                    default:
                        throw new Exception($"Unexpected token type: {tokenType}");
                }
            }
            switch (tokenType & TokenType.BaseWithEscapedMask) {
                case TokenType.SingleQuotedString:
                case TokenType.DoubleQuotedString:
                    throw new ParseException(row, col, "Unterminated string literal.");
                case TokenType.EscapedSingleQuotedString:
                case TokenType.EscapedDoubleQuotedString:
                    if (TokenizeEscapes(ref token, ref cToken, ref tokenType, ref escLength, ref temp, '\0') == TokenProcessResult.LookBackward)
                        throw new ParseException(row, col, "Unterminated string literal.");
                    break;
                case TokenType.Number:
                    if (TokenizeNumber(ref token, ref tokenType, '\0') == TokenProcessResult.LookBackward)
                        throw new ParseException(row, col, "Invalid number literal.");
                    break;
            }
            if (token.Length > 0) yield return (row, col, tokenType, token.ToString());
        }

        static TokenProcessResult TokenizeEscapes(
            ref StringBuilder token, ref StringBuilder charToken,
            ref TokenType tokenType, ref int length,
            ref int n, char c
        ) {
            switch (tokenType & TokenType.EscapeMask) {
                case TokenType.Escaped:
                    switch (c) {
                        case 'a': case 'A': token.Append('\a'); break;
                        case 'b': case 'B': token.Append('\b'); break;
                        case 'e': case 'E': token.Append('\x1b'); break;
                        case 'f': case 'F': token.Append('\f'); break;
                        case 'h': case 'H': token.Append('\x08'); break;
                        case 'n': case 'N': token.Append('\n'); break;
                        case 'r': case 'R': token.Append('\r'); break;
                        case 't': case 'T': token.Append('\t'); break;
                        case 'v': case 'V': token.Append('\v'); break;
                        case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7':
                            tokenType = (tokenType & TokenType.BaseMask) | TokenType.OctalEscape;
                            length = 2;
                            charToken.Clear();
                            charToken.Append(c);
                            n = c - '0';
                            return TokenProcessResult.Continue;
                        case 'o': case 'O':
                            tokenType = (tokenType & TokenType.BaseMask) | TokenType.OctalEscape;
                            length = 3;
                            charToken.Clear();
                            n = 0;
                            return TokenProcessResult.Continue;
                        case 'u':
                            tokenType = (tokenType & TokenType.BaseMask) | TokenType.HexademicalEscape;
                            length = 4;
                            charToken.Clear();
                            n = 0;
                            return TokenProcessResult.Continue;
                        case 'U':
                            tokenType = (tokenType & TokenType.BaseMask) | TokenType.HexademicalEscape;
                            length = 8;
                            charToken.Clear();
                            n = 0;
                            return TokenProcessResult.Continue;
                        case 'x': case 'X':
                            tokenType = (tokenType & TokenType.BaseMask) | TokenType.HexademicalEscape;
                            length = 2;
                            charToken.Clear();
                            n = 0;
                            return TokenProcessResult.Continue;
                    }
                    token.Append(c);
                    return TokenProcessResult.LookForward;
                case TokenType.OctalEscape:
                    switch (c) {
                        case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7':
                            n = (n << 3) | (c - '0');
                            break;
                        default:
                            tokenType &= TokenType.BaseMask;
                            token.Append((char)n);
                            return IsEmptySeparator(c) ?
                                TokenProcessResult.LookForward :
                                TokenProcessResult.LookBackward;
                    }
                    break;
                case TokenType.HexademicalEscape:
                    switch (c) {
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                            n = (n << 4) | (c - '0');
                            break;
                        case 'a': case 'b': case 'c': case 'd': case 'e': case 'f':
                            n = (n << 4) | (c - 'a' + 10);
                            break;
                        case 'A': case 'B': case 'C': case 'D': case 'E': case 'F':
                            n = (n << 4) | (c - 'A' + 10);
                            break;
                        default:
                            tokenType &= TokenType.BaseMask;
                            if (IsEmptySeparator(c))
                                return TokenProcessResult.LookForward;
                            token.Append(charToken);
                            return TokenProcessResult.LookBackward;
                    }
                    break;
            }
            if (--length > 0) {
                charToken.Append(c);
                return TokenProcessResult.Continue;
            }
            if ((n & char.MaxValue) != n)
                token.Append(char.ConvertFromUtf32(n));
            else
                token.Append((char)n);
            tokenType &= TokenType.BaseMask;
            return TokenProcessResult.LookForward;
        }

        static TokenProcessResult TokenizeNumber(ref StringBuilder token, ref TokenType tokenType, char c) {
            switch (tokenType & TokenType.NumberMask) {
                case TokenType.Number:
                    switch (c) {
                        case '+': case '-': case '0':
                            tokenType = TokenType.ZeroNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case '1': case '2': case '3': case '4': case '5':
                        case '6': case '7': case '8': case '9':
                            tokenType = TokenType.DecimalNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case '.':
                            tokenType = TokenType.FractionalNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case 'e': case 'E':
                            tokenType = TokenType.ExponentNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.ZeroNumber:
                    switch (c) {
                        case 'b': case 'B':
                            tokenType = TokenType.BinaryNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case 'o': case 'O':
                            tokenType = TokenType.OctalNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case 'x': case 'X':
                            tokenType = TokenType.HexademicalNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    tokenType = TokenType.DecimalNumber;
                    goto case TokenType.DecimalNumber;
                case TokenType.BinaryNumber:
                    switch (c) {
                        case '0': case '1':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.OctalNumber:
                    switch (c) {
                        case '0': case '1': case '2': case '3':
                        case '4': case '5': case '6': case '7':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.HexademicalNumber:
                    switch (c) {
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                        case 'a': case 'b': case 'c': case 'd': case 'e': case 'f':
                        case 'A': case 'B': case 'C': case 'D': case 'E': case 'F':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.DecimalNumber:
                    switch (c) {
                        case '.':
                            tokenType = TokenType.FractionalNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case 'e': case 'E':
                            tokenType = TokenType.ExponentNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.FractionalNumber:
                    switch (c) {
                        case 'e': case 'E':
                            tokenType = TokenType.ExponentNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.ExponentNumber:
                    switch (c) {
                        case '+': case '-':
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                            tokenType = TokenType.SignedExponentNumber;
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
                case TokenType.SignedExponentNumber:
                    switch (c) {
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                            token.Append(c);
                            return TokenProcessResult.Continue;
                    }
                    break;
            }
            tokenType &= TokenType.BaseMask;
            return TokenProcessResult.LookBackward;
        }

        static char Peek(in StringBuilder sb, Index index) =>
            sb[index.GetOffset(sb.Length)];

        static bool IsEmptySeparator(char c) => char.IsWhiteSpace(c) || char.IsControl(c);

        static bool IsIdentifier(char c) => c == '_' || char.IsLetterOrDigit(c);

        [Flags]
        public enum TokenType : ushort {
            // Base types - will pass to OnParse
            Init = 0x00,
            Identifier = 0x01,
            SingleQuotedString = 0x02,
            DoubleQuotedString = 0x03,
            Number = 0x04,
            Symbol = 0x08,
            SingleLineComments = 0x1000,
            MultiLineComments = 0x1001,
            BaseMask = Identifier | SingleQuotedString | DoubleQuotedString | Number | Symbol | SingleLineComments | MultiLineComments,

            // Numbers - internal only
            ZeroNumber = 0x20 | Number,
            BinaryNumber = 0x40 | Number,
            OctalNumber = 0x60 | Number,
            DecimalNumber = 0x80 | Number,
            HexademicalNumber = 0xA0 | Number,
            FractionalNumber = 0xC0 | Number,
            ExponentNumber = 0xE0 | Number,
            SignedExponentNumber = 0x100 | Number,
            NumberMask = Number | ZeroNumber | BinaryNumber | OctalNumber |
                DecimalNumber | HexademicalNumber |
                FractionalNumber | ExponentNumber | SignedExponentNumber,

            // Escapes - internal only
            Escaped = 0x10,
            OctalEscape = 0x200 | Escaped,
            HexademicalEscape = 0x400 | Escaped,
            EscapeMask = OctalEscape | HexademicalEscape,
            EscapedIdentifier = Identifier | Escaped,
            EscapedSingleQuotedString = SingleQuotedString | Escaped,
            EscapedDoubleQuotedString = DoubleQuotedString | Escaped,
            BaseWithEscapedMask = BaseMask | EscapeMask,
        }

        enum TokenProcessResult : byte {
            Continue,
            LookForward,
            LookBackward,
        }
    }

    public class ParseException : Exception {
        public int Row { get; }
        public int Col { get; }

        public override string Message => $"At row {Row}, column {Col}: {base.Message}";

        public ParseException(int row, int col, string message) : base(message) {
            Row = row;
            Col = col;
        }

        public ParseException(int row, int col, Exception innerException) :
            base(innerException.Message, innerException) {
            Row = row;
            Col = col;
        }
    }
}