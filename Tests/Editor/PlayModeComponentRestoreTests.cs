using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeComponentRestoreTests
    {
        private GameObject _object;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            _object = new GameObject("PlayModeComponentRestoreTests_Object");
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeSnapshot.SetIdsForTests(null, null);
            PlayModeValueSaver.ClearSaved();
            if (_object != null)
                Object.DestroyImmediate(_object);
        }

        [Test]
        public void AddedComponentBroughtInByRequireComponent_IsFilledInRatherThanAddedTwice()
        {
            PrePlay.Snapshot(_object);
            var joint = _object.AddComponent<ConfigurableJoint>();
            var body = _object.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "precondition: the joint brought a Rigidbody with it");
            body.mass = 17.5f;
            PlayModeValueSaver.SaveComponents(new Object[] { joint });
            PlayModeValueSaver.SaveComponents(new Object[] { body });

            Object.DestroyImmediate(joint);
            Object.DestroyImmediate(body);
            Assert.IsNull(_object.GetComponent<Rigidbody>(), "precondition: both are gone");

            // Adding the joint back adds a Rigidbody, which the Rigidbody's record then fills in.
            PlayModeValueSaver.Restore();

            Assert.AreEqual(1, _object.GetComponents<Rigidbody>().Length,
                            "a second Rigidbody should not have been attempted");
            Assert.IsNotNull(_object.GetComponent<ConfigurableJoint>(), "the joint should be back");
            Assert.AreEqual(17.5f, _object.GetComponent<Rigidbody>().mass, 0.001f,
                            "the saved values should still have been applied");
            Assert.IsNull(PlayModeValueSaver.Load(), "both records were applied");
        }

        [Test]
        public void AddedComponent_DoesNotAdoptAPreExistingOneAtItsIndex()
        {
            var existing = _object.AddComponent<BoxCollider>();
            existing.isTrigger = false;
            PrePlay.Snapshot(_object);

            var added = _object.AddComponent<BoxCollider>();
            added.isTrigger = true;
            PlayModeValueSaver.SaveComponents(new Object[] { added });
            Object.DestroyImmediate(added);

            PlayModeValueSaver.Restore();

            var all = _object.GetComponents<BoxCollider>();
            Assert.AreEqual(2, all.Length, "the addition should have been created, not adopted");
            Assert.IsFalse(all[0].isTrigger, "the pre-existing component should be untouched");
            Assert.IsTrue(all[1].isTrigger, "and the new one should carry the saved values");
        }

        [Test]
        public void UndoingASave_DropsItsRecord()
        {
            var light = _object.AddComponent<Light>();
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            Assert.IsNotNull(PlayModeValueSaver.Load(), "precondition: the save was recorded");

            Undo.PerformUndo();

            Assert.IsNull(PlayModeValueSaver.Load(),
                          "undo should have taken the record with it, or the values would be " +
                          "written when Play stops after all");
        }

        [Test]
        public void AnUnrelatedUndo_LeavesTheStoreAlone()
        {
            var light = _object.AddComponent<Light>();
            PlayModeValueSaver.SaveComponents(new Object[] { light });

            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(_object, "Rename");
            _object.name = "PlayModeComponentRestoreTests_Renamed";
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();

            Assert.AreEqual("PlayModeComponentRestoreTests_Object", _object.name,
                            "precondition: the rename was what got undone");
            var set = PlayModeValueSaver.Load();
            Assert.IsNotNull(set, "the save itself was not undone, so its record must still be there");
            Assert.AreEqual(1, set.Records.Count);

            Undo.PerformUndo();

            Assert.IsNull(PlayModeValueSaver.Load(), "the second undo reaches the save and takes the record");
        }

        [Test]
        public void UndoingASecondSave_LeavesTheFirst()
        {
            var light = _object.AddComponent<Light>();
            var collider = _object.AddComponent<BoxCollider>();
            light.intensity = 4f;
            collider.isTrigger = true;
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            PlayModeValueSaver.SaveComponents(new Object[] { collider });

            Undo.PerformUndo();

            light.intensity = 1f;
            collider.isTrigger = false;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(4f, light.intensity, 0.001f, "the first save should still be applied");
            Assert.IsFalse(collider.isTrigger, "the undone save should not be");
        }
    }
}
