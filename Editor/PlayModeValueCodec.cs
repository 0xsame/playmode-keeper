using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    internal static class PlayModeValueCodec
    {
        internal const string NullReference = "n";
        internal const string UnsavableReference = "x";

        private const string ArraySizeSuffix = ".Array.size";
        private const string ArrayDataMarker = ".Array.data[";

        internal sealed class WalkStats
        {
            public int LargeArrays;
        }

        internal sealed class Context
        {
            public bool IdsValid;
            public readonly Dictionary<string, GameObject> Recreated = new();
            public readonly HashSet<GameObject> RecreatedObjects = new();
            public readonly Dictionary<string, int> ArraySizesBefore = new();
            public readonly StringBuilder Problems = new();
            public int FailedReferences;
            public string Owner = "";

            public GameObject Resolve(string id)
            {
                if (string.IsNullOrEmpty(id))
                    return null;
                if (Recreated.TryGetValue(id, out var recreated) && recreated != null)
                    return recreated;
                if (!IdsValid)
                    return null;
                var go = PlayModeObjectRef.ToGameObject(id);
                if (go == null || EditorUtility.IsPersistent(go) || !go.scene.IsValid())
                    return null;
                return go;
            }
        }

        internal static IEnumerable<SerializedProperty> Leaves(SerializedObject so, WalkStats stats)
        {
            var target = so.targetObject;
            bool all = target is MonoBehaviour && PlayModeKeeperSettings.IncludeHiddenFields;
            if (!all)
            {
                // Not reached by NextVisible; the Inspector draws it in the title bar.
                var enabled = so.FindProperty("m_Enabled");
                if (enabled != null)
                    yield return enabled;
                // The Euler angles the Inspector shows for the rotation.
                if (target is Transform)
                {
                    var hint = so.FindProperty("m_LocalEulerAnglesHint");
                    if (hint != null)
                        yield return hint;
                }
            }

            foreach (var leaf in Iterate(so.GetIterator(), null, all, stats))
                yield return leaf;
        }

        internal static IEnumerable<SerializedProperty> LeavesUnder(SerializedProperty root, WalkStats stats)
        {
            switch (root.propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (root.isArray && root.arraySize > PlayModeKeeperSettings.MaxArraySize)
                    {
                        stats.LargeArrays++;
                        yield break;
                    }
                    break;
                case SerializedPropertyType.ExposedReference:
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Gradient:
                    break;
                case SerializedPropertyType.ManagedReference:
                    yield return root;
                    break;
                case SerializedPropertyType.FixedBufferSize:
                    yield break;
                default:
                    yield return root;
                    yield break;
            }

            foreach (var leaf in Iterate(root.Copy(), root.GetEndProperty(true), true, stats))
                yield return leaf;
        }

        private static IEnumerable<SerializedProperty> Iterate(SerializedProperty it, SerializedProperty end,
                                                               bool all, WalkStats stats)
        {
            int limit = PlayModeKeeperSettings.MaxArraySize;
            HashSet<long> visitedReferences = null;
            bool enter = true;
            while (all ? it.Next(enter) : it.NextVisible(enter))
            {
                if (end != null && SerializedProperty.EqualContents(it, end))
                    break;
                // Vector3, string and other value properties have children too; only containers are entered.
                enter = false;
                if (it.depth == 0 && IsStructural(it.name))
                    continue;

                switch (it.propertyType)
                {
                    case SerializedPropertyType.Generic:
                        if (it.isArray && it.arraySize > limit)
                        {
                            stats.LargeArrays++;
                            continue;
                        }
                        enter = true;
                        continue;

                    case SerializedPropertyType.ManagedReference:
                        yield return it;
                        long id = it.managedReferenceId;
                        visitedReferences ??= new HashSet<long>();
                        enter = id >= 0 && visitedReferences.Add(id);
                        continue;

                    case SerializedPropertyType.ExposedReference:
                        enter = true;
                        continue;

                    case SerializedPropertyType.AnimationCurve:
                    case SerializedPropertyType.Gradient:
                        if (all)
                        {
                            enter = true;
                            continue;
                        }
                        // The children of a curve or gradient are not visible to NextVisible.
                        foreach (var inner in Iterate(it.Copy(), it.GetEndProperty(true), true, stats))
                            yield return inner;
                        continue;

                    case SerializedPropertyType.FixedBufferSize:
                        continue;

                    default:
                        yield return it;
                        continue;
                }
            }
        }

        private static bool IsStructural(string name)
        {
            switch (name)
            {
                case "m_ObjectHideFlags":
                case "m_CorrespondingSourceObject":
                case "m_PrefabInstance":
                case "m_PrefabAsset":
                case "m_GameObject":
                case "m_Script":
                case "m_EditorHideFlags":
                case "m_EditorClassIdentifier":
                case "m_Name":
                case "m_Father":
                case "m_Children":
                case "m_RootOrder":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryEncode(SerializedProperty p, out string value)
        {
            var c = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = p.numericType == SerializedPropertyNumericType.UInt64
                        ? p.ulongValue.ToString(c)
                        : p.longValue.ToString(c);
                    return true;
                case SerializedPropertyType.Boolean:
                    value = p.boolValue ? "1" : "0"; return true;
                case SerializedPropertyType.Float:
                    value = p.doubleValue.ToString("R", c); return true;
                case SerializedPropertyType.String:
                    value = p.stringValue; return true;
                case SerializedPropertyType.Enum:
                    // intValue rather than enumValueIndex: a flags value with several bits set has no index.
                    value = p.intValue.ToString(c); return true;
                case SerializedPropertyType.Color:
                    var col = p.colorValue;
                    value = Join(c, col.r, col.g, col.b, col.a); return true;
                case SerializedPropertyType.Vector2:
                    value = Join(c, p.vector2Value.x, p.vector2Value.y); return true;
                case SerializedPropertyType.Vector3:
                    value = Join(c, p.vector3Value.x, p.vector3Value.y, p.vector3Value.z); return true;
                case SerializedPropertyType.Vector4:
                    var v4 = p.vector4Value;
                    value = Join(c, v4.x, v4.y, v4.z, v4.w); return true;
                case SerializedPropertyType.Quaternion:
                    var q = p.quaternionValue;
                    value = Join(c, q.x, q.y, q.z, q.w); return true;
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    value = p.intValue.ToString(c); return true;
                case SerializedPropertyType.Rect:
                    var rect = p.rectValue;
                    value = Join(c, rect.x, rect.y, rect.width, rect.height); return true;
                case SerializedPropertyType.Bounds:
                    var bounds = p.boundsValue;
                    value = Join(c, bounds.center.x, bounds.center.y, bounds.center.z,
                                 bounds.extents.x, bounds.extents.y, bounds.extents.z); return true;
                case SerializedPropertyType.Vector2Int:
                    value = JoinInt(c, p.vector2IntValue.x, p.vector2IntValue.y); return true;
                case SerializedPropertyType.Vector3Int:
                    var v3i = p.vector3IntValue;
                    value = JoinInt(c, v3i.x, v3i.y, v3i.z); return true;
                case SerializedPropertyType.RectInt:
                    var recti = p.rectIntValue;
                    value = JoinInt(c, recti.x, recti.y, recti.width, recti.height); return true;
                case SerializedPropertyType.BoundsInt:
                    var boundsi = p.boundsIntValue;
                    value = JoinInt(c, boundsi.position.x, boundsi.position.y, boundsi.position.z,
                                    boundsi.size.x, boundsi.size.y, boundsi.size.z); return true;
                case SerializedPropertyType.Hash128:
                    value = p.hash128Value.ToString(); return true;
                case SerializedPropertyType.ObjectReference:
                    value = EncodeReference(p); return true;
                case SerializedPropertyType.ManagedReference:
                    value = "t:" + p.managedReferenceFullTypename; return true;
                default:
                    value = null; return false;
            }
        }

        internal static bool TryDecode(SerializedProperty p, string value, Context context)
        {
            var c = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (p.numericType == SerializedPropertyNumericType.UInt64)
                    {
                        if (!ulong.TryParse(value, NumberStyles.Integer, c, out ulong ul)) return false;
                        p.ulongValue = ul; return true;
                    }
                    if (!long.TryParse(value, NumberStyles.Integer, c, out long l)) return false;
                    p.longValue = l; return true;
                case SerializedPropertyType.Boolean:
                    p.boolValue = value == "1"; return true;
                case SerializedPropertyType.Float:
                    if (!double.TryParse(value, NumberStyles.Float, c, out double d)) return false;
                    p.doubleValue = d; return true;
                case SerializedPropertyType.String:
                    p.stringValue = value; return true;
                case SerializedPropertyType.Enum:
                    if (!int.TryParse(value, NumberStyles.Integer, c, out int e)) return false;
                    p.intValue = e; return true;
                case SerializedPropertyType.Color:
                    if (!TrySplit(value, 4, out float[] cf)) return false;
                    p.colorValue = new Color(cf[0], cf[1], cf[2], cf[3]); return true;
                case SerializedPropertyType.Vector2:
                    if (!TrySplit(value, 2, out float[] v2)) return false;
                    p.vector2Value = new Vector2(v2[0], v2[1]); return true;
                case SerializedPropertyType.Vector3:
                    if (!TrySplit(value, 3, out float[] v3)) return false;
                    p.vector3Value = new Vector3(v3[0], v3[1], v3[2]); return true;
                case SerializedPropertyType.Vector4:
                    if (!TrySplit(value, 4, out float[] v4)) return false;
                    p.vector4Value = new Vector4(v4[0], v4[1], v4[2], v4[3]); return true;
                case SerializedPropertyType.Quaternion:
                    if (!TrySplit(value, 4, out float[] qf)) return false;
                    p.quaternionValue = new Quaternion(qf[0], qf[1], qf[2], qf[3]); return true;
                case SerializedPropertyType.ArraySize:
                    if (!int.TryParse(value, NumberStyles.Integer, c, out int size)) return false;
                    string sizePath = p.propertyPath;
                    if (sizePath.EndsWith(ArraySizeSuffix, StringComparison.Ordinal))
                        context.ArraySizesBefore[sizePath.Substring(0, sizePath.Length - ArraySizeSuffix.Length)] = p.intValue;
                    p.intValue = size; return true;
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    if (!int.TryParse(value, NumberStyles.Integer, c, out int n)) return false;
                    p.intValue = n; return true;
                case SerializedPropertyType.Rect:
                    if (!TrySplit(value, 4, out float[] rf)) return false;
                    p.rectValue = new Rect(rf[0], rf[1], rf[2], rf[3]); return true;
                case SerializedPropertyType.Bounds:
                    if (!TrySplit(value, 6, out float[] bf)) return false;
                    // The Bounds constructor takes the size, which is twice the stored extents.
                    p.boundsValue = new Bounds(new Vector3(bf[0], bf[1], bf[2]),
                                               new Vector3(bf[3], bf[4], bf[5]) * 2f); return true;
                case SerializedPropertyType.Vector2Int:
                    if (!TrySplitInt(value, 2, out int[] v2i)) return false;
                    p.vector2IntValue = new Vector2Int(v2i[0], v2i[1]); return true;
                case SerializedPropertyType.Vector3Int:
                    if (!TrySplitInt(value, 3, out int[] v3i)) return false;
                    p.vector3IntValue = new Vector3Int(v3i[0], v3i[1], v3i[2]); return true;
                case SerializedPropertyType.RectInt:
                    if (!TrySplitInt(value, 4, out int[] ri)) return false;
                    p.rectIntValue = new RectInt(ri[0], ri[1], ri[2], ri[3]); return true;
                case SerializedPropertyType.BoundsInt:
                    if (!TrySplitInt(value, 6, out int[] bi)) return false;
                    p.boundsIntValue = new BoundsInt(new Vector3Int(bi[0], bi[1], bi[2]),
                                                     new Vector3Int(bi[3], bi[4], bi[5])); return true;
                case SerializedPropertyType.Hash128:
                    p.hash128Value = Hash128.Parse(value); return true;
                case SerializedPropertyType.ObjectReference:
                    return DecodeReference(p, value, context);
                case SerializedPropertyType.ManagedReference:
                    return DecodeManagedReference(p, value, context);
                default:
                    return false;
            }
        }

        private static readonly List<Component> ComponentBuffer = new();

        internal static string EncodeReference(SerializedProperty p)
        {
            var target = p.objectReferenceValue;
            if (target == null)
            {
                // A reference to an object destroyed during Play also reads as null.
                return IsMissingReference(p) ? UnsavableReference : NullReference;
            }
            return EncodeReference(target);
        }

        internal static string EncodeReference(UnityEngine.Object target)
        {
            if (target == null)
                return NullReference;

            if (EditorUtility.IsPersistent(target))
            {
                return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long fileId)
                    ? "a:" + guid + ":" + fileId.ToString(CultureInfo.InvariantCulture)
                    : UnsavableReference;
            }

            var component = target as Component;
            var go = component != null ? component.gameObject : target as GameObject;
            if (go == null)
                return UnsavableReference;
            if (component == null)
                return "s:" + PlayModeObjectRef.Of(go);

            return "s:" + PlayModeObjectRef.Of(go) + ":" + PlayModeObjectRef.Of(component) + ":" +
                   IndexAmongSameType(component).ToString(CultureInfo.InvariantCulture) + ":" +
                   component.GetType().AssemblyQualifiedName;
        }

        private static bool DecodeReference(SerializedProperty p, string value, Context context)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            switch (value[0])
            {
                case 'n':
                    p.objectReferenceValue = null;
                    return true;

                case 'a':
                {
                    string[] parts = value.Split(':');
                    var asset = parts.Length == 3 &&
                                long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId)
                        ? LoadAsset(parts[1], fileId)
                        : null;
                    if (asset == null)
                    {
                        context.FailedReferences++;
                        context.Problems.AppendLine($"  {context.Owner}: the asset {p.propertyPath} pointed at " +
                                                    "was not found; the reference was left as it is");
                        return ClearIfGrown(p, context);
                    }
                    p.objectReferenceValue = asset;
                    return true;
                }

                case 's':
                {
                    var target = ResolveSceneReference(value, context);
                    if (target == null)
                    {
                        context.FailedReferences++;
                        context.Problems.AppendLine($"  {context.Owner}: the object {p.propertyPath} pointed at " +
                                                    "does not exist outside Play; the reference was left as it is");
                        return ClearIfGrown(p, context);
                    }
                    p.objectReferenceValue = target;
                    return true;
                }

                default:
                    return ClearIfGrown(p, context);
            }
        }

        internal static UnityEngine.Object ResolveSceneReference(string value, Context context)
        {
            if (string.IsNullOrEmpty(value) || value[0] != 's')
                return null;

            string[] parts = value.Split(new[] { ':' }, 5);
            if (parts.Length < 2)
                return null;
            var go = context.Resolve(parts[1]);
            if (go == null)
                return null;
            if (parts.Length == 2)
                return go;
            if (parts.Length != 5)
                return null;

            var type = Type.GetType(parts[4]);
            if (type == null)
                return null;
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index);

            go.GetComponents(ComponentBuffer);
            Component byIndex = null;
            int seen = 0;
            for (int i = 0; i < ComponentBuffer.Count; i++)
            {
                var candidate = ComponentBuffer[i];
                if (candidate == null || candidate.GetType() != type)
                    continue;
                if (context.IdsValid && PlayModeObjectRef.Of(candidate) == parts[2])
                    return candidate;
                if (seen == index)
                    byIndex = candidate;
                seen++;
            }
            return byIndex;
        }

        // Growing an array through SerializedProperty fills the new slots with a copy of the last element.
        private static bool ClearIfGrown(SerializedProperty p, Context context)
        {
            string path = p.propertyPath;
            int at = path.IndexOf(ArrayDataMarker, StringComparison.Ordinal);
            while (at >= 0)
            {
                int close = path.IndexOf(']', at);
                if (close < 0)
                    break;
                int digits = at + ArrayDataMarker.Length;
                if (context.ArraySizesBefore.TryGetValue(path.Substring(0, at), out int before) &&
                    int.TryParse(path.Substring(digits, close - digits), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out int index) &&
                    index >= before)
                {
                    p.objectReferenceValue = null;
                    return true;
                }
                at = path.IndexOf(ArrayDataMarker, close, StringComparison.Ordinal);
            }
            return false;
        }

        private static UnityEngine.Object LoadAsset(string guid, long fileId)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(main, out _, out long mainId) &&
                mainId == fileId)
                return main;
            // LoadAllAssetsAtPath is not valid for a scene file.
            if (main is SceneAsset)
                return null;

            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(all[i], out _, out long id) && id == fileId)
                    return all[i];
            }
            return null;
        }

        // objectReferenceInstanceIDValue is replaced by objectReferenceEntityIdValue in newer versions.
        private static readonly PropertyInfo ReferenceIdProperty = FindReferenceIdProperty();
        private static readonly object NoReferenceId = ReferenceIdProperty != null
            ? Activator.CreateInstance(ReferenceIdProperty.PropertyType)
            : null;

        private static PropertyInfo FindReferenceIdProperty()
        {
            var properties = typeof(SerializedProperty).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo fallback = null;
            for (int i = 0; i < properties.Length; i++)
            {
                string name = properties[i].Name;
                if (!name.StartsWith("objectReference", StringComparison.Ordinal) || !properties[i].CanRead ||
                    !properties[i].PropertyType.IsValueType)
                    continue;
                if (name == "objectReferenceEntityIdValue")
                    return properties[i];
                if (name == "objectReferenceInstanceIDValue")
                    fallback = properties[i];
            }
            return fallback;
        }

        private static bool IsMissingReference(SerializedProperty p)
        {
            if (ReferenceIdProperty == null)
                return false;
            object id = ReferenceIdProperty.GetValue(p);
            return id != null && !id.Equals(NoReferenceId);
        }

        internal static int IndexAmongSameType(Component component)
        {
            var type = component.GetType();
            component.gameObject.GetComponents(ComponentBuffer);
            int index = 0;
            for (int i = 0; i < ComponentBuffer.Count && ComponentBuffer[i] != component; i++)
            {
                if (ComponentBuffer[i] != null && ComponentBuffer[i].GetType() == type)
                    index++;
            }
            return index;
        }

        private static bool DecodeManagedReference(SerializedProperty p, string value, Context context)
        {
            string typename = value.Length > 2 ? value.Substring(2) : "";
            if (typename.Length == 0)
            {
                p.managedReferenceValue = null;
                return true;
            }

            object instance = CreateManagedInstance(typename);
            if (instance == null)
            {
                context.Problems.AppendLine($"  {context.Owner}: the type '{typename}' held by " +
                                            $"{p.propertyPath} no longer exists");
                return false;
            }
            p.managedReferenceValue = instance;
            return true;
        }

        // managedReferenceFullTypename is "Assembly Namespace.Type", with nested types separated by '/'.
        private static object CreateManagedInstance(string fullTypename)
        {
            int space = fullTypename.IndexOf(' ');
            if (space <= 0)
                return null;

            string assembly = fullTypename.Substring(0, space);
            string typeName = fullTypename.Substring(space + 1).Replace('/', '+');
            var type = Type.GetType(typeName + ", " + assembly);
            if (type == null)
                return null;

            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (Exception)
            {
                return FormatterServices.GetUninitializedObject(type);
            }
        }

        private static string Join(IFormatProvider c, params float[] parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(parts[i].ToString("R", c));
            }
            return sb.ToString();
        }

        private static string JoinInt(IFormatProvider c, params int[] parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(parts[i].ToString(c));
            }
            return sb.ToString();
        }

        private static bool TrySplitInt(string value, int count, out int[] parts)
        {
            parts = null;
            string[] split = value.Split(',');
            if (split.Length != count)
                return false;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(split[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }
            parts = result;
            return true;
        }

        private static bool TrySplit(string value, int count, out float[] parts)
        {
            parts = null;
            string[] split = value.Split(',');
            if (split.Length != count)
                return false;
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!float.TryParse(split[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                    return false;
            }
            parts = result;
            return true;
        }
    }
}
