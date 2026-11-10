using System;
using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.NDExtensions.Editors.AnimatorSimplePatcherConverter {
    static class TypeDB {
        static readonly Dictionary<string, Type> typeMap;

        static TypeDB() {
            typeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in type.GetTypes())
                    if (t.IsSubclassOf(typeof(Component)))
                        typeMap[t.Name] = t;
            typeMap["GameObject"] = typeof(GameObject);
        }

        public static Type TryGetType(string name) {
            var type = Type.GetType(name);
            if (type != null && type.IsSubclassOf(typeof(Component)))
                return type;
            typeMap.TryGetValue(name, out type);
            return type;
        }
    }
}