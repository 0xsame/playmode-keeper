using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlayModeKeeper
{
    [InitializeOnLoad]
    internal static class PlayModeObjectHeaderButton
    {
        private const string ElementName = "playmode-keeper-object-save";
        private const string GameObjectInspectorClass = "game-object-inspector";
        private const float Size = 16f;
        private const float Gap = 2f;
        private const float Room = Size + Gap * 2f;
        private const float Top = 10f; // first header row, measured on 2022.3 and 6000.3
        private static readonly Color MarkOutline = new(0.1f, 0.1f, 0.1f);

        private static readonly Type InspectorWindowType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");

        private static readonly Dictionary<UnityEditor.Editor, VisualElement> Attached = new();
        private static readonly List<UnityEditor.Editor> Detached = new();
        private static readonly HashSet<UnityEditor.Editor> Pending = new();
        private static readonly List<UnityEditor.Editor> PendingBuffer = new();
        private static readonly List<GameObject> Existing = new();
        private static readonly List<GameObject> Created = new();
        private static readonly List<VisualElement> Candidates = new();

        private static PropertyInfo _editorProperty;
        private static bool _broken;
        private static GUIContent _saveContent;
        private static GUIContent _createdContent;
        private static GUIContent _bothContent;

        static PlayModeObjectHeaderButton()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            PlayModeKeeperSettings.Changed += DetachAll;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                DetachAll();
        }

        private static void DetachAll()
        {
            foreach (var element in Attached.Values)
                Detach(element);
            Attached.Clear();
        }

        private static void Detach(VisualElement button)
        {
            if (button.userData is VisualElement header)
                header.style.marginRight = StyleKeyword.Null;
            button.RemoveFromHierarchy();
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (_broken || !EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return;
            if (editor == null || editor.target is not GameObject)
                return;
            if (Attached.TryGetValue(editor, out var element) && element.panel != null)
                return;
            // The header is drawn during UI Toolkit layout, where elements must not be added.
            if (Pending.Count == 0)
                EditorApplication.delayCall += AttachPending;
            Pending.Add(editor);
        }

        private static void AttachPending()
        {
            PendingBuffer.Clear();
            PendingBuffer.AddRange(Pending);
            Pending.Clear();
            for (int i = 0; i < PendingBuffer.Count; i++)
            {
                var editor = PendingBuffer[i];
                if (_broken || editor == null || !EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                    continue;
                if (Attached.TryGetValue(editor, out var element) && element.panel != null)
                    continue;
                Attach(editor);
            }
            PendingBuffer.Clear();
        }

        private static void Attach(UnityEditor.Editor editor)
        {
            ForgetDetached();
            if (InspectorWindowType == null)
            {
                Fail("the InspectorWindow type");
                return;
            }

            foreach (var window in Resources.FindObjectsOfTypeAll(InspectorWindowType))
            {
                Candidates.Clear();
                ((EditorWindow)window).rootVisualElement.Query(className: GameObjectInspectorClass).ToList(Candidates);
                for (int i = 0; i < Candidates.Count; i++)
                {
                    var candidate = Candidates[i];
                    // The candidate is the internal UnityEditor.UIElements.EditorElement.
                    _editorProperty ??= candidate.GetType().GetProperty(
                        "editor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (_editorProperty == null)
                    {
                        Fail("the editor property of " + candidate.GetType().Name);
                        return;
                    }
                    if (!ReferenceEquals(_editorProperty.GetValue(candidate), editor))
                        continue;

                    var header = candidate.Q<IMGUIContainer>();
                    if (header == null)
                    {
                        Fail("the header of " + candidate.GetType().Name);
                        return;
                    }
                    var stale = candidate.Q(ElementName);
                    if (stale != null)
                        Detach(stale);
                    // A margin narrows the IMGUI header; padding does not.
                    header.style.marginRight = Room;

                    // Not a child of the IMGUIContainer: that collapses the header's measured height.
                    var button = new IMGUIContainer(() => Draw(editor)) { name = ElementName, userData = header };
                    button.style.position = Position.Absolute;
                    button.style.top = Top;
                    button.style.right = Gap;
                    button.style.width = Size;
                    button.style.height = Size;
                    candidate.Add(button);
                    Attached[editor] = button;
                    return;
                }
            }
            Candidates.Clear();
        }

        private static void ForgetDetached()
        {
            Detached.Clear();
            foreach (var pair in Attached)
            {
                if (pair.Key == null || pair.Value.panel == null)
                    Detached.Add(pair.Key);
            }
            for (int i = 0; i < Detached.Count; i++)
                Attached.Remove(Detached[i]);
            Detached.Clear();
        }

        private static void Fail(string what)
        {
            _broken = true;
            PlayModeKeeperLog.Error($"Could not find {what} in the Inspector. No save button will be drawn in " +
                                    "the GameObject header; the component title bar buttons, the Hierarchy " +
                                    "and the menu still work.");
        }

        private static void Draw(UnityEditor.Editor editor)
        {
            if (editor == null || !EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return;

            Existing.Clear();
            Created.Clear();
            var targets = editor.targets;
            for (int i = 0; i < targets.Length; i++)
            {
                var go = targets[i] as GameObject;
                if (!PlayModeValueSaver.IsSceneObject(go))
                    continue;
                if (PlayModeValueSaver.IsCreatedDuringPlay(go))
                    Created.Add(go);
                else
                    Existing.Add(go);
            }
            if (Existing.Count == 0 && Created.Count == 0)
                return;

            bool anyModified = false;
            bool anySaved = false;
            for (int i = 0; i < Existing.Count; i++)
                PlayModeSaveState.AccumulateAny(PlayModeSaveState.Of(Existing[i]), ref anyModified, ref anySaved);
            for (int i = 0; i < Created.Count; i++)
            {
                if (PlayModeValueSaver.IsSavedCreatedObject(Created[i]))
                    anySaved = true;
            }

            var rect = new Rect(0f, 0f, Size, Size);
            var previousColor = GUI.color;
            GUI.color = PlayModeSaveHeaderButton.ColorOf(PlayModeSaveState.Combine(anyModified, anySaved));
            bool pressed = GUI.Button(rect, ContentOf(), EditorStyles.iconButton);
            GUI.color = previousColor;
            if (Created.Count > 0 && Event.current.type == EventType.Repaint)
                DrawCreatedMark();
            if (!pressed)
                return;

            if (Existing.Count > 0)
                PlayModeValueSaver.SaveObjects(new List<GameObject>(Existing), SaveScope.Object);
            if (Created.Count > 0)
                PlayModeValueSaver.SaveObjects(new List<GameObject>(Created), SaveScope.Hierarchy);
        }

        private static void DrawCreatedMark()
        {
            EditorGUI.DrawRect(new Rect(6f, 9f, 10f, 4f), MarkOutline);
            EditorGUI.DrawRect(new Rect(9f, 6f, 4f, 10f), MarkOutline);
            EditorGUI.DrawRect(new Rect(7f, 10f, 8f, 2f), Color.white);
            EditorGUI.DrawRect(new Rect(10f, 7f, 2f, 8f), Color.white);
        }

        private static GUIContent ContentOf()
        {
            if (_saveContent == null)
            {
                _saveContent = PlayModeSaveHeaderButton.IconContent(
                    "Save this object's own state: active flag, name, layer, tag, static flags and where it sits in the hierarchy");
                _createdContent = PlayModeSaveHeaderButton.IconContent(
                    "Created during Play: press to recreate it, with everything under it, when Play stops");
                _bothContent = PlayModeSaveHeaderButton.IconContent(
                    "Save the own state of the objects that were there before Play, and recreate the ones created during it when Play stops");
            }
            if (Created.Count == 0)
                return _saveContent;
            return Existing.Count == 0 ? _createdContent : _bothContent;
        }
    }
}
