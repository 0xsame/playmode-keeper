using NUnit.Framework;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeGameObjectTests
    {
        private const float Tolerance = 1e-4f;

        private GameObject _root;
        private GameObject _a;
        private GameObject _b;
        private GameObject _c;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            _root = new GameObject("PlayModeGameObjectTests_Root");
            _a = Child("A", _root);
            _b = Child("B", _root);
            _c = Child("C", _root);
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeValueSaver.ClearSaved();
            if (_a != null) Object.DestroyImmediate(_a);
            if (_b != null) Object.DestroyImmediate(_b);
            if (_c != null) Object.DestroyImmediate(_c);
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static GameObject Child(string name, GameObject parent)
        {
            var go = new GameObject("PlayModeGameObjectTests_" + name);
            go.transform.SetParent(parent.transform);
            return go;
        }

        private string Order()
        {
            var names = new string[_root.transform.childCount];
            for (int i = 0; i < names.Length; i++)
                names[i] = _root.transform.GetChild(i).name.Replace("PlayModeGameObjectTests_", "");
            return string.Join(",", names);
        }

        [Test]
        public void InactiveObject_IsRestoredInactive()
        {
            _a.SetActive(false);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.SetActive(true);
            PlayModeValueSaver.Restore();

            Assert.IsFalse(_a.activeSelf, "switching an object off is the change that was saved");
            Assert.IsNull(PlayModeValueSaver.Load(), "a successful restore consumes the store");
        }

        [Test]
        public void NameLayerAndTag_AreRestored()
        {
            _a.name = "PlayModeGameObjectTests_Renamed";
            _a.layer = 4;
            _a.tag = "EditorOnly";
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.name = "PlayModeGameObjectTests_A";
            _a.layer = 0;
            _a.tag = "Untagged";
            PlayModeValueSaver.Restore();

            Assert.AreEqual("PlayModeGameObjectTests_Renamed", _a.name);
            Assert.AreEqual(4, _a.layer);
            Assert.AreEqual("EditorOnly", _a.tag);
        }

        [Test]
        public void SavingTheObjectAlone_LeavesItsComponentsOut()
        {
            _a.transform.localPosition = new Vector3(1f, 2f, 3f);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.transform.localPosition = Vector3.zero;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(0f, _a.transform.localPosition.x, Tolerance,
                            "the Transform was not asked for, so it is not written");
        }

        [Test]
        public void SaveAll_CoversEveryComponent()
        {
            var light = _a.AddComponent<Light>();
            var collider = _a.AddComponent<BoxCollider>();
            light.intensity = 4f;
            collider.size = new Vector3(2f, 3f, 4f);
            _a.transform.localPosition = new Vector3(1f, 2f, 3f);
            _a.SetActive(false);

            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.ObjectAndComponents);

            light.intensity = 1f;
            collider.size = Vector3.one;
            _a.transform.localPosition = Vector3.zero;
            _a.SetActive(true);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(4f, light.intensity, Tolerance);
            Assert.AreEqual(3f, collider.size.y, Tolerance);
            Assert.AreEqual(2f, _a.transform.localPosition.y, Tolerance);
            Assert.IsFalse(_a.activeSelf);
        }

        [Test]
        public void SaveAll_LeavesChildrenOut()
        {
            var child = Child("Grandchild", _a);
            var light = child.AddComponent<Light>();
            light.intensity = 4f;
            child.SetActive(false);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.ObjectAndComponents);

            light.intensity = 1f;
            child.SetActive(true);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(1f, light.intensity, Tolerance, "a component on a child is not part of the save");
            Assert.IsTrue(child.activeSelf, "and neither is the child's own state");
        }

        [Test]
        public void SaveHierarchy_CoversEverythingBelow()
        {
            var child = Child("Grandchild", _a);
            var light = child.AddComponent<Light>();
            light.intensity = 4f;
            child.SetActive(false);

            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Hierarchy);

            light.intensity = 1f;
            child.SetActive(true);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(4f, light.intensity, Tolerance, "a component on a child is part of the hierarchy");
            Assert.IsFalse(child.activeSelf, "and so is the child's own state");
            Object.DestroyImmediate(child);
        }

        [Test]
        public void ObjectMovedUnderAnotherParent_IsMovedThereAgain()
        {
            _a.transform.SetParent(_b.transform);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.transform.SetParent(_root.transform);
            _a.transform.SetSiblingIndex(0);
            PlayModeValueSaver.Restore();

            Assert.AreSame(_b.transform, _a.transform.parent);
        }

        [Test]
        public void ObjectMovedToTheSceneRoot_IsMovedThereAgain()
        {
            _a.transform.SetParent(null);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.transform.SetParent(_root.transform);
            _a.transform.SetSiblingIndex(0);
            PlayModeValueSaver.Restore();

            Assert.IsNull(_a.transform.parent);
        }

        [Test]
        public void MovedObject_KeepsItsWorldPosition_UnlessItsTransformWasSaved()
        {
            _b.transform.position = new Vector3(10f, 0f, 0f);
            _a.transform.position = new Vector3(1f, 0f, 0f);

            _a.transform.SetParent(_b.transform, true);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);
            _a.transform.SetParent(_root.transform, true);

            PlayModeValueSaver.Restore();

            Assert.AreSame(_b.transform, _a.transform.parent);
            Assert.AreEqual(1f, _a.transform.position.x, Tolerance,
                            "re-parenting alone leaves the object where it is in the world");
        }

        [Test]
        public void MovedObject_TakesItsSavedLocalValues()
        {
            _b.transform.position = new Vector3(10f, 0f, 0f);

            _a.transform.SetParent(_b.transform, false);
            _a.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.ObjectAndComponents);

            _a.transform.SetParent(_root.transform, false);
            _a.transform.localPosition = new Vector3(3f, 0f, 0f);

            PlayModeValueSaver.Restore();

            Assert.AreSame(_b.transform, _a.transform.parent);
            Assert.AreEqual(0.5f, _a.transform.localPosition.x, Tolerance,
                            "the Transform is written after the move, under the parent it was measured against");
        }

        [Test]
        public void ObjectMovedAmongItsSiblings_IsMovedThereAgain()
        {
            _c.transform.SetSiblingIndex(0);
            Assert.AreEqual("C,A,B", Order(), "precondition");
            PlayModeValueSaver.SaveObjects(new[] { _c }, SaveScope.Object);

            _c.transform.SetSiblingIndex(2);
            Assert.AreEqual("A,B,C", Order(), "precondition");
            PlayModeValueSaver.Restore();

            Assert.AreEqual("C,A,B", Order());
        }

        [Test]
        public void ObjectMovedToTheEnd_IsMovedThereAgain()
        {
            _a.transform.SetSiblingIndex(2);
            Assert.AreEqual("B,C,A", Order(), "precondition");
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _a.transform.SetSiblingIndex(0);
            PlayModeValueSaver.Restore();

            Assert.AreEqual("B,C,A", Order());
        }

        [Test]
        public void ObjectThatDidNotMove_StaysWhenASiblingDid()
        {
            _c.transform.SetSiblingIndex(0);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);

            _c.transform.SetSiblingIndex(2);
            Assert.AreEqual("A,B,C", Order(), "precondition");
            PlayModeValueSaver.Restore();

            Assert.AreEqual("A,B,C", Order());
        }

        [Test]
        public void ObjectWhoseNeighboursWereDestroyedDuringPlay_IsNotMoved()
        {
            var left = Child("Left", _root);
            var right = Child("Right", _root);
            left.transform.SetSiblingIndex(1);
            right.transform.SetSiblingIndex(3);
            Assert.AreEqual("A,Left,B,Right,C", Order(), "precondition");

            // B is saved with A and C as its neighbours.
            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
            PlayModeValueSaver.SaveObjects(new[] { _b }, SaveScope.Object);

            left = Child("Left", _root);
            right = Child("Right", _root);
            left.transform.SetSiblingIndex(1);
            right.transform.SetSiblingIndex(3);
            Assert.AreEqual("A,Left,B,Right,C", Order(), "precondition");
            PlayModeValueSaver.Restore();

            Assert.AreEqual("A,Left,B,Right,C", Order());
            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
        }

        [Test]
        public void UnmovedObject_LeavesTheHierarchyAlone()
        {
            PlayModeValueSaver.SaveObjects(new[] { _a, _b, _c }, SaveScope.Object);
            PlayModeValueSaver.Restore();

            Assert.AreEqual("A,B,C", Order());
            Assert.AreSame(_root.transform, _b.transform.parent);
        }

        [Test]
        public void ParentAndChildThatSwappedPlaces_AreBothMoved()
        {
            _b.transform.SetParent(null);
            _a.transform.SetParent(_b.transform);
            PlayModeValueSaver.SaveObjects(new[] { _a, _b }, SaveScope.Object);

            _a.transform.SetParent(_root.transform);
            _b.transform.SetParent(_a.transform);
            // A cannot go under B while B is under A, so B has to move first.
            PlayModeValueSaver.Restore();

            Assert.IsNull(_b.transform.parent, "B must be back at the root");
            Assert.AreSame(_b.transform, _a.transform.parent, "and A under it");
        }

        [Test]
        public void InALaterSession_NothingIsMoved()
        {
            _a.transform.SetParent(_b.transform);
            PlayModeValueSaver.SaveObjects(new[] { _a }, SaveScope.Object);
            _a.transform.SetParent(_root.transform);
            _a.transform.SetSiblingIndex(0);

            PlayModeValueSaver.ExpireIdsForTests();
            PlayModeValueSaver.Restore();

            Assert.AreSame(_root.transform, _a.transform.parent);
        }
    }
}
