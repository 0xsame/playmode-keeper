using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModeSnapshot
    {
        private const string ObjectKey = "PlayModeKeeper.PrePlayIds";
        private const string ComponentKey = "PlayModeKeeper.PrePlayComponentIds";
        private const string SceneKey = "PlayModeKeeper.PrePlayScenes";

        private const string EnteredKey = "PlayModeKeeper.EnteredPlay";

        private static HashSet<string> _objectIds;
        private static HashSet<string> _componentIds;
        private static HashSet<string> _scenePaths;

        private static bool _entered;

        internal static HashSet<string> ObjectIds => _objectIds;

        internal static HashSet<string> ComponentIds => _componentIds;

        static PlayModeSnapshot()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (EditorApplication.isPlaying && SessionState.GetBool(EnteredKey, false))
            {
                ReadBack();
                _entered = true;
            }
        }

        internal static void SetIdsForTests(HashSet<string> objectIds, HashSet<string> componentIds)
        {
            _objectIds = objectIds;
            _componentIds = componentIds;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    Collect();
                    SetEntered(false);
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    if (_objectIds == null)
                        ReadBack();
                    SetEntered(true);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    _objectIds = null;
                    _componentIds = null;
                    _scenePaths = null;
                    SetEntered(false);
                    break;
            }
        }

        private static void SetEntered(bool entered)
        {
            _entered = entered;
            SessionState.SetBool(EnteredKey, entered);
        }

        private static void Collect()
        {
            var objects = new StringBuilder();
            var components = new StringBuilder();
            var scenes = new StringBuilder();
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                if (scenes.Length > 0)
                    scenes.Append('\n');
                scenes.Append(scene.path);
                AppendScene(scene, objects, components);
            }
            SessionState.SetString(ObjectKey, objects.ToString());
            SessionState.SetString(ComponentKey, components.ToString());
            SessionState.SetString(SceneKey, scenes.ToString());
        }

        private static void ReadBack()
        {
            _objectIds = ParseIds(SessionState.GetString(ObjectKey, null), ',');
            _componentIds = ParseIds(SessionState.GetString(ComponentKey, null), ',');
            _scenePaths = ParseIds(SessionState.GetString(SceneKey, null), '\n');
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!EditorApplication.isPlaying)
                return;
            if (_objectIds == null)
                ReadBack();
            if (_objectIds == null || _componentIds == null || _scenePaths == null)
                return;
            if (string.IsNullOrEmpty(scene.path))
                return;
            // sceneLoaded also fires for the scenes that were already open when Play started.
            if (!_entered && _scenePaths.Contains(scene.path))
                return;
            bool firstTime = _scenePaths.Add(scene.path);

            var objects = new StringBuilder();
            var components = new StringBuilder();
            AppendScene(scene, objects, components);
            AddIds(_objectIds, objects.ToString());
            AddIds(_componentIds, components.ToString());

            Append(ObjectKey, objects, ',');
            Append(ComponentKey, components, ',');
            if (firstTime)
                Append(SceneKey, new StringBuilder(scene.path), '\n');
        }

        private static void Append(string key, StringBuilder addition, char separator)
        {
            if (addition.Length == 0)
                return;
            string existing = SessionState.GetString(key, "");
            SessionState.SetString(key, existing.Length > 0 ? existing + separator + addition : addition.ToString());
        }

        private static void AppendScene(Scene scene, StringBuilder objects, StringBuilder components)
        {
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
                AppendIds(roots[r].transform, objects, components);
        }

        private static void AppendIds(Transform t, StringBuilder objects, StringBuilder components)
        {
            if (objects.Length > 0)
                objects.Append(',');
            objects.Append(PlayModeObjectRef.Of(t.gameObject));

            var all = t.gameObject.GetComponents<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                if (components.Length > 0)
                    components.Append(',');
                components.Append(PlayModeObjectRef.Of(all[i]));
            }

            for (int i = 0; i < t.childCount; i++)
                AppendIds(t.GetChild(i), objects, components);
        }

        private static HashSet<string> ParseIds(string text, char separator)
        {
            if (text == null)
                return null;

            var ids = new HashSet<string>();
            AddIds(ids, text, separator);
            return ids;
        }

        private static void AddIds(HashSet<string> ids, string text, char separator = ',')
        {
            if (text.Length == 0)
                return;
            string[] parts = text.Split(separator);
            for (int i = 0; i < parts.Length; i++)
                ids.Add(parts[i]);
        }
    }
}
