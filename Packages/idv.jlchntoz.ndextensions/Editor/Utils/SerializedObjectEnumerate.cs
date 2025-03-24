using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace JLChnToZ.NDExtensions.Editors {
    public static class SerializedObjectEnumerate {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Enumerable Enumerate(this SerializedObject so, bool visibleOnly = true, bool deep = true) => new(so, visibleOnly, deep);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Enumerator GetEnumerator(this SerializedObject so) => new(so, true, true);

        public readonly struct Enumerable : IEnumerable<SerializedProperty> {
            readonly SerializedObject so;
            readonly bool visibleOnly, deep;

            public Enumerable(SerializedObject so, bool visibleOnly, bool deep) {
                this.so = so;
                this.visibleOnly = visibleOnly;
                this.deep = deep;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Enumerator GetEnumerator() => new(so, visibleOnly, deep);

            IEnumerator<SerializedProperty> IEnumerable<SerializedProperty>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public readonly struct Enumerator : IEnumerator<SerializedProperty> {
            readonly SerializedProperty iterator;
            readonly bool visibleOnly, deep;

            public SerializedProperty Current => iterator;

            readonly object IEnumerator.Current => iterator;

            public Enumerator(SerializedObject so, bool visibleOnly, bool deep) {
                this.visibleOnly = visibleOnly;
                this.deep = deep;
                iterator = so.GetIterator();
            }

            public bool MoveNext() {
                bool enter = deep || iterator.depth < 0;
                return visibleOnly ? iterator.NextVisible(enter) : iterator.Next(enter);
            }

            public void Reset() => iterator.Reset();

            public void Dispose() => iterator.Dispose();
        }
    }
}