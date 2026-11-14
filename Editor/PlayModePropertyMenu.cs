using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModePropertyMenu
    {
        static PlayModePropertyMenu()
        {
            EditorApplication.contextualPropertyMenu += OnPropertyMenu;
        }

        private static void OnPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            if (!EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return;

            var targets = property.serializedObject.targetObjects;
            if (targets.Length == 0 || targets[0] is not Component component ||
                !PlayModeValueSaver.IsSceneObject(component.gameObject))
                return;

            // The SerializedProperty is reused after this callback, so the path is copied now.
            string path = property.propertyPath;
            menu.AddItem(new GUIContent("Keep This Value After Play"), false,
                         () => PlayModeValueSaver.SaveProperty(targets, path));
        }
    }
}
