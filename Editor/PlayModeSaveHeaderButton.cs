using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModeSaveHeaderButton
    {
        private static readonly Color ModifiedColor = new(1f, 0.6f, 0.15f);
        private static readonly Color SavedColor = new(0.35f, 0.9f, 0.45f);

        private static FieldInfo _headerItemsField;
        private static Delegate _headerItem;

        private static GUIContent _cleanContent;
        private static GUIContent _modifiedContent;
        private static GUIContent _savedContent;

        internal static bool HookAvailable => _headerItem != null;

        static PlayModeSaveHeaderButton()
        {
            _headerItemsField = typeof(EditorGUIUtility).GetField(
                "s_EditorHeaderItemsMethods", BindingFlags.NonPublic | BindingFlags.Static);
            var delegateType = typeof(EditorGUIUtility).GetNestedType(
                "HeaderItemDelegate", BindingFlags.NonPublic);
            var method = typeof(PlayModeSaveHeaderButton).GetMethod(
                nameof(DrawItem), BindingFlags.NonPublic | BindingFlags.Static);

            if (_headerItemsField == null || delegateType == null || method == null)
            {
                PlayModeKeeperLog.Error("EditorGUIUtility header item hook is gone " +
                               $"(field {_headerItemsField != null}, delegate type {delegateType != null}, " +
                               $"method {method != null}). The component save buttons will not be drawn; " +
                               "the GameObject header button, the Hierarchy buttons, the property context menu " +
                               "and the Tools menu still work.");
                return;
            }

            _headerItem = Delegate.CreateDelegate(delegateType, method);
            if (!TryInstall())
                UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        private static bool _retryQueued;

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (TryInstall())
            {
                UnityEditor.Editor.finishedDefaultHeaderGUI -= OnHeaderGUI;
                return;
            }
            if (_retryQueued)
                return;
            // The GameObject header does not create the list; the component title bars drawn after it do.
            _retryQueued = true;
            EditorApplication.delayCall += () =>
            {
                _retryQueued = false;
                if (!TryInstall())
                    return;
                UnityEditor.Editor.finishedDefaultHeaderGUI -= OnHeaderGUI;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            };
        }

        // Null until header items are first drawn; creating it here would drop Unity's built-in items.
        private static bool TryInstall()
        {
            if (_headerItemsField.GetValue(null) is not IList headerItems)
                return false;
            if (!headerItems.Contains(_headerItem))
                headerItems.Add(_headerItem);
            return true;
        }

        private static bool DrawItem(Rect rect, UnityEngine.Object[] targets)
        {
            if (!EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return false;
            if (targets.Length == 0 || targets[0] is not Component first)
                return false;
            if (!PlayModeValueSaver.IsSceneObject(first.gameObject))
                return false;

            var state = PlayModeSaveState.Of(targets);
            var previousColor = GUI.color;
            GUI.color = ColorOf(state);
            bool pressed = GUI.Button(rect, ContentOf(state), EditorStyles.iconButton);
            GUI.color = previousColor;

            if (pressed)
                PlayModeValueSaver.SaveComponents(targets);
            return true;
        }

        internal static Color ColorOf(SaveState state)
        {
            switch (state)
            {
                case SaveState.Modified: return ModifiedColor;
                case SaveState.Saved: return SavedColor;
                default: return Color.white;
            }
        }

        private static GUIContent ContentOf(SaveState state)
        {
            if (_cleanContent == null)
            {
                _cleanContent = IconContent("Save values now — applied to the scene when Play stops");
                _modifiedContent = IconContent("Unsaved changes — press to save");
                _savedContent = IconContent("Saved — values will be applied when Play stops");
            }

            switch (state)
            {
                case SaveState.Modified: return _modifiedContent;
                case SaveState.Saved: return _savedContent;
                default: return _cleanContent;
            }
        }

        internal static GUIContent IconContent(string tooltip)
        {
            // Built-in editor icon; not guaranteed to exist in every version.
            var image = EditorGUIUtility.IconContent("SaveFromPlay").image;
            return image != null ? new GUIContent(image, tooltip) : new GUIContent("S", tooltip);
        }
    }
}
