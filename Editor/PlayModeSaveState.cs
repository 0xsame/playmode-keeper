using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PlayModeKeeper
{
    internal enum SaveState
    {
        Clean,
        Modified,
        Saved,
    }

    [InitializeOnLoad]
    internal static class PlayModeSaveState
    {
        private static readonly Dictionary<string, PlayModeValueSaver.Snapshot> _baseline = new();
        private static readonly Dictionary<string, SaveState> _cache = new();
        private static readonly List<Component> CompanionBuffer = new();

        static PlayModeSaveState()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            PlayModeKeeperSettings.Changed += Reset;
            PlayModeValueSaver.StoreChanged += OnStoreChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                Reset();
        }

        private static void Reset()
        {
            _baseline.Clear();
            _cache.Clear();
        }

        internal static void ForgetReadings() => _cache.Clear();

        internal static void ResetForTests() => Reset();

        private static void OnStoreChanged()
        {
            _cache.Clear();
            InternalEditorUtility.RepaintAllViews();
        }

        internal static SaveState Of(UnityEngine.Object[] targets)
        {
            bool anyModified = false;
            bool allSaved = true;
            int count = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    continue;

                count++;
                Accumulate(Of(targets[i]), ref anyModified, ref allSaved);

                if (targets[i] is not Component component)
                    continue;
                CompanionBuffer.Clear();
                PlayModeValueSaver.CompanionsOf(component, CompanionBuffer);
                for (int c = 0; c < CompanionBuffer.Count; c++)
                    Accumulate(Of(CompanionBuffer[c]), ref anyModified, ref allSaved);
            }

            if (anyModified)
                return SaveState.Modified;
            return allSaved && count > 0 ? SaveState.Saved : SaveState.Clean;
        }

        private static void Accumulate(SaveState state, ref bool anyModified, ref bool allSaved)
        {
            if (state == SaveState.Modified)
                anyModified = true;
            if (state != SaveState.Saved)
                allSaved = false;
        }

        internal static void AccumulateAny(SaveState state, ref bool anyModified, ref bool anySaved)
        {
            if (state == SaveState.Modified)
                anyModified = true;
            else if (state == SaveState.Saved)
                anySaved = true;
        }

        internal static SaveState Combine(bool anyModified, bool anySaved)
        {
            if (anyModified)
                return SaveState.Modified;
            return anySaved ? SaveState.Saved : SaveState.Clean;
        }

        internal static void NoteEdit(UnityEngine.Object target)
        {
            if (target == null)
                return;
            string id = PlayModeObjectRef.Of(target);
            _cache.Remove(id);
            if (!_baseline.ContainsKey(id) && !PlayModeValueSaver.TryGetSaved(target, out _, out _, out _))
                _baseline[id] = PlayModeValueSaver.Read(target);
        }

        internal static SaveState Of(UnityEngine.Object target)
        {
            string id = PlayModeObjectRef.Of(target);
            if (_cache.TryGetValue(id, out var cached))
                return cached;

            var current = PlayModeValueSaver.Read(target);
            SaveState state;
            if (PlayModeValueSaver.TryGetSaved(target, out var paths, out var values, out bool partial))
            {
                if (!partial)
                {
                    state = Same(current.Paths, current.Values, paths, values) ? SaveState.Saved : SaveState.Modified;
                }
                else
                {
                    if (!_baseline.TryGetValue(id, out var original))
                    {
                        original = current;
                        _baseline[id] = current;
                    }
                    state = MatchesPartial(current, paths, values, original) ? SaveState.Saved : SaveState.Modified;
                }
            }
            else if (_baseline.TryGetValue(id, out var baseline))
            {
                state = Same(current.Paths, current.Values, baseline.Paths, baseline.Values)
                    ? SaveState.Clean
                    : SaveState.Modified;
            }
            else
            {
                _baseline[id] = current;
                state = SaveState.Clean;
            }

            _cache[id] = state;
            return state;
        }

        private static bool Same(List<string> pathsA, List<string> valuesA, List<string> pathsB, List<string> valuesB)
        {
            if (pathsA.Count != pathsB.Count)
                return false;
            for (int i = 0; i < pathsA.Count; i++)
            {
                if (valuesA[i] != valuesB[i] || pathsA[i] != pathsB[i])
                    return false;
            }
            return true;
        }

        private static bool MatchesPartial(PlayModeValueSaver.Snapshot current, List<string> savedPaths,
                                           List<string> savedValues, PlayModeValueSaver.Snapshot original)
        {
            var saved = new Dictionary<string, string>(savedPaths.Count);
            for (int i = 0; i < savedPaths.Count; i++)
                saved[savedPaths[i]] = savedValues[i];
            var untouched = new Dictionary<string, string>(original.Paths.Count);
            for (int i = 0; i < original.Paths.Count; i++)
                untouched[original.Paths[i]] = original.Values[i];

            int matched = 0;
            for (int i = 0; i < current.Paths.Count; i++)
            {
                string path = current.Paths[i];
                if (saved.TryGetValue(path, out string value))
                {
                    if (value != current.Values[i])
                        return false;
                    matched++;
                }
                else if (!untouched.TryGetValue(path, out value) || value != current.Values[i])
                {
                    return false;
                }
            }
            return matched >= saved.Count;
        }
    }
}
