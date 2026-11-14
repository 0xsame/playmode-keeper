using System;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    internal static class PlayModeKeeperSettings
    {
        private const string EnabledKey = "PlayModeKeeper.Enabled";
        private const string HiddenFieldsKey = "PlayModeKeeper.IncludeHiddenFields";
        private const string MaxArraySizeKey = "PlayModeKeeper.MaxArraySize";

        internal const int DefaultMaxArraySize = 10000;

        private static bool? _hiddenFieldsOverride;
        private static int? _maxArraySizeOverride;

        private static bool _loaded;
        private static bool _enabled;
        private static bool _includeHiddenFields;
        private static int _maxArraySize;

        internal static event Action Changed;

        internal static bool Enabled
        {
            get { Load(); return _enabled; }
            set
            {
                Load();
                if (_enabled == value)
                    return;
                _enabled = value;
                EditorPrefs.SetBool(EnabledKey, value);
                Changed?.Invoke();
            }
        }

        internal static bool IncludeHiddenFields
        {
            get
            {
                if (_hiddenFieldsOverride.HasValue)
                    return _hiddenFieldsOverride.Value;
                Load();
                return _includeHiddenFields;
            }
            set
            {
                Load();
                if (_includeHiddenFields == value)
                    return;
                _includeHiddenFields = value;
                EditorPrefs.SetBool(HiddenFieldsKey, value);
                Changed?.Invoke();
            }
        }

        internal static int MaxArraySize
        {
            get
            {
                if (_maxArraySizeOverride.HasValue)
                    return _maxArraySizeOverride.Value;
                Load();
                return _maxArraySize;
            }
            set
            {
                Load();
                value = Mathf.Max(0, value);
                if (_maxArraySize == value)
                    return;
                _maxArraySize = value;
                EditorPrefs.SetInt(MaxArraySizeKey, value);
                Changed?.Invoke();
            }
        }

        internal static void OverrideForTests(bool? includeHiddenFields, int? maxArraySize)
        {
            _hiddenFieldsOverride = includeHiddenFields;
            _maxArraySizeOverride = maxArraySize;
            Changed?.Invoke();
        }

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _enabled = EditorPrefs.GetBool(EnabledKey, true);
            _includeHiddenFields = EditorPrefs.GetBool(HiddenFieldsKey, true);
            _maxArraySize = Mathf.Max(0, EditorPrefs.GetInt(MaxArraySizeKey, DefaultMaxArraySize));
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/PlayMode Keeper", SettingsScope.User)
            {
                label = "PlayMode Keeper",
                guiHandler = _ => DrawPreferences(),
                keywords = new[] { "play", "mode", "keeper", "save", "inspector" },
            };
        }

        private static void DrawPreferences()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 240f;

            Enabled = EditorGUILayout.Toggle(
                new GUIContent("Enabled", "Turn off to hide the buttons and disable the menu and the shortcut"),
                Enabled);

            EditorGUILayout.Space();
            IncludeHiddenFields = EditorGUILayout.Toggle(
                new GUIContent("Save fields hidden from the Inspector",
                               "Save a script's [HideInInspector] fields as well. Needed for values that a custom editor draws"),
                IncludeHiddenFields);
            MaxArraySize = EditorGUILayout.DelayedIntField(
                new GUIContent("Longest array saved", "A longer array is left out of the save and counted in the log"),
                MaxArraySize);

            EditorGUIUtility.labelWidth = labelWidth;
        }
    }
}
