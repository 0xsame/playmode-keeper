using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModeHierarchyMarks
    {
        private const float IconSize = 16f;
        private const float RightMargin = 2f;
        private const float PrefabArrowWidth = 16f;

        private static readonly Dictionary<string, SaveState> SavedMarks = new();
        private static readonly Dictionary<string, SaveState> EditMarks = new();
        private static readonly List<UnityEngine.Object> Saved = new();
        private static readonly List<GameObject> SavedCreatedRoots = new();

        private static bool _savedDirty = true;
        private static bool _editsDirty = true;
        private static GUIContent _modifiedContent;
        private static GUIContent _savedContent;

        static PlayModeHierarchyMarks()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnItemGUI;
            PlayModeChangeTracker.Edit += () => _editsDirty = true;
            PlayModeValueSaver.StoreChanged += OnStoreChanged;
            EditorApplication.playModeStateChanged += _ =>
            {
                _savedDirty = true;
                _editsDirty = true;
            };
        }

        internal static SaveState StateOf(GameObject go)
        {
            BuildSaved();
            BuildEdits();
            return StateOf(PlayModeObjectRef.Of(go));
        }

        private static void OnStoreChanged()
        {
            _savedDirty = true;
            _editsDirty = true;
            EditorApplication.RepaintHierarchyWindow();
        }

        private static void OnItemGUI(int instanceID, Rect rowRect)
        {
            if (!EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return;
            if (_savedDirty)
                BuildSaved();
            if (_editsDirty)
                BuildEdits();
            if (SavedMarks.Count == 0 && EditMarks.Count == 0)
                return;

            string id = PlayModeObjectRef.OfHierarchyItem(instanceID);
            var state = StateOf(id);
            if (state == SaveState.Clean)
                return;
            var go = PlayModeObjectRef.ToGameObject(id);
            if (go == null)
                return;

            float right = rowRect.xMax - RightMargin;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
                right -= PrefabArrowWidth;
            var rect = new Rect(right - IconSize, rowRect.y + (rowRect.height - IconSize) * 0.5f, IconSize, IconSize);

            var previousColor = GUI.color;
            GUI.color = PlayModeSaveHeaderButton.ColorOf(state);
            bool pressed = GUI.Button(rect, ContentOf(state), EditorStyles.iconButton);
            GUI.color = previousColor;

            if (pressed)
                PlayModeValueSaver.SaveObjects(new List<GameObject> { go }, SaveScope.Hierarchy);
        }

        private static SaveState StateOf(string id)
        {
            SavedMarks.TryGetValue(id, out var saved);
            EditMarks.TryGetValue(id, out var edited);
            return Rank(edited) >= Rank(saved) ? edited : saved;
        }

        private static GUIContent ContentOf(SaveState state)
        {
            if (_modifiedContent == null)
            {
                _modifiedContent = PlayModeSaveHeaderButton.IconContent(
                    "Unsaved changes in this object or under it — press to save it with everything under it");
                _savedContent = PlayModeSaveHeaderButton.IconContent(
                    "Saved — values in this object or under it will be applied when Play stops");
            }
            return state == SaveState.Saved ? _savedContent : _modifiedContent;
        }

        private static void BuildSaved()
        {
            SavedMarks.Clear();
            Saved.Clear();
            SavedCreatedRoots.Clear();
            PlayModeValueSaver.CollectSavedTargets(Saved, SavedCreatedRoots);
            for (int i = 0; i < Saved.Count; i++)
            {
                var go = OwnerOf(Saved[i]);
                if (go != null)
                    Climb(SavedMarks, go.transform, SaveState.Saved);
            }
            for (int i = 0; i < SavedCreatedRoots.Count; i++)
            {
                var parent = SavedCreatedRoots[i] != null ? SavedCreatedRoots[i].transform.parent : null;
                if (parent != null)
                    Climb(SavedMarks, parent, SaveState.Saved);
            }
            Saved.Clear();
            SavedCreatedRoots.Clear();
            _savedDirty = false;
        }

        private static void BuildEdits()
        {
            EditMarks.Clear();
            var edited = PlayModeChangeTracker.EditedTargets;
            for (int i = 0; i < edited.Count; i++)
            {
                var target = edited[i];
                if (target == null)
                    continue;
                var state = PlayModeSaveState.Of(target);
                if (state != SaveState.Clean)
                    Climb(EditMarks, OwnerOf(target).transform, state);
            }
            _editsDirty = false;
        }

        private static GameObject OwnerOf(UnityEngine.Object target)
        {
            if (target is GameObject go)
                return go;
            return target is Component component && component != null ? component.gameObject : null;
        }

        private static void Climb(Dictionary<string, SaveState> marks, Transform t, SaveState state)
        {
            for (; t != null; t = t.parent)
            {
                string id = PlayModeObjectRef.Of(t.gameObject);
                if (marks.TryGetValue(id, out var existing) && Rank(existing) >= Rank(state))
                    return;
                marks[id] = state;
            }
        }

        private static int Rank(SaveState state)
        {
            switch (state)
            {
                case SaveState.Modified: return 2;
                case SaveState.Saved: return 1;
                default: return 0;
            }
        }
    }
}
