using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace JLChnToZ.NDExtensions.Editors {
    public class FieldPatcher<T> {
        public delegate bool Patcher(ref T parameter);
        static readonly List<Cache> tempFields = new();
        static readonly Type targetType = typeof(T),
            unityObjectType = typeof(UnityEngine.Object),
            stringType = typeof(string),
            nullableType = typeof(Nullable<>);
        static readonly Dictionary<Type, Cache[]> fieldCache = new();
        readonly ConditionalWeakTable<object, object> cache = new();
        readonly Patcher patcher;

        static Cache[] GetFieldsOfType(Type type) {
            if (!fieldCache.TryGetValue(type, out var fields))
                try {
                    tempFields.Clear();
                    var currentType = type;
                    while (currentType != null && currentType != typeof(object)) {
                        var allFields = currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        foreach (var field in allFields) {
                            var fieldType = field.FieldType;
                            if (targetType.IsAssignableFrom(fieldType)) {
                                tempFields.Add(new(field, FieldType.TargetType));
                                continue;
                            }
                            var underlyType = fieldType;
                            if (underlyType.IsArray) underlyType = underlyType.GetElementType();
                            if (underlyType.IsGenericType && underlyType.GetGenericTypeDefinition() == nullableType)
                                underlyType = Nullable.GetUnderlyingType(underlyType);
                            if (!underlyType.IsPrimitive &&
                                !underlyType.IsPointer &&
                                !underlyType.IsEnum &&
                                underlyType != stringType &&
                                !unityObjectType.IsAssignableFrom(underlyType) &&
                                (!fieldType.IsValueType || !field.IsInitOnly))
                                tempFields.Add(new(field, FieldType.ScanRecursive));
                        }
                        currentType = currentType.BaseType;
                    }
                    fields = tempFields.ToArray();
                    fieldCache[type] = fields;
                } finally {
                    tempFields.Clear();
                }
            return fields;
        }

        public FieldPatcher(Patcher patcher) => this.patcher = patcher;

        public object Patch(object obj) {
            if (obj == null) return null;
            var objType = obj.GetType();
            bool isValueType = objType.IsValueType;
            if (!isValueType && cache.TryGetValue(obj, out var patched)) return patched;
            if (obj is T tObj && patcher(ref tObj)) {
                if (!isValueType) cache.Add(obj, tObj);
                return tObj;
            }
            if (obj is Array arr) return Patch(arr);
            if (!isValueType) cache.Add(obj, obj);
            foreach (var field in GetFieldsOfType(objType)) {
                var value = field.fieldInfo.GetValue(obj);
                switch (field.fieldType) {
                    case FieldType.TargetType:
                        if (value is T tVal && patcher(ref tVal))
                            field.fieldInfo.SetValue(obj, tVal);
                        break;
                    case FieldType.ScanRecursive:
                        if (value is Array array)
                            field.fieldInfo.SetValue(obj, Patch(array));
                        else if (field.fieldInfo.FieldType.IsValueType || !ReferenceEquals(value, obj))
                            field.fieldInfo.SetValue(obj, Patch(value));
                        break;
                }
            }
            return obj;
        }

        public Array Patch(Array array) {
            if (array == null) return null;
            if (array is T[] tArray) return Patch(tArray);
            if (cache.TryGetValue(array, out var patched)) return (Array)patched;
            cache.Add(array, array);
            for (int i = 0, length = array.Length; i < length; i++) {
                var element = array.GetValue(i);
                if (element == null)
                    continue;
                if (element is T tElm && patcher(ref tElm)) {
                    array.SetValue(tElm, i);
                    continue;
                }
                array.SetValue(Patch(element), i);
            }
            return array;
        }

        public T[] Patch(T[] array) {
            if (array == null) return null;
            if (cache.TryGetValue(array, out var patched)) return (T[])patched;
            cache.Add(array, array);
            for (int i = 0, length = array.Length; i < length; i++) {
                var element = array[i];
                if (patcher(ref element))
                    array[i] = element;
            }
            return array;
        }   

        readonly struct Cache {
            public readonly FieldInfo fieldInfo;
            public readonly FieldType fieldType;

            public Cache(FieldInfo fieldInfo, FieldType fieldType) {
                this.fieldInfo = fieldInfo;
                this.fieldType = fieldType;
            }
        }

        enum FieldType {
            Ignore,
            TargetType,
            ScanRecursive,
        }
    }
}