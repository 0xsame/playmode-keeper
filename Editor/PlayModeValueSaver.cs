using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlayModeKeeper
{
    internal enum SaveScope
    {
        Object,
        ObjectAndComponents,
        Hierarchy,
    }

    [InitializeOnLoad]
    internal static class PlayModeValueSaver
    {
        private const string SessionStampKey = "PlayModeKeeper.Session";
        private const string FilePath = "Library/PlayModeKeeper.json";

        private const string SavedObjectFolderName = "PlayModeKeeperSaved";
        private const string SavedObjectFolder = "Assets/" + SavedObjectFolderName;

        internal const string MenuRoot = "Tools/PlayMode Keeper/";
        private const string SaveSelectedMenu = MenuRoot + "Save Selected Objects &#s";
        private const string RestoreMenu = MenuRoot + "Restore Saved Values";
        private const string ClearMenu = MenuRoot + "Clear Saved Values";
        private const string PreferencesMenu = MenuRoot + "Preferences...";

        private const string RestoreUndoName = "Restore Play Mode values";

        internal const string ParentKey = "@parent";
        internal const string PreviousKey = "@previous";
        internal const string NextKey = "@next";

        private static readonly string[] GameObjectProperties =
        {
            "m_Name", "m_IsActive", "m_Layer", "m_TagString", "m_StaticEditorFlags",
        };

        [Serializable]
        internal sealed class Record
        {
            public string Session;
            public string ScenePath;
            public string SiblingPath;
            public string NamePath;
            public string InstanceId;
            public string ComponentType;
            public int ComponentIndex;
            public string ComponentId;
            public bool Partial;
            public List<string> PropertyPaths = new();
            public List<string> Values = new();

            public bool IsGameObject => string.IsNullOrEmpty(ComponentType);
        }

        [Serializable]
        internal sealed class RelinkRecord
        {
            public string LocalSiblingPath;
            public string ComponentType;
            public int ComponentIndex;
            public string PropertyPath;
            public string Target;
            public string TargetNamePath;
        }

        [Serializable]
        internal sealed class AddedRecord
        {
            public string Session;
            public string ScenePath;
            public string ParentSiblingPath;
            public string ParentNamePath;
            public string ParentInstanceId;
            public int SiblingIndex;
            public string Name;
            public string PrefabPath;
            public string RootInstanceId;
            public List<string> SubtreeIds = new();
            public List<RelinkRecord> Relinks = new();
        }

        [Serializable]
        internal sealed class RecordSet
        {
            public List<Record> Records = new();
            public List<AddedRecord> Added = new();
            public List<Record> AddedComponents = new();
        }

        internal enum RecordKind
        {
            Values,
            CreatedObject,
            AddedComponent,
        }

        internal sealed class Snapshot
        {
            public readonly List<string> Paths = new();
            public readonly List<string> Values = new();
        }

        internal static event Action StoreChanged;
        internal static event Action Restored;

        private static RecordSet _set;
        private static bool _loaded;

        private static readonly Dictionary<string, Snapshot> _baked = new();

        private static readonly List<GameObject> RootBuffer = new();

        static PlayModeValueSaver()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoEvent += OnUndoRedoEvent;
        }

        private readonly struct UndoStep
        {
            public readonly int Group;
            public readonly string Before;
            public readonly string After;

            public UndoStep(int group, string before, string after)
            {
                Group = group;
                Before = before;
                After = after;
            }
        }

        private static readonly List<UndoStep> _undoSteps = new();
        private static readonly List<UndoStep> _redoSteps = new();

        // A store change touches no Unity object, so each one records a change here to get an undo group.
        private sealed class UndoMarker : ScriptableObject
        {
            public int Step;
        }

        private static UndoMarker _marker;

        private static void PushUndoStep(int group, string before, string after)
        {
            if (before == after)
                return;

            if (_marker == null)
            {
                _marker = ScriptableObject.CreateInstance<UndoMarker>();
                _marker.hideFlags = HideFlags.HideAndDontSave;
            }
            Undo.RegisterCompleteObjectUndo(_marker, Undo.GetCurrentGroupName());
            _marker.Step++;
            Undo.CollapseUndoOperations(group);

            _redoSteps.Clear();
            _undoSteps.Add(new UndoStep(group, before, after));
        }

        private static void OnUndoRedoEvent(in UndoRedoInfo info)
        {
            var from = info.isRedo ? _redoSteps : _undoSteps;
            var to = info.isRedo ? _undoSteps : _redoSteps;
            for (int i = from.Count - 1; i >= 0; i--)
            {
                if (from[i].Group != info.undoGroup)
                    continue;
                WriteStore(info.isRedo ? from[i].After : from[i].Before);
                to.Add(from[i]);
                from.RemoveAt(i);
                return;
            }
        }

        private static void ForgetUndoSteps()
        {
            _undoSteps.Clear();
            _redoSteps.Clear();
        }

        private sealed class SaveTally
        {
            public int Components;
            public int AddedComponents;
            public int Objects;
            public int Values;
            public int UnsavableReferences;
            public int Unsupported;
            public int LargeArrays;
            public int UnsavedCreated;
            public readonly List<GameObject> CreatedToSave = new();
            public readonly List<Component> Baked = new();
        }

        internal static void SaveComponents(UnityEngine.Object[] targets)
        {
            string storeBefore = ReadStore();
            var set = Parse(storeBefore) ?? new RecordSet();
            var tally = new SaveTally();
            var seen = new HashSet<string>();
            var companions = new List<Component>();

            for (int t = 0; t < targets.Length; t++)
            {
                var component = targets[t] as Component;
                if (component == null)
                    continue;

                CaptureComponent(set, component, tally, seen);
                companions.Clear();
                CompanionsOf(component, companions);
                for (int i = 0; i < companions.Count; i++)
                    CaptureComponent(set, companions[i], tally, seen);
            }

            Commit(set, storeBefore, "Save component values", tally);
        }

        internal static void SaveObjects(IList<GameObject> gameObjects, SaveScope scope)
        {
            string storeBefore = ReadStore();
            var set = Parse(storeBefore) ?? new RecordSet();
            var tally = new SaveTally();
            var seen = new HashSet<string>();

            for (int i = 0; i < gameObjects.Count; i++)
            {
                if (IsSceneObject(gameObjects[i]))
                    CaptureObject(set, gameObjects[i], scope, tally, seen);
            }

            Commit(set, storeBefore, "Save object", tally);
        }

        internal static void SaveProperty(UnityEngine.Object[] targets, string propertyPath)
        {
            string storeBefore = ReadStore();
            var set = Parse(storeBefore) ?? new RecordSet();
            var tally = new SaveTally();
            var seen = new HashSet<string>();

            int element = propertyPath.IndexOf(".Array.data[", StringComparison.Ordinal);
            string path = element >= 0 ? propertyPath.Substring(0, element) : propertyPath;

            for (int t = 0; t < targets.Length; t++)
            {
                var component = targets[t] as Component;
                if (component == null || !IsSceneObject(component.gameObject))
                    continue;

                if (IsCreatedDuringPlay(component.gameObject) || IsAddedDuringPlay(component))
                {
                    CaptureComponent(set, component, tally, seen);
                    continue;
                }

                var paths = new List<string>();
                var values = new List<string>();
                var stats = new PlayModeValueCodec.WalkStats();
                using (var so = new SerializedObject(component))
                {
                    var property = so.FindProperty(path);
                    if (property == null)
                        continue;
                    foreach (var leaf in PlayModeValueCodec.LeavesUnder(property, stats))
                        AppendLeaf(leaf, paths, values, tally);
                }
                tally.LargeArrays += stats.LargeArrays;
                if (paths.Count == 0)
                    continue;

                var record = Find(set.Records, component);
                if (record == null)
                {
                    record = NewRecord(component);
                    record.Partial = true;
                    set.Records.Add(record);
                }
                else
                {
                    RemoveSubtree(record, path);
                }
                record.PropertyPaths.AddRange(paths);
                record.Values.AddRange(values);
                tally.Components++;
                tally.Values += paths.Count;
            }

            Commit(set, storeBefore, "Keep value after Play", tally);
        }

        internal static void SaveSelection()
        {
            if (!EditorApplication.isPlaying || !PlayModeKeeperSettings.Enabled)
                return;

            var selected = Selection.gameObjects;
            if (selected.Length > 0)
                SaveObjects(selected, SaveScope.ObjectAndComponents);
        }

        internal static void Forget(RecordKind kind, int index)
        {
            string storeBefore = ReadStore();
            var set = Parse(storeBefore);
            if (set == null)
                return;

            switch (kind)
            {
                case RecordKind.Values:
                    if (index < set.Records.Count)
                        set.Records.RemoveAt(index);
                    break;
                case RecordKind.CreatedObject:
                    if (index < set.Added.Count)
                        set.Added.RemoveAt(index);
                    break;
                case RecordKind.AddedComponent:
                    if (index < set.AddedComponents.Count)
                        set.AddedComponents.RemoveAt(index);
                    break;
            }

            Store(set);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Forget saved values");
            PushUndoStep(Undo.GetCurrentGroup(), storeBefore, ReadStore());
        }

        private static void CaptureObject(RecordSet set, GameObject go, SaveScope scope, SaveTally tally,
                                          HashSet<string> seen)
        {
            if (IsCreatedDuringPlay(go))
            {
                RequestSaveCreated(set, go, tally);
                return;
            }

            var record = Capture(go, tally);
            Merge(set.Records, record);
            tally.Objects++;
            tally.Values += record.Values.Count;

            if (scope == SaveScope.Object)
                return;

            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    CaptureComponent(set, components[i], tally, seen);
            }

            if (scope != SaveScope.Hierarchy)
                return;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                CaptureObject(set, t.GetChild(i).gameObject, scope, tally, seen);
        }

        private static void CaptureComponent(RecordSet set, Component component, SaveTally tally,
                                             HashSet<string> seen)
        {
            if (component == null || !seen.Add(PlayModeObjectRef.Of(component)))
                return;

            var go = component.gameObject;
            if (!IsSceneObject(go))
                return;

            if (IsCreatedDuringPlay(go))
            {
                var root = FindSavedCreatedRoot(set, go);
                if (root == null)
                {
                    tally.UnsavedCreated++;
                    return;
                }
                if (!tally.CreatedToSave.Contains(root))
                    tally.CreatedToSave.Add(root);
                tally.Baked.Add(component);
                return;
            }

            bool added = IsAddedDuringPlay(component);
            var record = Capture(component, tally);
            Merge(added ? set.AddedComponents : set.Records, record);
            if (added)
                tally.AddedComponents++;
            else
                tally.Components++;
            tally.Values += record.Values.Count;
        }

        private static void RequestSaveCreated(RecordSet set, GameObject go, SaveTally tally)
        {
            var target = FindSavedCreatedRoot(set, go);
            if (target == null)
            {
                var parent = go.transform.parent;
                if (parent != null && IsCreatedDuringPlay(parent.gameObject))
                {
                    tally.UnsavedCreated++;
                    return;
                }
                target = go;
            }
            if (!tally.CreatedToSave.Contains(target))
                tally.CreatedToSave.Add(target);
        }

        private static void Commit(RecordSet set, string storeBefore, string undoName, SaveTally tally)
        {
            var notes = new StringBuilder();
            int savedCreated = 0;
            for (int i = 0; i < tally.CreatedToSave.Count; i++)
            {
                if (SaveCreatedObject(set, tally.CreatedToSave[i], notes))
                    savedCreated++;
                notes.Append(' ');
            }
            for (int i = 0; i < tally.Baked.Count; i++)
            {
                if (tally.Baked[i] != null)
                    _baked[PlayModeObjectRef.Of(tally.Baked[i])] = Read(tally.Baked[i]);
            }

            Store(set);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            PushUndoStep(Undo.GetCurrentGroup(), storeBefore, ReadStore());

            var message = new StringBuilder();
            int targets = tally.Components + tally.AddedComponents + tally.Objects;
            if (targets > 0)
            {
                message.Append($"Saved {tally.Values} value(s) across {tally.Components + tally.AddedComponents} " +
                               $"component(s) and {tally.Objects} object(s). ");
            }
            if (tally.AddedComponents > 0)
                message.Append($"{tally.AddedComponents} component(s) will be added back to their object. ");
            if (savedCreated > 0)
                message.Append(notes.ToString().Trim()).Append(' ');
            if (tally.UnsavableReferences > 0)
            {
                message.Append($"{tally.UnsavableReferences} reference(s) to things that exist only during " +
                               "Play were not captured. ");
            }
            if (tally.Unsupported > 0)
                message.Append($"{tally.Unsupported} field(s) of a type this cannot encode were skipped. ");
            if (tally.LargeArrays > 0)
            {
                message.Append($"{tally.LargeArrays} array(s) longer than {PlayModeKeeperSettings.MaxArraySize} " +
                               "were skipped (see Preferences > PlayMode Keeper). ");
            }

            if (tally.UnsavedCreated > 0)
            {
                PlayModeKeeperLog.Warning(message +
                    $"{tally.UnsavedCreated} item(s) belong to objects created during Play and were not saved: " +
                    "save the topmost object created during Play first.");
                return;
            }
            if (message.Length == 0)
                return;
            message.Append("They will be applied when Play stops.");
            PlayModeKeeperLog.Info(message.ToString());
        }

        private static int IndexOf(List<Record> records, string ownerId, string componentId, string componentType)
        {
            return records.FindIndex(r => r.InstanceId == ownerId && r.ComponentId == componentId &&
                                          r.ComponentType == componentType);
        }

        private static void Merge(List<Record> records, Record record)
        {
            int index = IndexOf(records, record.InstanceId, record.ComponentId, record.ComponentType);
            if (index >= 0)
                records[index] = record;
            else
                records.Add(record);
        }

        private static Record Find(List<Record> records, UnityEngine.Object target)
        {
            var component = target as Component;
            var go = component != null ? component.gameObject : target as GameObject;
            if (go == null)
                return null;

            int index = IndexOf(records, PlayModeObjectRef.Of(go),
                                component != null ? PlayModeObjectRef.Of(component) : "",
                                component != null ? component.GetType().AssemblyQualifiedName : "");
            return index >= 0 ? records[index] : null;
        }

        private static void RemoveSubtree(Record record, string path)
        {
            string prefix = path + ".";
            for (int i = record.PropertyPaths.Count - 1; i >= 0; i--)
            {
                string existing = record.PropertyPaths[i];
                if (existing != path && !existing.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                record.PropertyPaths.RemoveAt(i);
                record.Values.RemoveAt(i);
            }
        }

        private static Record NewRecord(UnityEngine.Object target)
        {
            var component = target as Component;
            var go = component != null ? component.gameObject : (GameObject)target;
            return new Record
            {
                Session = CurrentSession(),
                ScenePath = go.scene.path,
                SiblingPath = SiblingPath(go.transform),
                NamePath = NamePath(go.transform),
                InstanceId = PlayModeObjectRef.Of(go),
                ComponentType = component != null ? component.GetType().AssemblyQualifiedName : "",
                ComponentIndex = component != null ? PlayModeValueCodec.IndexAmongSameType(component) : 0,
                ComponentId = component != null ? PlayModeObjectRef.Of(component) : "",
            };
        }

        private static Record Capture(UnityEngine.Object target, SaveTally tally)
        {
            var record = NewRecord(target);
            ReadValues(target, record.PropertyPaths, record.Values, tally);
            return record;
        }

        private static void ReadValues(UnityEngine.Object target, List<string> paths, List<string> values,
                                       SaveTally counts)
        {
            if (target is GameObject go)
            {
                using (var so = new SerializedObject(go))
                {
                    for (int i = 0; i < GameObjectProperties.Length; i++)
                    {
                        var property = so.FindProperty(GameObjectProperties[i]);
                        if (property != null)
                            AppendLeaf(property, paths, values, counts);
                    }
                }
                AppendPlacement(go.transform, paths, values);
                return;
            }

            var stats = new PlayModeValueCodec.WalkStats();
            using (var so = new SerializedObject(target))
            {
                foreach (var leaf in PlayModeValueCodec.Leaves(so, stats))
                    AppendLeaf(leaf, paths, values, counts);
            }
            counts.LargeArrays += stats.LargeArrays;
        }

        private static void AppendLeaf(SerializedProperty leaf, List<string> paths, List<string> values,
                                       SaveTally counts)
        {
            if (!PlayModeValueCodec.TryEncode(leaf, out string encoded))
            {
                counts.Unsupported++;
                return;
            }
            if (encoded == PlayModeValueCodec.UnsavableReference)
                counts.UnsavableReferences++;
            paths.Add(leaf.propertyPath);
            values.Add(encoded);
        }

        private static void AppendPlacement(Transform t, List<string> paths, List<string> values)
        {
            // The scene Unity moves DontDestroyOnLoad objects into.
            var scene = t.gameObject.scene;
            if (scene.buildIndex == -1 && scene.name == "DontDestroyOnLoad" &&
                !scene.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return;

            paths.Add(ParentKey);
            values.Add(t.parent != null ? PlayModeObjectRef.Of(t.parent.gameObject) : "");
            paths.Add(PreviousKey);
            values.Add(NeighbourId(t, -1));
            paths.Add(NextKey);
            values.Add(NeighbourId(t, 1));
        }

        private static string NeighbourId(Transform t, int direction)
        {
            var prePlayIds = PlayModeSnapshot.ObjectIds;
            var parent = t.parent;
            int index = t.GetSiblingIndex();
            int count;
            if (parent != null)
            {
                count = parent.childCount;
            }
            else
            {
                var scene = t.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                    return "";
                scene.GetRootGameObjects(RootBuffer);
                count = RootBuffer.Count;
            }

            for (int i = index + direction; i >= 0 && i < count; i += direction)
            {
                var sibling = parent != null ? parent.GetChild(i).gameObject : RootBuffer[i];
                string id = PlayModeObjectRef.Of(sibling);
                if (prePlayIds == null || prePlayIds.Contains(id))
                    return id;
            }
            return "";
        }

        private static readonly Dictionary<Type, Type[]> _companionTargets = new();
        private static readonly List<Component> SiblingBuffer = new();

        internal static void CompanionsOf(Component component, List<Component> companions)
        {
            var type = component.GetType();
            component.gameObject.GetComponents(SiblingBuffer);
            for (int i = 0; i < SiblingBuffer.Count; i++)
            {
                var sibling = SiblingBuffer[i];
                if (sibling == null || sibling == component)
                    continue;

                var targets = CompanionTargets(sibling.GetType());
                for (int t = 0; t < targets.Length; t++)
                {
                    if (!targets[t].IsAssignableFrom(type))
                        continue;
                    companions.Add(sibling);
                    break;
                }
            }
        }

        private static Type[] CompanionTargets(Type type)
        {
            if (_companionTargets.TryGetValue(type, out var targets))
                return targets;

            targets = Type.EmptyTypes;
            // The URP/HDRP camera and light additional data; matched by name to avoid a render pipeline reference.
            if (type.GetInterface("UnityEngine.Rendering.IAdditionalData") != null)
            {
                var found = new List<Type>();
                var attributes = type.GetCustomAttributes(typeof(RequireComponent), true);
                for (int i = 0; i < attributes.Length; i++)
                {
                    var require = (RequireComponent)attributes[i];
                    if (require.m_Type0 != null) found.Add(require.m_Type0);
                    if (require.m_Type1 != null) found.Add(require.m_Type1);
                    if (require.m_Type2 != null) found.Add(require.m_Type2);
                }
                targets = found.ToArray();
            }
            _companionTargets[type] = targets;
            return targets;
        }

        private static bool SaveCreatedObject(RecordSet set, GameObject go, StringBuilder notes)
        {
            var savedRoot = FindSavedCreatedRoot(set, go);
            string reBakedNote = "";
            if (savedRoot != null && savedRoot != go)
            {
                reBakedNote = $"'{savedRoot.name}' was saved again with the latest state of everything under it. ";
                go = savedRoot;
            }

            if (!AssetDatabase.IsValidFolder(SavedObjectFolder))
                AssetDatabase.CreateFolder("Assets", SavedObjectFolderName);

            string rootId = PlayModeObjectRef.Of(go);
            string fileName = string.Join("_", go.name.Split(Path.GetInvalidFileNameChars()));
            string prefabPath = $"{SavedObjectFolder}/{fileName}_{rootId}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool saved);
            if (!saved)
            {
                notes.Append($"Could not save a copy of '{go.name}' to {prefabPath}. " +
                             "It will not be recreated when Play stops.");
                return false;
            }

            int discarded = DiscardOverlapping(set, go, rootId, prefabPath);

            var parent = go.transform.parent;
            var record = new AddedRecord
            {
                Session = CurrentSession(),
                ScenePath = go.scene.path,
                ParentSiblingPath = parent != null ? SiblingPath(parent) : "",
                ParentNamePath = parent != null ? NamePath(parent) : "",
                ParentInstanceId = parent != null ? PlayModeObjectRef.Of(parent.gameObject) : "",
                SiblingIndex = go.transform.GetSiblingIndex(),
                Name = go.name,
                PrefabPath = prefabPath,
                RootInstanceId = rootId,
            };
            var subtree = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < subtree.Length; i++)
                record.SubtreeIds.Add(PlayModeObjectRef.Of(subtree[i].gameObject));

            int doubtful = CollectRelinks(go, set, record.Relinks);
            set.Added.Add(record);

            string discardedNote = discarded > 0
                ? $"{discarded} record(s) for objects inside it were dropped. "
                : "";
            string doubtfulNote = doubtful > 0
                ? $"{doubtful} of them point at objects created during Play, which come back only if those are saved too. "
                : "";
            notes.Append($"'{go.name}' will be recreated when Play stops, with " +
                         $"{record.Relinks.Count} reference(s) to reconnect. " +
                         reBakedNote + discardedNote + doubtfulNote);
            return true;
        }

        private static GameObject FindSavedCreatedRoot(RecordSet set, GameObject go)
        {
            for (int i = 0; i < set.Added.Count; i++)
            {
                var root = PlayModeObjectRef.ToGameObject(set.Added[i].RootInstanceId);
                if (root != null && go.transform.IsChildOf(root.transform))
                    return root;
            }
            return null;
        }

        private static int DiscardOverlapping(RecordSet set, GameObject go, string rootId, string newPrefabPath)
        {
            int discarded = 0;
            for (int i = set.Added.Count - 1; i >= 0; i--)
            {
                var existing = set.Added[i];
                if (existing.RootInstanceId != rootId)
                {
                    var other = PlayModeObjectRef.ToGameObject(existing.RootInstanceId);
                    if (other == null || !other.transform.IsChildOf(go.transform))
                        continue;
                    discarded++;
                }

                if (existing.PrefabPath != newPrefabPath)
                    AssetDatabase.DeleteAsset(existing.PrefabPath);
                set.Added.RemoveAt(i);
            }
            return discarded;
        }

        private static int CollectRelinks(GameObject root, RecordSet set, List<RelinkRecord> relinks)
        {
            var prePlayIds = PlayModeSnapshot.ObjectIds;
            int doubtful = 0;
            var stats = new PlayModeValueCodec.WalkStats();
            var components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                using (var so = new SerializedObject(component))
                {
                    foreach (var leaf in PlayModeValueCodec.Leaves(so, stats))
                    {
                        if (leaf.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        var value = leaf.objectReferenceValue;
                        if (value == null)
                            continue;

                        var targetComponent = value as Component;
                        var targetGo = targetComponent != null ? targetComponent.gameObject : value as GameObject;
                        if (targetGo == null || EditorUtility.IsPersistent(targetGo) ||
                            targetGo.transform.IsChildOf(root.transform))
                            continue;
                        if (prePlayIds != null && !prePlayIds.Contains(PlayModeObjectRef.Of(targetGo)) &&
                            FindSavedCreatedRoot(set, targetGo) == null)
                            doubtful++;

                        relinks.Add(new RelinkRecord
                        {
                            LocalSiblingPath = SiblingPath(component.transform, root.transform),
                            ComponentType = component.GetType().AssemblyQualifiedName,
                            ComponentIndex = PlayModeValueCodec.IndexAmongSameType(component),
                            PropertyPath = leaf.propertyPath,
                            Target = PlayModeValueCodec.EncodeReference(value),
                            TargetNamePath = NamePath(targetGo.transform),
                        });
                    }
                }
            }
            return doubtful;
        }

        [MenuItem(SaveSelectedMenu, false, 50)]
        private static void SaveSelectedMenuItem() => SaveSelection();

        [MenuItem(SaveSelectedMenu, true)]
        private static bool SaveSelectedValidate()
        {
            return EditorApplication.isPlaying && PlayModeKeeperSettings.Enabled &&
                   Selection.gameObjects.Length > 0;
        }

        [MenuItem(RestoreMenu, false, 101)]
        private static void RestoreMenuItem() => Restore();

        [MenuItem(RestoreMenu, true)]
        private static bool RestoreValidate() => !Application.isPlaying && CachedSet() != null;

        [MenuItem(ClearMenu, false, 102)]
        internal static void ClearSaved()
        {
            bool had = CachedSet() != null;
            ClearStore();
            SweepSavedObjects(null);
            _baked.Clear();
            if (had)
                PlayModeKeeperLog.Info("Cleared saved values.");
        }

        [MenuItem(ClearMenu, true)]
        private static bool ClearValidate() => CachedSet() != null;

        [MenuItem(PreferencesMenu, false, 200)]
        private static void OpenPreferences() => SettingsService.OpenUserPreferences("Preferences/PlayMode Keeper");

        private static void ClearStore() => WriteStore("");

        private static void SweepSavedObjects(RecordSet remaining)
        {
            if (!AssetDatabase.IsValidFolder(SavedObjectFolder))
                return;

            var needed = new HashSet<string>();
            if (remaining != null)
            {
                for (int i = 0; i < remaining.Added.Count; i++)
                    needed.Add(remaining.Added[i].PrefabPath);
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SavedObjectFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!needed.Contains(path))
                    AssetDatabase.DeleteAsset(path);
            }

            if (Directory.GetFileSystemEntries(SavedObjectFolder).Length == 0)
                AssetDatabase.DeleteAsset(SavedObjectFolder);
        }

        internal static Snapshot Read(UnityEngine.Object target)
        {
            var snapshot = new Snapshot();
            ReadValues(target, snapshot.Paths, snapshot.Values, new SaveTally());
            return snapshot;
        }

        internal static bool TryGetSaved(UnityEngine.Object target, out List<string> paths,
                                         out List<string> values, out bool partial)
        {
            var set = CachedSet();
            var record = set != null ? Find(set.Records, target) ?? Find(set.AddedComponents, target) : null;
            if (record != null)
            {
                paths = record.PropertyPaths;
                values = record.Values;
                partial = record.Partial;
                return true;
            }

            partial = false;
            if (_baked.TryGetValue(PlayModeObjectRef.Of(target), out var baked))
            {
                paths = baked.Paths;
                values = baked.Values;
                return true;
            }
            paths = null;
            values = null;
            return false;
        }

        internal static RecordSet Peek() => CachedSet();

        internal static void CollectSavedTargets(List<UnityEngine.Object> saved, List<GameObject> savedCreatedRoots)
        {
            var set = CachedSet();
            if (set == null)
                return;
            string session = CurrentSession();

            for (int i = 0; i < set.Records.Count; i++)
            {
                var record = set.Records[i];
                if (record.Session != session)
                    continue;
                var target = record.IsGameObject
                    ? PlayModeObjectRef.ToObject(record.InstanceId)
                    : PlayModeObjectRef.ToObject(record.ComponentId);
                if (target != null)
                    saved.Add(target);
            }
            for (int i = 0; i < set.AddedComponents.Count; i++)
            {
                var record = set.AddedComponents[i];
                if (record.Session != session)
                    continue;
                var target = PlayModeObjectRef.ToObject(record.ComponentId);
                if (target != null)
                    saved.Add(target);
            }
            for (int i = 0; i < set.Added.Count; i++)
            {
                var record = set.Added[i];
                if (record.Session != session)
                    continue;
                var root = PlayModeObjectRef.ToGameObject(record.RootInstanceId);
                if (root != null)
                    savedCreatedRoots.Add(root);
            }
        }

        internal static bool IsSavedCreatedObject(GameObject go)
        {
            var set = CachedSet();
            return set != null && go != null && FindSavedCreatedRoot(set, go) != null;
        }

        private static RecordSet CachedSet()
        {
            if (!_loaded)
            {
                _set = Load();
                _loaded = true;
            }
            return _set;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;
            ForgetUndoSteps();
            _baked.Clear();
            if (CachedSet() == null)
                SweepSavedObjects(null);
            else
                Restore();
        }

        private sealed class PendingRelink
        {
            public Transform Root;
            public RelinkRecord Link;
            public string Name;
            public bool IdsValid;
        }

        internal static void Restore()
        {
            var set = CachedSet();
            if (set == null)
                return;

            string session = CurrentSession();
            var context = new PlayModeValueCodec.Context();
            var problems = context.Problems;
            var dirtyScenes = new HashSet<Scene>();
            var unresolved = new RecordSet();
            int restoredObjects = 0;
            int addedComponents = 0;
            int writtenTargets = 0;
            int writtenValues = 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(RestoreUndoName);
            int undoGroup = Undo.GetCurrentGroup();

            var recreatedRoots = new List<GameObject>();
            var pendingRelinks = new List<PendingRelink>();
            set.Added.Sort(CompareAddedOrder);
            for (int i = 0; i < set.Added.Count; i++)
            {
                var record = set.Added[i];
                context.IdsValid = record.Session == session;
                if (TryRecreate(record, context, recreatedRoots, dirtyScenes, pendingRelinks))
                    restoredObjects++;
                else
                    unresolved.Added.Add(record);
            }

            var restoredComponentIds = new HashSet<string>();
            for (int i = 0; i < recreatedRoots.Count; i++)
            {
                var all = recreatedRoots[i].GetComponentsInChildren<Component>(true);
                for (int c = 0; c < all.Length; c++)
                {
                    if (all[c] != null)
                        restoredComponentIds.Add(PlayModeObjectRef.Of(all[c]));
                }
            }

            set.AddedComponents.Sort(CompareAddedComponentOrder);
            for (int i = 0; i < set.AddedComponents.Count; i++)
            {
                var record = set.AddedComponents[i];
                context.IdsValid = record.Session == session;
                if (TryAddComponent(record, context, restoredComponentIds, dirtyScenes, ref writtenValues))
                    addedComponents++;
                else
                    unresolved.AddedComponents.Add(record);
            }

            RetryRelinks(pendingRelinks, context);
            int failedRelinks = pendingRelinks.Count;

            var targets = new UnityEngine.Object[set.Records.Count];
            for (int i = 0; i < set.Records.Count; i++)
            {
                context.IdsValid = set.Records[i].Session == session;
                targets[i] = ResolveTarget(set.Records[i], context);
                if (targets[i] == null)
                    unresolved.Records.Add(set.Records[i]);
            }

            int moved = ApplyPlacements(set.Records, targets, session, context, dirtyScenes);

            for (int i = 0; i < set.Records.Count; i++)
            {
                if (targets[i] == null)
                    continue;
                context.IdsValid = set.Records[i].Session == session;
                int written = ApplyValues(targets[i], set.Records[i], context);
                if (written == 0)
                    continue;
                writtenTargets++;
                writtenValues += written;
                dirtyScenes.Add(SceneOf(targets[i]));
            }

            foreach (var scene in dirtyScenes)
            {
                if (scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(scene);
            }
            Undo.CollapseUndoOperations(undoGroup);

            var summary = new StringBuilder();
            summary.Append($"Restored {writtenValues} value(s) on {writtenTargets} of " +
                           $"{set.Records.Count - unresolved.Records.Count} saved component(s) and object(s); " +
                           "the rest already matched.");
            if (restoredObjects > 0)
                summary.Append($" Recreated {restoredObjects} object(s).");
            if (addedComponents > 0)
                summary.Append($" Added {addedComponents} component(s).");
            if (moved > 0)
                summary.Append($" Moved {moved} object(s) in the hierarchy.");
            summary.Append(" Undo reverts the whole restore.");

            int leftover = unresolved.Added.Count + unresolved.Records.Count + unresolved.AddedComponents.Count;
            SweepSavedObjects(unresolved);

            if (leftover == 0)
            {
                ClearStore();
                int failed = failedRelinks + context.FailedReferences;
                if (failed > 0)
                {
                    PlayModeKeeperLog.Warning($"{summary} {failed} reference(s) could not be " +
                                              $"reconnected.\n{problems}");
                }
                else if (problems.Length > 0)
                {
                    PlayModeKeeperLog.Warning($"{summary}\n{problems}");
                }
                else
                {
                    PlayModeKeeperLog.Info(summary.ToString());
                }
            }
            else
            {
                Store(unresolved);
                PlayModeKeeperLog.Warning($"{summary} {leftover} record(s) could not be resolved and " +
                                          "remain. Open the scene they belong to and use " +
                                          $"{RestoreMenu}, or {ClearMenu} to drop them.\n{problems}");
            }

            Restored?.Invoke();
        }

        private static int ApplyValues(UnityEngine.Object target, Record record, PlayModeValueCodec.Context context)
        {
            context.Owner = record.IsGameObject
                ? record.NamePath
                : $"{record.NamePath} ({TypeName(record.ComponentType)})";
            return ApplyValues(target, record.PropertyPaths, record.Values, context, true);
        }

        private static int ApplyValues(UnityEngine.Object target, List<string> paths, List<string> values,
                                       PlayModeValueCodec.Context context, bool withUndo)
        {
            context.ArraySizesBefore.Clear();
            int written = 0;
            using (var so = new SerializedObject(target))
            {
                for (int p = 0; p < paths.Count; p++)
                {
                    string path = paths[p];
                    if (path.Length > 0 && path[0] == '@')
                        continue;
                    var property = so.FindProperty(path);
                    if (property == null)
                        continue;

                    string value = values[p];
                    if (PlayModeValueCodec.TryEncode(property, out string current) && current == value)
                        continue;
                    if (!PlayModeValueCodec.TryDecode(property, value, context))
                        continue;
                    written++;

                    // The fields of a newly assigned managed reference exist only after it is applied.
                    if (property.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        if (withUndo)
                            so.ApplyModifiedProperties();
                        else
                            so.ApplyModifiedPropertiesWithoutUndo();
                        so.Update();
                    }
                }

                if (written > 0)
                {
                    if (withUndo)
                        so.ApplyModifiedProperties();
                    else
                        so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(target);
                }
            }
            return written;
        }

        private enum PlacementResult
        {
            Unchanged,
            Moved,
            Deferred,
        }

        private static int ApplyPlacements(List<Record> records, UnityEngine.Object[] targets, string session,
                                           PlayModeValueCodec.Context context, HashSet<Scene> dirtyScenes)
        {
            var pending = new List<int>();
            for (int i = 0; i < records.Count; i++)
            {
                if (targets[i] is GameObject && records[i].PropertyPaths.Contains(ParentKey))
                    pending.Add(i);
            }

            int moved = 0;
            bool progress = true;
            while (pending.Count > 0 && progress)
            {
                progress = false;
                for (int n = 0; n < pending.Count; n++)
                {
                    int i = pending[n];
                    context.IdsValid = records[i].Session == session;
                    var result = ApplyPlacement((GameObject)targets[i], records[i], context, dirtyScenes);
                    if (result == PlacementResult.Deferred)
                        continue;
                    if (result == PlacementResult.Moved)
                        moved++;
                    pending.RemoveAt(n--);
                    progress = true;
                }
            }

            for (int n = 0; n < pending.Count; n++)
            {
                context.Problems.AppendLine($"  {records[pending[n]].NamePath}: it was saved under an object " +
                                            "that is itself saved under it; left where it is");
            }
            return moved;
        }

        private static PlacementResult ApplyPlacement(GameObject go, Record record,
                                                      PlayModeValueCodec.Context context,
                                                      HashSet<Scene> dirtyScenes)
        {
            // Moved to another scene during Play, such as DontDestroyOnLoad.
            if (go.scene.path != record.ScenePath)
                return PlacementResult.Unchanged;

            var t = go.transform;
            Transform parent = null;
            string parentId = ValueOf(record, ParentKey);
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentGo = context.Resolve(parentId);
                if (parentGo == null)
                {
                    if (context.IdsValid)
                    {
                        context.Problems.AppendLine($"  {record.NamePath}: the object it was under during " +
                                                    "Play does not exist outside it; left where it is");
                    }
                    return PlacementResult.Unchanged;
                }
                parent = parentGo.transform;
            }
            else if (!context.IdsValid)
            {
                return PlacementResult.Unchanged;
            }

            string previousId = ValueOf(record, PreviousKey);
            string nextId = ValueOf(record, NextKey);

            bool needsParent = t.parent != parent;
            if (needsParent && parent != null && parent.IsChildOf(t))
                return PlacementResult.Deferred;

            if (needsParent && !CanMove(go))
            {
                context.Problems.AppendLine($"  {record.NamePath}: it is part of a prefab instance and " +
                                            "cannot be moved; unpack the prefab to move it");
                return PlacementResult.Unchanged;
            }

            var sceneBefore = go.scene;
            bool moved = false;
            if (needsParent)
            {
                Undo.SetTransformParent(t, parent, RestoreUndoName);
                if (t.parent != parent)
                {
                    context.Problems.AppendLine($"  {record.NamePath}: could not be moved under its saved parent");
                    return PlacementResult.Unchanged;
                }
                moved = true;
            }

            if (previousId != null && nextId != null)
            {
                var previous = SiblingFor(previousId, t, context);
                var next = SiblingFor(nextId, t, context);
                bool previousKnown = previousId.Length == 0 || previous != null;
                bool nextKnown = nextId.Length == 0 || next != null;
                int current = t.GetSiblingIndex();
                bool adjacent = (previousKnown && Neighbour(t, -1, context) == previous) ||
                                (nextKnown && Neighbour(t, 1, context) == next);
                bool between = (previous != null || next != null) &&
                               (previous == null || previous.GetSiblingIndex() < current) &&
                               (next == null || next.GetSiblingIndex() > current);
                if (!adjacent && !between && CanMove(go))
                {
                    int index = -1;
                    if (previous != null)
                        index = current < previous.GetSiblingIndex() ? previous.GetSiblingIndex() : previous.GetSiblingIndex() + 1;
                    else if (previousId.Length == 0)
                        index = 0;
                    else if (next != null)
                        index = current < next.GetSiblingIndex() ? next.GetSiblingIndex() - 1 : next.GetSiblingIndex();
                    else if (nextId.Length == 0)
                        index = SiblingCount(t) - 1;

                    if (index >= 0 && index != current)
                    {
                        Undo.SetSiblingIndex(t, index, RestoreUndoName);
                        moved = true;
                    }
                }
            }

            if (!moved)
                return PlacementResult.Unchanged;
            dirtyScenes.Add(sceneBefore);
            dirtyScenes.Add(go.scene);
            return PlacementResult.Moved;
        }

        private static bool CanMove(GameObject go)
        {
            return !PrefabUtility.IsPartOfPrefabInstance(go) ||
                   PrefabUtility.IsOutermostPrefabInstanceRoot(go) ||
                   PrefabUtility.IsAddedGameObjectOverride(go);
        }

        private static Transform SiblingFor(string id, Transform t, PlayModeValueCodec.Context context)
        {
            var go = context.Resolve(id);
            if (go == null)
                return null;
            var sibling = go.transform;
            if (sibling == t || sibling.parent != t.parent)
                return null;
            if (t.parent == null && go.scene != t.gameObject.scene)
                return null;
            return sibling;
        }

        private static Transform Neighbour(Transform t, int direction, PlayModeValueCodec.Context context)
        {
            var parent = t.parent;
            int count;
            if (parent != null)
            {
                count = parent.childCount;
            }
            else
            {
                t.gameObject.scene.GetRootGameObjects(RootBuffer);
                count = RootBuffer.Count;
            }

            for (int i = t.GetSiblingIndex() + direction; i >= 0 && i < count; i += direction)
            {
                var sibling = parent != null ? parent.GetChild(i).gameObject : RootBuffer[i];
                if (!context.RecreatedObjects.Contains(sibling))
                    return sibling.transform;
            }
            return null;
        }

        private static int SiblingCount(Transform t)
        {
            return t.parent != null ? t.parent.childCount : t.gameObject.scene.rootCount;
        }

        private static string ValueOf(Record record, string path)
        {
            int index = record.PropertyPaths.IndexOf(path);
            return index >= 0 ? record.Values[index] : null;
        }

        private static Scene SceneOf(UnityEngine.Object target)
        {
            return target is Component component ? component.gameObject.scene : ((GameObject)target).scene;
        }

        private static bool TryRecreate(AddedRecord record, PlayModeValueCodec.Context context,
                                        List<GameObject> recreatedRoots, HashSet<Scene> dirtyScenes,
                                        List<PendingRelink> pendingRelinks)
        {
            var problems = context.Problems;
            Transform parent = null;
            if (record.ParentSiblingPath.Length > 0)
            {
                var parentGo = context.Resolve(record.ParentInstanceId);
                if (parentGo == null)
                {
                    parentGo = FindBySiblingPath(record.ScenePath, record.ParentSiblingPath);
                    if (parentGo != null && NamePath(parentGo.transform) != record.ParentNamePath)
                        parentGo = null;
                }
                if (parentGo == null)
                {
                    problems.AppendLine($"  {record.Name}: its parent '{record.ParentNamePath}' is not " +
                                        "where it was; nothing to attach it to");
                    return false;
                }
                parent = parentGo.transform;
            }

            var scene = parent != null ? parent.gameObject.scene : FindLoadedScene(record.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                problems.AppendLine($"  {record.Name}: its scene '{record.ScenePath}' is not open");
                return false;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.PrefabPath);
            if (prefab == null)
            {
                problems.AppendLine($"  {record.Name}: the saved copy '{record.PrefabPath}' is gone");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.name = record.Name;
            if (parent != null)
                instance.transform.SetParent(parent, false);
            int siblingCount = parent != null ? parent.childCount : scene.rootCount;
            instance.transform.SetSiblingIndex(Mathf.Clamp(record.SiblingIndex, 0, siblingCount - 1));

            var subtree = instance.GetComponentsInChildren<Transform>(true);
            bool sameShape = record.SubtreeIds != null && record.SubtreeIds.Count == subtree.Length;
            for (int i = 0; i < subtree.Length; i++)
            {
                context.RecreatedObjects.Add(subtree[i].gameObject);
                if (sameShape)
                    context.Recreated[record.SubtreeIds[i]] = subtree[i].gameObject;
            }
            if (!sameShape)
                context.Recreated[record.RootInstanceId] = instance;

            for (int i = 0; i < record.Relinks.Count; i++)
            {
                if (!TryRelink(instance.transform, record.Relinks[i], context, null, record.Name))
                {
                    pendingRelinks.Add(new PendingRelink
                    {
                        Root = instance.transform,
                        Link = record.Relinks[i],
                        Name = record.Name,
                        IdsValid = context.IdsValid,
                    });
                }
            }

            Undo.RegisterCreatedObjectUndo(instance, RestoreUndoName);
            recreatedRoots.Add(instance);
            dirtyScenes.Add(scene);
            AssetDatabase.DeleteAsset(record.PrefabPath);
            return true;
        }

        private static bool TryRelink(Transform root, RelinkRecord link, PlayModeValueCodec.Context context,
                                      StringBuilder problems, string name)
        {
            var owner = FindLocal(root, link.LocalSiblingPath);
            if (owner == null)
            {
                problems?.AppendLine($"  {name}: the recreated object has nothing at " +
                                     $"'{link.LocalSiblingPath}' to reconnect");
                return false;
            }

            var componentType = Type.GetType(link.ComponentType);
            if (componentType == null)
            {
                problems?.AppendLine($"  {name}: component type '{link.ComponentType}' no longer exists");
                return false;
            }

            var component = ComponentAt(owner.gameObject, componentType, link.ComponentIndex);
            if (component == null)
            {
                problems?.AppendLine($"  {name}: no {componentType.Name} at index {link.ComponentIndex} " +
                                     "on the recreated object");
                return false;
            }

            var value = PlayModeValueCodec.ResolveSceneReference(link.Target, context);
            if (value == null)
            {
                problems?.AppendLine($"  {name}: '{link.TargetNamePath}', referenced by " +
                                     $"{link.PropertyPath}, was not found");
                return false;
            }

            using (var so = new SerializedObject(component))
            {
                var prop = so.FindProperty(link.PropertyPath);
                if (prop == null)
                {
                    problems?.AppendLine($"  {name}: {componentType.Name} has no '{link.PropertyPath}' any more");
                    return false;
                }

                prop.objectReferenceValue = value;
                // Reverted together with the object's creation.
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return true;
        }

        private static void RetryRelinks(List<PendingRelink> pending, PlayModeValueCodec.Context context)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var link = pending[i];
                if (link.Root == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }
                context.IdsValid = link.IdsValid;
                if (TryRelink(link.Root, link.Link, context, context.Problems, link.Name))
                    pending.RemoveAt(i);
            }
        }

        private static UnityEngine.Object ResolveTarget(Record record, PlayModeValueCodec.Context context)
        {
            var problems = context.Problems;
            var go = ResolveGameObject(record.ScenePath, record.SiblingPath, record.NamePath,
                                       record.InstanceId, context);
            if (go == null)
                return null;
            if (record.IsGameObject)
                return go;

            var type = Type.GetType(record.ComponentType);
            if (type == null)
            {
                problems.AppendLine($"  {record.NamePath}: component type '{record.ComponentType}' no longer exists");
                return null;
            }

            var component = context.IdsValid ? ComponentById(go, record.ComponentId, type) : null;
            component ??= ComponentAt(go, type, record.ComponentIndex);
            if (component == null)
                problems.AppendLine($"  {record.NamePath}: no {type.Name} at index {record.ComponentIndex}");
            return component;
        }

        private static bool TryAddComponent(Record record, PlayModeValueCodec.Context context,
                                            HashSet<string> restoredComponentIds, HashSet<Scene> dirtyScenes,
                                            ref int writtenValues)
        {
            var problems = context.Problems;
            var type = Type.GetType(record.ComponentType);
            if (type == null)
            {
                problems.AppendLine($"  {record.NamePath}: component type '{record.ComponentType}' " +
                                    "no longer exists");
                return false;
            }

            var go = ResolveGameObject(record.ScenePath, record.SiblingPath, record.NamePath,
                                       record.InstanceId, context);
            if (go == null)
                return false;

            var component = ComponentAt(go, type, record.ComponentIndex);
            if (component != null && !restoredComponentIds.Contains(PlayModeObjectRef.Of(component)))
                component = null;
            if (component == null)
            {
                var before = new HashSet<string>();
                AddComponentIds(go, before);
                try
                {
                    component = Undo.AddComponent(go, type);
                }
                catch (Exception e)
                {
                    problems.AppendLine($"  {record.NamePath}: {type.Name} could not be added ({e.Message})");
                    return false;
                }
                if (component == null)
                {
                    problems.AppendLine($"  {record.NamePath}: {type.Name} could not be added");
                    return false;
                }
                // [RequireComponent] can add other components along with this one.
                var after = new HashSet<string>();
                AddComponentIds(go, after);
                foreach (var id in after)
                {
                    if (!before.Contains(id))
                        restoredComponentIds.Add(id);
                }
            }

            context.Owner = $"{record.NamePath} ({type.Name})";
            // Reverted together with the component's creation.
            writtenValues += ApplyValues(component, record.PropertyPaths, record.Values, context, false);
            EditorUtility.SetDirty(component);
            dirtyScenes.Add(go.scene);

            restoredComponentIds.Add(PlayModeObjectRef.Of(component));
            return true;
        }

        private static GameObject ResolveGameObject(string scenePath, string siblingPath, string namePath,
                                                    string instanceId, PlayModeValueCodec.Context context)
        {
            var go = context.Resolve(instanceId);
            if (go != null)
                return go;

            go = FindBySiblingPath(scenePath, siblingPath);
            if (go != null && NamePath(go.transform) != namePath)
            {
                context.Problems.AppendLine($"  {namePath}: '{NamePath(go.transform)}' occupies that " +
                                            "hierarchy position now; refusing to touch it");
                return null;
            }

            if (go == null)
            {
                context.Problems.AppendLine($"  {namePath}: object not found " +
                                            "(created during Play, or its scene is closed)");
            }
            return go;
        }

        private static void AddComponentIds(GameObject go, HashSet<string> ids)
        {
            var all = go.GetComponents<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    ids.Add(PlayModeObjectRef.Of(all[i]));
            }
        }

        internal static string TypeName(string assemblyQualifiedName)
        {
            var type = Type.GetType(assemblyQualifiedName);
            if (type != null)
                return type.Name;
            int comma = assemblyQualifiedName.IndexOf(',');
            return comma > 0 ? assemblyQualifiedName.Substring(0, comma) : assemblyQualifiedName;
        }

        private static int CompareAddedComponentOrder(Record a, Record b)
        {
            int owner = string.CompareOrdinal(a.InstanceId, b.InstanceId);
            if (owner != 0)
                return owner;
            int type = string.CompareOrdinal(a.ComponentType, b.ComponentType);
            if (type != 0)
                return type;
            return a.ComponentIndex.CompareTo(b.ComponentIndex);
        }

        private static int CompareAddedOrder(AddedRecord a, AddedRecord b)
        {
            int scene = string.CompareOrdinal(a.ScenePath, b.ScenePath);
            if (scene != 0)
                return scene;
            int parent = string.CompareOrdinal(a.ParentSiblingPath, b.ParentSiblingPath);
            if (parent != 0)
                return parent;
            return a.SiblingIndex.CompareTo(b.SiblingIndex);
        }

        internal static bool IsSceneObject(GameObject go)
        {
            return go != null && go.scene.IsValid() && !EditorUtility.IsPersistent(go) &&
                   !EditorSceneManager.IsPreviewSceneObject(go);
        }

        internal static bool IsCreatedDuringPlay(GameObject go)
        {
            var ids = PlayModeSnapshot.ObjectIds;
            return ids != null && !ids.Contains(PlayModeObjectRef.Of(go));
        }

        private static bool IsAddedDuringPlay(Component component)
        {
            // Adding a RectTransform to an object replaces its Transform under a new id.
            var ids = PlayModeSnapshot.ComponentIds;
            return ids != null && !ids.Contains(PlayModeObjectRef.Of(component)) && component is not Transform;
        }

        private static Component ComponentAt(GameObject go, Type type, int index)
        {
            var all = go.GetComponents<Component>();
            int seen = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].GetType() != type)
                    continue;
                if (seen == index)
                    return all[i];
                seen++;
            }
            return null;
        }

        private static Component ComponentById(GameObject go, string componentId, Type type)
        {
            if (string.IsNullOrEmpty(componentId))
                return null;
            var all = go.GetComponents<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].GetType() != type)
                    continue;
                if (PlayModeObjectRef.Of(all[i]) == componentId)
                    return all[i];
            }
            return null;
        }

        // Not SceneManager.GetSceneByPath, so that an unsaved scene (empty path) is found too.
        private static Scene FindLoadedScene(string scenePath)
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.path == (scenePath ?? ""))
                    return scene;
            }
            return default;
        }

        private static GameObject FindBySiblingPath(string scenePath, string siblingPath)
        {
            var scene = FindLoadedScene(scenePath);
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(siblingPath))
                return null;

            string[] parts = siblingPath.Split('/');
            var roots = scene.GetRootGameObjects();
            if (!int.TryParse(parts[0], out int rootIndex) || rootIndex < 0 || rootIndex >= roots.Length)
                return null;

            var t = roots[rootIndex].transform;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int childIndex) || childIndex < 0 || childIndex >= t.childCount)
                    return null;
                t = t.GetChild(childIndex);
            }
            return t.gameObject;
        }

        private static string SiblingPath(Transform t, Transform root = null)
        {
            var parts = new List<string>();
            while (t != root && t != null)
            {
                parts.Insert(0, t.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        private static Transform FindLocal(Transform root, string localSiblingPath)
        {
            if (localSiblingPath.Length == 0)
                return root;

            string[] parts = localSiblingPath.Split('/');
            var t = root;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 || index >= t.childCount)
                    return null;
                t = t.GetChild(index);
            }
            return t;
        }

        private static string NamePath(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        // SessionState is cleared when the editor quits, so the stamp changes with each editor session.
        private static string CurrentSession()
        {
            string stamp = SessionState.GetString(SessionStampKey, "");
            if (stamp.Length == 0)
            {
                stamp = Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionStampKey, stamp);
            }
            return stamp;
        }

        internal static void ExpireIdsForTests()
        {
            SessionState.SetString(SessionStampKey, Guid.NewGuid().ToString("N"));
        }

        private static bool IsEmpty(RecordSet set)
        {
            return set.Records.Count == 0 && set.Added.Count == 0 && set.AddedComponents.Count == 0;
        }

        private static void Store(RecordSet set) => WriteStore(IsEmpty(set) ? "" : JsonUtility.ToJson(set));

        private static string ReadStore() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";

        private static void WriteStore(string json)
        {
            if (!string.IsNullOrEmpty(json))
                File.WriteAllText(FilePath, json);
            else if (File.Exists(FilePath))
                File.Delete(FilePath);
            _loaded = false;
            StoreChanged?.Invoke();
        }

        internal static RecordSet Load() => Parse(ReadStore());

        private static RecordSet Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var set = JsonUtility.FromJson<RecordSet>(json);
            if (set == null)
                return null;
            set.Records ??= new List<Record>();
            set.Added ??= new List<AddedRecord>();
            set.AddedComponents ??= new List<Record>();
            return IsEmpty(set) ? null : set;
        }
    }
}
