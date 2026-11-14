using System;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    /// <summary>Saves values during Play mode so that they are written back to the scene when Play stops.</summary>
    public static class Keeper
    {
        /// <summary>Whether a save would be recorded now: Play mode is running and the package is enabled.</summary>
        public static bool CanSave => EditorApplication.isPlaying && PlayModeKeeperSettings.Enabled;

        /// <summary>Whether anything is saved and not yet applied.</summary>
        public static bool HasSavedValues => PlayModeValueSaver.Peek() != null;

        /// <summary>Raised after saved values have been applied to the edit-mode scene.</summary>
        public static event Action Restored
        {
            add => PlayModeValueSaver.Restored += value;
            remove => PlayModeValueSaver.Restored -= value;
        }

        /// <summary>Saves the components' values, as their Save button does. Returns false if nothing was recorded.</summary>
        public static bool Save(params Component[] components)
        {
            if (!CanSave || components == null || components.Length == 0)
                return false;

            PlayModeValueSaver.SaveComponents(components);
            return true;
        }

        /// <summary>Saves the GameObject, optionally with its components and children. Returns false if nothing was recorded.</summary>
        public static bool Save(GameObject gameObject, bool includeComponents = true, bool includeChildren = false)
        {
            if (!CanSave || gameObject == null)
                return false;

            var scope = includeChildren
                ? SaveScope.Hierarchy
                : includeComponents ? SaveScope.ObjectAndComponents : SaveScope.Object;
            PlayModeValueSaver.SaveObjects(new[] { gameObject }, scope);
            return true;
        }

        /// <summary>Saves one property (a SerializedProperty.propertyPath) and everything under it. Returns false if nothing was recorded.</summary>
        public static bool SaveProperty(Component component, string propertyPath)
        {
            if (!CanSave || component == null || string.IsNullOrEmpty(propertyPath))
                return false;

            PlayModeValueSaver.SaveProperty(new UnityEngine.Object[] { component }, propertyPath);
            return true;
        }

        /// <summary>Saves the GameObjects selected in the Hierarchy with their components.</summary>
        public static void SaveSelection() => PlayModeValueSaver.SaveSelection();

        /// <summary>Applies saved values to the open scenes now. Does nothing during Play.</summary>
        public static void ApplySavedValues()
        {
            if (!EditorApplication.isPlaying)
                PlayModeValueSaver.Restore();
        }

        /// <summary>Discards everything that is saved without applying it.</summary>
        public static void ClearSavedValues() => PlayModeValueSaver.ClearSaved();
    }
}
