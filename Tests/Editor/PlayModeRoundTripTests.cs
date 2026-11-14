using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayModeKeeper.Tests
{
    public class PlayModeRoundTripTests
    {
        private const float Tolerance = 1e-4f;
        private const string RootName = "PlayModeRoundTripTests_Root";
        private const string CreatedName = "PlayModeRoundTripTests_Created";
        private const string SavedFolder = "Assets/PlayModeKeeperSaved";

        [SetUp]
        public void SetUp()
        {
            // SetUp runs again on entering Play; objects made then would count as created during Play.
            if (Application.isPlaying)
                return;

            PlayModeValueSaver.ClearSaved();
            DestroyRoot();

            var root = new GameObject(RootName);
            var light = root.AddComponent<Light>();
            light.intensity = 1f;
            light.range = 10f;
            var a = new GameObject("A");
            a.transform.SetParent(root.transform);
            a.AddComponent<KeeperTestBehaviour>();
            var b = new GameObject("B");
            b.transform.SetParent(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeValueSaver.ClearSaved();
            DestroyRoot();
            if (AssetDatabase.IsValidFolder(SavedFolder))
                AssetDatabase.DeleteAsset(SavedFolder);
        }

        private static void DestroyRoot()
        {
            GameObject root;
            while ((root = GameObject.Find(RootName)) != null)
                Object.DestroyImmediate(root);
        }

        private static GameObject Root()
        {
            var root = GameObject.Find(RootName);
            Assert.IsNotNull(root, "the test object must be in the scene");
            return root;
        }

        [UnityTest]
        public IEnumerator SavedComponent_KeepsItsValuesWhenPlayStops()
        {
            yield return new EnterPlayMode();
            yield return null;
            var light = Root().GetComponent<Light>();
            light.intensity = 7f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            light.intensity = 9f;

            yield return new ExitPlayMode();
            yield return null;
            Assert.AreEqual(7f, Root().GetComponent<Light>().intensity, Tolerance);
            Assert.IsNull(PlayModeValueSaver.Load(), "the restore ran by itself and used the record up");
        }

        [UnityTest]
        public IEnumerator SavedProperty_KeepsThatValueOnly()
        {
            yield return new EnterPlayMode();
            yield return null;
            var light = Root().GetComponent<Light>();
            light.intensity = 7f;
            light.range = 30f;
            PlayModeValueSaver.SaveProperty(new Object[] { light }, "m_Intensity");

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root().GetComponent<Light>();
            Assert.AreEqual(7f, restored.intensity, Tolerance);
            Assert.AreEqual(10f, restored.range, Tolerance, "the rest of the component went with the run");
        }

        [UnityTest]
        public IEnumerator ObjectRenamedDuringPlay_IsStillRestored()
        {
            yield return new EnterPlayMode();
            yield return null;
            var root = Root();
            var light = root.GetComponent<Light>();
            root.name = RootName + "_Renamed";
            light.intensity = 7f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });

            yield return new ExitPlayMode();
            yield return null;
            // The name was not saved, so Root() finds the object by its original name.
            Assert.AreEqual(7f, Root().GetComponent<Light>().intensity, Tolerance);
        }

        [UnityTest]
        public IEnumerator ObjectMovedToDontDestroyOnLoad_IsStillRestored()
        {
            yield return new EnterPlayMode();
            yield return null;
            var root = Root();
            Object.DontDestroyOnLoad(root);
            var light = root.GetComponent<Light>();
            light.intensity = 7f;
            PlayModeValueSaver.SaveObjects(new[] { root }, SaveScope.ObjectAndComponents);

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root();
            Assert.AreEqual(7f, restored.GetComponent<Light>().intensity, Tolerance);
            Assert.IsTrue(restored.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
                          "where it sat in the other scene must not have moved it in this one");
        }

        [UnityTest]
        public IEnumerator ObjectStateAndPlace_AreWrittenBack()
        {
            yield return new EnterPlayMode();
            yield return null;
            var root = Root();
            var a = root.transform.Find("A").gameObject;
            var b = root.transform.Find("B").gameObject;
            b.transform.SetParent(a.transform);
            a.SetActive(false);
            PlayModeValueSaver.SaveObjects(new[] { a, b }, SaveScope.Object);

            yield return new ExitPlayMode();
            yield return null;
            var restoredA = Root().transform.Find("A");
            Assert.IsNotNull(restoredA);
            Assert.IsFalse(restoredA.gameObject.activeSelf, "switched off during the run and saved");
            Assert.IsNotNull(restoredA.Find("B"), "B was moved under A during the run and saved");
        }

        [UnityTest]
        public IEnumerator SiblingOrder_IsWrittenBack()
        {
            yield return new EnterPlayMode();
            yield return null;
            var b = Root().transform.Find("B");
            b.SetSiblingIndex(0);
            PlayModeValueSaver.SaveObjects(new[] { b.gameObject }, SaveScope.Object);

            yield return new ExitPlayMode();
            yield return null;
            Assert.AreEqual("B", Root().transform.GetChild(0).name);
        }

        [UnityTest]
        public IEnumerator ComponentAddedDuringPlay_IsAddedBack()
        {
            yield return new EnterPlayMode();
            yield return null;
            var collider = Root().AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 3f, 4f);
            PlayModeValueSaver.SaveComponents(new Object[] { collider });

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root().GetComponent<BoxCollider>();
            Assert.IsNotNull(restored, "the component must have been added back");
            Assert.AreEqual(3f, restored.size.y, Tolerance);
            Assert.AreEqual(1, Root().GetComponents<BoxCollider>().Length);
        }

        [UnityTest]
        public IEnumerator ExistingComponent_IsNotMistakenForAnAddition()
        {
            yield return new EnterPlayMode();
            yield return null;
            var light = Root().GetComponent<Light>();
            light.intensity = 7f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });

            yield return new ExitPlayMode();
            yield return null;
            Assert.AreEqual(1, Root().GetComponents<Light>().Length, "nothing may have been added");
            Assert.AreEqual(7f, Root().GetComponent<Light>().intensity, Tolerance,
                            "the value must be written to the Light that was there");
        }

        [UnityTest]
        public IEnumerator ObjectCreatedDuringPlay_IsRecreated()
        {
            yield return new EnterPlayMode();
            yield return null;
            var created = new GameObject(CreatedName);
            created.transform.SetParent(Root().transform);
            created.AddComponent<Light>().intensity = 3f;
            Assert.IsTrue(PlayModeValueSaver.IsCreatedDuringPlay(created),
                          "precondition: the snapshot tells it from what was there before");
            Assert.IsFalse(PlayModeValueSaver.IsCreatedDuringPlay(Root()));
            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.ObjectAndComponents);

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root().transform.Find(CreatedName);
            Assert.IsNotNull(restored, "the object must be back under its parent");
            Assert.AreEqual(3f, restored.GetComponent<Light>().intensity, Tolerance);
            Assert.IsFalse(AssetDatabase.IsValidFolder(SavedFolder), "the copy it travelled in must be gone");
        }

        [UnityTest]
        public IEnumerator ComponentOnAnUnsavedCreatedObject_IsNotRecorded()
        {
            yield return new EnterPlayMode();
            yield return null;
            var created = new GameObject(CreatedName);
            created.transform.SetParent(Root().transform);
            var light = created.AddComponent<Light>();
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            Assert.IsNull(PlayModeValueSaver.Load(),
                          "a record for something on an object that will not exist could never be applied");

            yield return new ExitPlayMode();
            yield return null;
            Assert.IsNull(Root().transform.Find(CreatedName));
        }

        [UnityTest]
        public IEnumerator ComponentOnASavedCreatedObject_TakesItsLatestValuesAlong()
        {
            yield return new EnterPlayMode();
            yield return null;
            var created = new GameObject(CreatedName);
            created.transform.SetParent(Root().transform);
            var light = created.AddComponent<Light>();
            light.intensity = 3f;
            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Object);
            light.intensity = 6f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            Assert.AreEqual(1, PlayModeValueSaver.Load().Added.Count, "still one created object");

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root().transform.Find(CreatedName);
            Assert.IsNotNull(restored);
            Assert.AreEqual(6f, restored.GetComponent<Light>().intensity, Tolerance);
        }

        [UnityTest]
        public IEnumerator ReferenceToAnObjectCreatedDuringPlay_FollowsItWhenItIsSaved()
        {
            yield return new EnterPlayMode();
            yield return null;
            var created = new GameObject(CreatedName);
            created.transform.SetParent(Root().transform);
            var behaviour = Root().transform.Find("A").GetComponent<KeeperTestBehaviour>();
            behaviour.Reference = created;
            PlayModeValueSaver.SaveObjects(new[] { created }, SaveScope.Object);
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            yield return new ExitPlayMode();
            yield return null;
            var restored = Root().transform.Find(CreatedName);
            Assert.IsNotNull(restored);
            Assert.AreSame(restored.gameObject,
                           Root().transform.Find("A").GetComponent<KeeperTestBehaviour>().Reference,
                           "the saved reference must point at the recreated object");
        }
    }

    public class PlayModeEditorHookTests
    {
        [Test]
        public void TitleBarHook_IsSetUp()
        {
            Assert.IsTrue(PlayModeSaveHeaderButton.HookAvailable);
        }
    }
}
