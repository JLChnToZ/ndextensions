using System;
using System.Collections.Generic;

namespace JLChnToZ.NDExtensions.Editors {
    public abstract partial class StackParser {
        Stack<StackParser> currentStack;
        protected char[] symbolOverride;

        protected StackParser() {}

        public void Parse(string text) {
            if (currentStack != null)
                throw new InvalidOperationException("This parser is already parsing.");
            if (string.IsNullOrWhiteSpace(text)) return;
            currentStack = new Stack<StackParser>();
            currentStack.Push(this);
            OnAttach(null);
            int row = -1, col = 0, indentLevel = 0;
            try {
                foreach (var (row_, col_, tokenType, data) in Tokenize(text, symbolOverride)) {
                    bool hasLineBreak = row_ > row;
                    if (hasLineBreak) indentLevel = col_;
                    row = row_;
                    col = col_;
                    if (tokenType == TokenType.Symbol && data.Length > 1) {
                        foreach (var c in data) {
                            currentStack.Peek().OnParse(tokenType, c.ToString(), hasLineBreak, indentLevel);
                            col++;
                        }
                        continue;
                    }
                    currentStack.Peek().OnParse(tokenType, data, hasLineBreak, indentLevel);
                }
            } catch (ParseException) {
                throw;
            } catch (Exception ex) {
                throw new ParseException(row, col, ex);
            } finally {
                while (currentStack.TryPop(out var top)) {
                    if (top.currentStack == currentStack) {
                        top.OnDetech();
                        top.currentStack = null;
                        if (top == this) break; // this is the root parser
                    }
                }
            }
        }

        protected abstract void OnParse(TokenType type, string token, bool hasLineBreak, int indentLevel);

        public void Attach(StackParser parent) {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (parent.currentStack == null)
                throw new InvalidOperationException("Parent parser is not parsing.");
            if (currentStack != null)
                throw new InvalidOperationException("This parser is already attached.");
            currentStack = parent.currentStack;
            currentStack.Push(this);
            OnAttach(parent);
        }

        public void Detech() {
            if (currentStack == null)
                throw new InvalidOperationException("This parser is not attached.");
            var top = currentStack.Peek();
            if (top != this)
                throw new InvalidOperationException("This parser is not the top of the stack.");
            if (currentStack.Count == 1)
                throw new InvalidOperationException("This parser is the root of the stack.");
            OnDetech();
            currentStack.Pop();
            currentStack = null;
        }

        public StackParser[] CopyStack() {
            if (currentStack == null) {
                UnityEngine.Debug.LogWarning("This parser is not attached.");
                return Array.Empty<StackParser>();
            }
            var stack = currentStack.ToArray();
            Array.Reverse(stack);
            return stack;
        }

        protected virtual void OnAttach(StackParser parent) { }

        protected virtual void OnDetech() { }
    }
}