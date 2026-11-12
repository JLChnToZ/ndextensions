using System;
using System.Collections.Generic;
using UnityObject = UnityEngine.Object;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NDExtensions.Editor.Tests")]

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    abstract class ActionStatement : Statement {
        protected ActionStatement(int row, int col) : base(row, col) { }
    }

    sealed class AnimationStatement : ActionStatement {
        public readonly string target;
        public Type type;
        public readonly string property;
        public readonly AnimationKeyframe[] keyframes;
        public string resolvedPath;

        public AnimationStatement(
            int row, int col, string target, Type type, string property, AnimationKeyframe[] keyframes
        ) : base(row, col) {
            this.target = target;
            this.type = type;
            this.property = property;
            this.keyframes = keyframes;
        }

        public bool IsPointerCurve => keyframes[0].IsPointerKey;
    }

    sealed class AnimationKeyframe {
        public readonly float value;
        public readonly string refName;
        public readonly EasingFactory.EasingType? easing;
        public readonly float time;
        public readonly bool hasTime;
        public readonly bool absoluteTime;
        public UnityObject resolvedRef;

        public AnimationKeyframe(
            float value, string refName, EasingFactory.EasingType? easing,
            float time, bool hasTime, bool absoluteTime
        ) {
            this.value = value;
            this.refName = refName;
            this.easing = easing;
            this.time = time;
            this.hasTime = hasTime;
            this.absoluteTime = absoluteTime;
        }

        public bool IsPointerKey => refName != null;
    }

    sealed class TreeStatement : ActionStatement {
        public readonly TreeKind kind;
        public readonly string parameterX, parameterY;
        public readonly bool normalized;
        public readonly List<TreeChild> children = new();

        public TreeStatement(int row, int col, TreeKind kind, string parameterX = null, string parameterY = null, bool normalized = false) : base(row, col) {
            this.kind = kind;
            this.parameterX = parameterX;
            this.parameterY = parameterY;
            this.normalized = normalized;
        }
    }

    enum TreeKind : byte {
        Direct,
        OneDimensional,
        SimpleDirectional,
        FreeformDirectional,
        FreeformCartesian,
    }

    sealed class SetStatement : ActionStatement {
        public readonly string parameter;
        public readonly SetOperation operation;
        public readonly Value value;
        public readonly float min, max;
        public readonly bool local;

        public SetStatement(int row, int col, string parameter, SetOperation operation, Value value, float min, float max, bool local) : base(row, col) {
            this.parameter = parameter;
            this.operation = operation;
            this.value = value;
            this.min = min;
            this.max = max;
            this.local = local;
        }
    }

    enum SetOperation : byte {
        Assign,
        Add,
        Subtract,
    }
}