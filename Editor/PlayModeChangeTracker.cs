using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModeChangeTracker
    {
        private static readonly HashSet<string> EditedIds = new();
        private static readonly List<UnityEngine.Object> Edited = new();

        private static bool _repaintQueued;

        internal static event Action Edit;

        static PlayModeChangeTracker()
        {
            Undo.postprocessModifications += OnModifications;
            Undo.undoRedoPerformed += OnUndoRedo;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static IReadOnlyList<UnityEngine.Object> EditedTargets => Edited;

        internal static void NoteEditForTests(UnityEngine.Object target) => Note(target);

        internal static void ResetForTests() => Reset();

        private static bool Following => EditorApplication.isPlaying && PlayModeKeeperSettings.Enabled;

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                Reset();
        }

        private static void Reset()
        {
            EditedIds.Clear();
            Edited.Clear();
        }

        private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (!Following)
                return modifications;
            for (int i = 0; i < modifications.Length; i++)
            {
                var target = modifications[i].currentValue?.target;
                if (target == null)
                    target = modifications[i].previousValue?.target;
                Note(target);
            }
            return modifications;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!Following)
                return;
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties);
#if UNITY_6000_5_OR_NEWER
                        Note(PlayModeObjectRef.FromEvent(properties.entityId));
#else
                        Note(PlayModeObjectRef.FromEvent(properties.instanceId));
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var parent);
#if UNITY_6000_5_OR_NEWER
                        Note(PlayModeObjectRef.FromEvent(parent.entityId));
#else
                        Note(PlayModeObjectRef.FromEvent(parent.instanceId));
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structure);
#if UNITY_6000_5_OR_NEWER
                        Note(PlayModeObjectRef.FromEvent(structure.entityId));
#else
                        Note(PlayModeObjectRef.FromEvent(structure.instanceId));
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var hierarchy);
#if UNITY_6000_5_OR_NEWER
                        Note(PlayModeObjectRef.FromEvent(hierarchy.entityId));
#else
                        Note(PlayModeObjectRef.FromEvent(hierarchy.instanceId));
#endif
                        break;
                }
            }
        }

        private static void OnUndoRedo()
        {
            if (!Following)
                return;
            PlayModeSaveState.ForgetReadings();
            Edit?.Invoke();
            QueueRepaint();
        }

        private static void Note(UnityEngine.Object target)
        {
            GameObject go = target as GameObject;
            if (go == null && target is Component component && component != null)
                go = component.gameObject;
            if (go == null || !PlayModeValueSaver.IsSceneObject(go))
                return;

            PlayModeSaveState.NoteEdit(target);
            if (target != go)
                PlayModeSaveState.NoteEdit(go);
            if (!PlayModeValueSaver.IsCreatedDuringPlay(go) && EditedIds.Add(PlayModeObjectRef.Of(target)))
                Edited.Add(target);
            Edit?.Invoke();
            QueueRepaint();
        }

        // Undo.postprocessModifications runs before the edit is applied.
        private static void QueueRepaint()
        {
            if (_repaintQueued)
                return;
            _repaintQueued = true;
            EditorApplication.delayCall += () =>
            {
                _repaintQueued = false;
                InternalEditorUtility.RepaintAllViews();
            };
        }
    }
}
