using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    internal static class PlayModeObjectRef
    {
        internal static string Of(UnityEngine.Object o)
        {
            if (o == null)
                return "";
#if UNITY_6000_5_OR_NEWER
            return UnityEngine.EntityId.ToULong(o.GetEntityId()).ToString(CultureInfo.InvariantCulture);
#else
            return o.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#endif
        }

        internal static UnityEngine.Object ToObject(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
#if UNITY_6000_5_OR_NEWER
            if (!ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong raw))
                return null;
            return EditorUtility.EntityIdToObject(UnityEngine.EntityId.FromULong(raw));
#else
            if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
                return null;
            return EditorUtility.InstanceIDToObject(raw);
#endif
        }

        internal static GameObject ToGameObject(string id) => ToObject(id) as GameObject;

#if UNITY_6000_5_OR_NEWER
        internal static UnityEngine.Object FromEvent(UnityEngine.EntityId id) => EditorUtility.EntityIdToObject(id);
#else
        internal static UnityEngine.Object FromEvent(int instanceID) =>
            ToObject(instanceID.ToString(CultureInfo.InvariantCulture));
#endif

        // The Hierarchy row callback passes an int instanceID.
        internal static string OfHierarchyItem(int instanceID)
        {
#if UNITY_6000_5_OR_NEWER
            return Of(EditorUtility.EntityIdToObject(instanceID));
#else
            return instanceID.ToString(CultureInfo.InvariantCulture);
#endif
        }
    }
}
