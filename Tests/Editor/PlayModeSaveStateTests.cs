using NUnit.Framework;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeSaveStateTests
    {
        private GameObject _parent;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            PlayModeSaveState.ResetForTests();
            PlayModeChangeTracker.ResetForTests();
            _parent = new GameObject("PlayModeSaveStateTests_Parent");
            _go = new GameObject("PlayModeSaveStateTests_Target");
            _go.transform.SetParent(_parent.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            if (_parent != null)
                Object.DestroyImmediate(_parent);
            PlayModeValueSaver.ClearSaved();
            PlayModeSaveState.ResetForTests();
            PlayModeChangeTracker.ResetForTests();
        }

        [Test]
        public void ObjectReferenceChange_ShowsUp()
        {
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Assert.AreEqual(SaveState.Clean, State(canvas));

            var cameraGo = new GameObject("PlayModeSaveStateTests_Camera");
            cameraGo.transform.SetParent(_parent.transform);
            canvas.worldCamera = cameraGo.AddComponent<Camera>();

            Assert.AreEqual(SaveState.Modified, State(canvas), "a changed reference is a change to save");
        }

        [Test]
        public void State_FollowsChangesAndSaves()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 37.5f;

            Assert.AreEqual(SaveState.Clean, State(camera), "first seen is the baseline");

            camera.fieldOfView = 50f;
            Assert.AreEqual(SaveState.Modified, State(camera));

            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            Assert.AreEqual(SaveState.Saved, State(camera));

            camera.fieldOfView = 60f;
            Assert.AreEqual(SaveState.Modified, State(camera), "a change after the save is unsaved again");

            camera.fieldOfView = 50f;
            Assert.AreEqual(SaveState.Saved, State(camera), "back at the saved value is saved");
        }

        [Test]
        public void State_OfASinglePropertySave_LooksAtTheRestToo()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 37.5f;
            camera.nearClipPlane = 0.5f;
            Assert.AreEqual(SaveState.Clean, State(camera));

            camera.fieldOfView = 50f;
            PlayModeValueSaver.SaveProperty(new Object[] { camera }, "field of view");
            Assert.AreEqual(SaveState.Saved, State(camera), "the only change there is has been saved");

            camera.nearClipPlane = 2f;
            Assert.AreEqual(SaveState.Modified, State(camera), "a change outside the saved property is not");
        }

        [Test]
        public void State_OfAGameObject_FollowsItsActiveFlag()
        {
            Assert.AreEqual(SaveState.Clean, State(_go));

            _go.SetActive(false);
            Assert.AreEqual(SaveState.Modified, State(_go));

            PlayModeValueSaver.SaveObjects(new[] { _go }, SaveScope.Object);
            Assert.AreEqual(SaveState.Saved, State(_go));
        }

        [Test]
        public void State_OfAComponent_CoversWhatHoldsTheRestOfItsSettings()
        {
            var light = _go.AddComponent<Light>();
            var extra = _go.AddComponent<KeeperTestAdditionalData>();
            var targets = new Object[] { light };

            PlayModeSaveState.ForgetReadings();
            Assert.AreEqual(SaveState.Clean, PlayModeSaveState.Of(targets));

            extra.Quality = 3;
            PlayModeSaveState.ForgetReadings();
            Assert.AreEqual(SaveState.Modified, PlayModeSaveState.Of(targets),
                            "a change in the companion is a change in what the Light's inspector shows");

            PlayModeValueSaver.SaveComponents(targets);
            Assert.AreEqual(SaveState.Saved, PlayModeSaveState.Of(targets));
        }

        [Test]
        public void State_ShowsAnEditAtOnce()
        {
            var light = _go.AddComponent<Light>();
            Assert.AreEqual(SaveState.Clean, State(light));

            // No ForgetReadings here: only the edit notice can make the change show.
            PlayModeChangeTracker.NoteEditForTests(light);
            light.enabled = false;
            Assert.AreEqual(SaveState.Modified, PlayModeSaveState.Of(light),
                            "switching a component off is a change, and shows without waiting");
        }

        [Test]
        public void State_OfSomethingNeverShown_CountsTheEditFromBeforeIt()
        {
            var light = _go.AddComponent<Light>();

            PlayModeChangeTracker.NoteEditForTests(light);
            light.intensity += 2f;
            Assert.AreEqual(SaveState.Modified, State(light),
                            "the baseline is the value from before the edit, not the first one read");
        }

        [Test]
        public void HierarchyMark_ClimbsFromTheEditToTheTop()
        {
            var sibling = new GameObject("PlayModeSaveStateTests_Sibling");
            sibling.transform.SetParent(_parent.transform, false);
            var light = _go.AddComponent<Light>();
            Assert.AreEqual(SaveState.Clean, Mark(_parent), "nothing has been edited");

            PlayModeChangeTracker.NoteEditForTests(light);
            light.intensity += 2f;
            Assert.AreEqual(SaveState.Modified, Mark(_go));
            Assert.AreEqual(SaveState.Modified, Mark(_parent), "a collapsed parent shows what changed inside it");
            Assert.AreEqual(SaveState.Clean, Mark(sibling));

            PlayModeValueSaver.SaveObjects(new[] { _parent }, SaveScope.Hierarchy);
            Assert.AreEqual(SaveState.Saved, Mark(_parent));
            Assert.AreEqual(SaveState.Saved, Mark(_go));
        }

        [Test]
        public void HierarchyMark_LeavesOutWhatNobodyEditedAndWhatWasPutBack()
        {
            var light = _go.AddComponent<Light>();
            Assert.AreEqual(SaveState.Clean, State(light));

            light.intensity += 2f;
            Assert.AreEqual(SaveState.Clean, Mark(_parent),
                            "a value a script moves is not an edit, so it marks nothing");

            PlayModeChangeTracker.NoteEditForTests(light);
            light.intensity -= 2f;
            Assert.AreEqual(SaveState.Clean, Mark(_parent), "an edit put back the way it was has nothing to save");
        }

        // Drops the cached readings, as an undo does, so the values are read as they are now.
        private static SaveState State(Object target)
        {
            PlayModeSaveState.ForgetReadings();
            return PlayModeSaveState.Of(target);
        }

        private static SaveState Mark(GameObject go)
        {
            PlayModeSaveState.ForgetReadings();
            return PlayModeHierarchyMarks.StateOf(go);
        }
    }
}
