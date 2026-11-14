using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper
{
    internal sealed class PlayModeKeeperWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem(PlayModeValueSaver.MenuRoot + "Saved Values", false, 100)]
        private static void Open()
        {
            GetWindow<PlayModeKeeperWindow>().Show();
        }

        private void OnEnable()
        {
            PlayModeValueSaver.StoreChanged += Repaint;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            PlayModeValueSaver.StoreChanged -= Repaint;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change) => Repaint();

        private void OnGUI()
        {
            titleContent.text = "Saved Values";

            var set = PlayModeValueSaver.Peek();
            int count = set != null ? set.Records.Count + set.Added.Count + set.AddedComponents.Count : 0;

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"{count} saved", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(count == 0 || EditorApplication.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("Apply Now",
                                                    "Apply the saved values to the open scenes now (not available during Play)"),
                                     EditorStyles.toolbarButton))
                {
                    PlayModeValueSaver.Restore();
                    GUIUtility.ExitGUI();
                }
            }
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("Clear All", EditorStyles.toolbarButton))
                {
                    PlayModeValueSaver.ClearSaved();
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndHorizontal();

            if (set == null)
            {
                EditorGUILayout.HelpBox(
                    "Nothing is saved. Press the Save button on a component's title bar during Play and it is listed here.",
                    MessageType.Info);
                return;
            }

            bool forget = false;
            var forgetKind = PlayModeValueSaver.RecordKind.Values;
            int forgetIndex = 0;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < set.Added.Count; i++)
            {
                var record = set.Added[i];
                if (Row($"Recreate: {record.Name}", record.ParentNamePath,
                        "", record.RootInstanceId))
                {
                    forget = true;
                    forgetKind = PlayModeValueSaver.RecordKind.CreatedObject;
                    forgetIndex = i;
                }
            }
            for (int i = 0; i < set.AddedComponents.Count; i++)
            {
                var record = set.AddedComponents[i];
                if (Row($"Add: {PlayModeValueSaver.TypeName(record.ComponentType)}",
                        record.NamePath, $"{record.Values.Count} value(s)",
                        record.InstanceId))
                {
                    forget = true;
                    forgetKind = PlayModeValueSaver.RecordKind.AddedComponent;
                    forgetIndex = i;
                }
            }
            for (int i = 0; i < set.Records.Count; i++)
            {
                var record = set.Records[i];
                string what = record.IsGameObject ? "Object" : PlayModeValueSaver.TypeName(record.ComponentType);
                string detail = record.Partial
                    ? $"{record.Values.Count} value(s), partial"
                    : $"{record.Values.Count} value(s)";
                if (Row(what, record.NamePath, detail, record.InstanceId))
                {
                    forget = true;
                    forgetKind = PlayModeValueSaver.RecordKind.Values;
                    forgetIndex = i;
                }
            }
            EditorGUILayout.EndScrollView();

            if (forget)
            {
                PlayModeValueSaver.Forget(forgetKind, forgetIndex);
                GUIUtility.ExitGUI();
            }
        }

        private static bool Row(string what, string where, string detail, string objectId)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent($"{what}  —  {where}", where), EditorStyles.label,
                                 GUILayout.MinWidth(80f)))
            {
                var go = PlayModeObjectRef.ToGameObject(objectId);
                if (go != null)
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                }
            }
            GUILayout.FlexibleSpace();
            if (detail.Length > 0)
                GUILayout.Label(detail, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            bool forget = GUILayout.Button(new GUIContent("×", "Forget this save"),
                                           EditorStyles.miniButton, GUILayout.Width(22f));
            GUILayout.EndHorizontal();
            return forget;
        }
    }
}
