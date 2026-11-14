using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeObjectRestoreTests
    {
        private const string SavedFolder = "Assets/PlayModeKeeperSaved";

        private GameObject _parent;
        private GameObject _referenced;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            _parent = new GameObject("PlayModeObjectRestoreTests_Parent");
            _referenced = new GameObject("PlayModeObjectRestoreTests_Referenced");
            _referenced.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeSnapshot.SetIdsForTests(null, null);
            PlayModeValueSaver.ClearSaved();
            if (_parent != null)
                Object.DestroyImmediate(_parent);
            if (_referenced != null)
                Object.DestroyImmediate(_referenced);
            if (AssetDatabase.IsValidFolder(SavedFolder))
                AssetDatabase.DeleteAsset(SavedFolder);
        }

        [Test]
        public void CreatedObject_ComesBackWithItsChildrenAndReferences()
        {
            PrePlay.Snapshot(_parent, _referenced);
            var created = new GameObject("PlayModeObjectRestoreTests_Created");
            created.transform.SetParent(_parent.transform);
            var child = new GameObject("PlayModeObjectRestoreTests_Child");
            child.transform.SetParent(created.transform);

            var camera = _referenced.GetComponent<Camera>();
            var canvas = created.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;

            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Hierarchy);

            Object.DestroyImmediate(created);
            PlayModeValueSaver.Restore();

            var restored = _parent.transform.Find("PlayModeObjectRestoreTests_Created");
            Assert.IsNotNull(restored, "the object must be back under its parent");
            Assert.AreEqual(0, restored.GetSiblingIndex());
            Assert.AreEqual(1, restored.childCount, "its children must come with it");
            Assert.AreEqual("PlayModeObjectRestoreTests_Child", restored.GetChild(0).name);

            var restoredCanvas = restored.GetComponent<Canvas>();
            Assert.IsNotNull(restoredCanvas, "its components must come with it");
            Assert.AreSame(camera, restoredCanvas.worldCamera, "the outward reference must be reconnected");

            Assert.IsFalse(AssetDatabase.IsValidFolder(SavedFolder),
                           "the copy and its folder must be cleaned up once no longer needed");
            Assert.IsNull(PlayModeValueSaver.Load(), "a successful restore consumes the store");

            Object.DestroyImmediate(restored.gameObject);
        }

        [Test]
        public void SavingSomethingInsideASavedCreatedObject_SavesThatObjectAgain()
        {
            PrePlay.Snapshot(_parent);
            var outer = new GameObject("PlayModeObjectRestoreTests_Outer");
            outer.transform.SetParent(_parent.transform);
            var inner = new GameObject("PlayModeObjectRestoreTests_Inner");
            inner.transform.SetParent(outer.transform);
            PlayModeValueSaver.SaveObjects(new[] { outer }, SaveScope.Hierarchy);

            var late = new GameObject("PlayModeObjectRestoreTests_Late");
            late.transform.SetParent(inner.transform);
            PlayModeValueSaver.SaveObjects(new[] { inner }, SaveScope.Hierarchy);

            Object.DestroyImmediate(outer);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(1, _parent.transform.childCount, "the object must come back once");
            var restored = _parent.transform.GetChild(0);
            Assert.AreEqual("PlayModeObjectRestoreTests_Outer", restored.name);
            Assert.IsNotNull(restored.Find("PlayModeObjectRestoreTests_Inner/PlayModeObjectRestoreTests_Late"),
                             "what was added under it after the first save must come with it");
        }

        [Test]
        public void CreatedObjectsUnderTheSameParent_ComeBackInTheirSiblingOrder()
        {
            var before = new GameObject("PlayModeObjectRestoreTests_Before");
            before.transform.SetParent(_parent.transform);
            var between = new GameObject("PlayModeObjectRestoreTests_Between");
            between.transform.SetParent(_parent.transform);
            var after = new GameObject("PlayModeObjectRestoreTests_After");
            after.transform.SetParent(_parent.transform);
            PrePlay.Snapshot(_parent, before, between, after);

            var first = new GameObject("PlayModeObjectRestoreTests_First");
            first.transform.SetParent(_parent.transform);
            first.transform.SetSiblingIndex(1);
            var second = new GameObject("PlayModeObjectRestoreTests_Second");
            second.transform.SetParent(_parent.transform);
            second.transform.SetSiblingIndex(3);

            // Saved in reverse order, so the restore has to sort them.
            PlayModeValueSaver.SaveObjects(new[] { second }, SaveScope.Hierarchy);
            PlayModeValueSaver.SaveObjects(new[] { first }, SaveScope.Hierarchy);

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(5, _parent.transform.childCount);
            Assert.AreEqual("PlayModeObjectRestoreTests_Before", _parent.transform.GetChild(0).name);
            Assert.AreEqual("PlayModeObjectRestoreTests_First", _parent.transform.GetChild(1).name);
            Assert.AreEqual("PlayModeObjectRestoreTests_Between", _parent.transform.GetChild(2).name);
            Assert.AreEqual("PlayModeObjectRestoreTests_Second", _parent.transform.GetChild(3).name);
            Assert.AreEqual("PlayModeObjectRestoreTests_After", _parent.transform.GetChild(4).name);
        }

        [Test]
        public void CreatedObjectReferencingAComponentAddedDuringPlay_IsReconnected()
        {
            PrePlay.Snapshot(_parent);
            var camera = _parent.AddComponent<Camera>();
            var created = new GameObject("PlayModeObjectRestoreTests_Referring");
            created.transform.SetParent(_parent.transform);
            var canvas = created.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;

            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Hierarchy);
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            Object.DestroyImmediate(created);
            Object.DestroyImmediate(camera);
            // The created object is recreated before the camera is added back.
            PlayModeValueSaver.Restore();

            var restored = _parent.transform.Find("PlayModeObjectRestoreTests_Referring");
            Assert.IsNotNull(restored, "the object must come back");
            var restoredCamera = _parent.GetComponent<Camera>();
            Assert.IsNotNull(restoredCamera, "and so must the camera");
            Assert.AreSame(restoredCamera, restored.GetComponent<Canvas>().worldCamera,
                           "the reference must point at the camera that was added back");
            Assert.IsNull(PlayModeValueSaver.Load(), "everything was applied");

            Object.DestroyImmediate(restored.gameObject);
        }

        [Test]
        public void ValueSavedOnAnObjectThatPointsIntoACreatedObject_FindsTheRecreatedOne()
        {
            var canvasGo = new GameObject("PlayModeObjectRestoreTests_Canvas");
            canvasGo.transform.SetParent(_parent.transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            PrePlay.Snapshot(_parent, canvasGo);

            var created = new GameObject("PlayModeObjectRestoreTests_Rig");
            created.transform.SetParent(_parent.transform);
            var lens = new GameObject("PlayModeObjectRestoreTests_Lens");
            lens.transform.SetParent(created.transform);
            var camera = lens.AddComponent<Camera>();
            canvas.worldCamera = camera;

            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Hierarchy);
            PlayModeValueSaver.SaveComponents(new Object[] { canvas });

            Object.DestroyImmediate(created);
            Assert.IsTrue(canvas.worldCamera == null, "precondition: the camera went with the run");

            PlayModeValueSaver.Restore();

            var restored = _parent.transform.Find("PlayModeObjectRestoreTests_Rig");
            Assert.IsNotNull(restored, "the object must come back");
            var restoredCamera = restored.GetChild(0).GetComponent<Camera>();
            Assert.IsNotNull(restoredCamera);
            Assert.AreSame(restoredCamera, canvas.worldCamera,
                           "the saved reference must lead to the recreated camera");
            Assert.IsNull(PlayModeValueSaver.Load(), "everything was applied");

            Object.DestroyImmediate(restored.gameObject);
        }

        [Test]
        public void ForgettingACreatedObject_LeavesNothingBehind()
        {
            PrePlay.Snapshot(_parent);
            var created = new GameObject("PlayModeObjectRestoreTests_Created");
            created.transform.SetParent(_parent.transform);
            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Hierarchy);

            PlayModeValueSaver.Forget(PlayModeValueSaver.RecordKind.CreatedObject, 0);
            Assert.IsNull(PlayModeValueSaver.Load(), "the record must be gone");

            // As the end of Play does when nothing is left to apply.
            PlayModeValueSaver.ClearSaved();

            Assert.IsFalse(AssetDatabase.IsValidFolder(SavedFolder), "the copy and its folder must be swept up");

            Object.DestroyImmediate(created);
        }

        [Test]
        public void UndoingAForget_BringsTheRecordBack()
        {
            PrePlay.Snapshot(_parent);
            var created = new GameObject("PlayModeObjectRestoreTests_Created");
            created.transform.SetParent(_parent.transform);
            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Hierarchy);

            PlayModeValueSaver.Forget(PlayModeValueSaver.RecordKind.CreatedObject, 0);
            Assert.IsNull(PlayModeValueSaver.Load(), "precondition: forgotten");

            Undo.PerformUndo();

            Object.DestroyImmediate(created);
            PlayModeValueSaver.Restore();

            Assert.IsNotNull(_parent.transform.Find("PlayModeObjectRestoreTests_Created"),
                             "the object must come back after the forget is undone");
        }
    }

    internal static class PrePlay
    {
        internal static void Snapshot(params GameObject[] gameObjects)
        {
            var objectIds = new HashSet<string>();
            var componentIds = new HashSet<string>();
            foreach (var go in gameObjects)
            {
                objectIds.Add(PlayModeObjectRef.Of(go));
                foreach (var component in go.GetComponents<Component>())
                    componentIds.Add(PlayModeObjectRef.Of(component));
            }
            PlayModeSnapshot.SetIdsForTests(objectIds, componentIds);
        }
    }
}
