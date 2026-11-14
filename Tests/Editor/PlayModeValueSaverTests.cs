using NUnit.Framework;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeValueSaverTests
    {
        private const float Tolerance = 1e-4f;

        private GameObject _parent;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            _parent = new GameObject("PlayModeValueSaverTests_Parent");
            _go = new GameObject("PlayModeValueSaverTests_Target");
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
        }

        [Test]
        public void CaptureApplyRoundtrip()
        {
            var camera = _go.AddComponent<Camera>();
            var transform = _go.transform;

            camera.fieldOfView = 37.5f;
            camera.orthographic = true;
            camera.backgroundColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            transform.localPosition = new Vector3(1f, 2f, 3f);
            transform.localRotation = Quaternion.Euler(10f, 20f, 30f);

            // Read back, since a setter may convert the value.
            float expectedFov = camera.fieldOfView;
            Color expectedColor = camera.backgroundColor;
            Vector3 expectedPosition = transform.localPosition;
            Quaternion expectedRotation = transform.localRotation;

            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            PlayModeValueSaver.SaveComponents(new Object[] { transform });

            camera.fieldOfView = 60f;
            camera.orthographic = false;
            camera.backgroundColor = Color.red;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(expectedFov, camera.fieldOfView, Tolerance);
            Assert.IsTrue(camera.orthographic);
            Assert.AreEqual(expectedColor.r, camera.backgroundColor.r, Tolerance);
            Assert.AreEqual(expectedColor.g, camera.backgroundColor.g, Tolerance);
            Assert.AreEqual(expectedColor.b, camera.backgroundColor.b, Tolerance);
            Assert.AreEqual(expectedColor.a, camera.backgroundColor.a, Tolerance);
            Assert.AreEqual(expectedPosition.x, transform.localPosition.x, Tolerance);
            Assert.AreEqual(expectedPosition.y, transform.localPosition.y, Tolerance);
            Assert.AreEqual(expectedPosition.z, transform.localPosition.z, Tolerance);
            Assert.AreEqual(0f, Quaternion.Angle(expectedRotation, transform.localRotation), Tolerance);

            Assert.IsNull(PlayModeValueSaver.Load(), "a successful restore consumes the store");
        }

        [Test]
        public void SaveTwice_ReplacesRecord()
        {
            var camera = _go.AddComponent<Camera>();

            camera.fieldOfView = 10f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            camera.fieldOfView = 20f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            var set = PlayModeValueSaver.Load();
            Assert.AreEqual(1, set.Records.Count, "re-saving a component must replace its record");

            camera.fieldOfView = 60f;
            PlayModeValueSaver.Restore();
            Assert.AreEqual(20f, camera.fieldOfView, Tolerance);
        }

        [Test]
        public void ArrayLength_IsRestoredBeforeItsElements()
        {
            var line = _go.AddComponent<LineRenderer>();
            line.positionCount = 3;
            line.SetPosition(0, new Vector3(1f, 0f, 0f));
            line.SetPosition(1, new Vector3(2f, 0f, 0f));
            line.SetPosition(2, new Vector3(3f, 0f, 0f));

            PlayModeValueSaver.SaveComponents(new Object[] { line });

            line.positionCount = 1;
            line.SetPosition(0, Vector3.zero);

            PlayModeValueSaver.Restore();

            Assert.AreEqual(3, line.positionCount, "the array length must come back");
            Assert.AreEqual(1f, line.GetPosition(0).x, Tolerance);
            Assert.AreEqual(2f, line.GetPosition(1).x, Tolerance);
            Assert.AreEqual(3f, line.GetPosition(2).x, Tolerance);
        }

        [Test]
        public void RenamedObject_IsStillFoundWithinTheSession()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 33f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            _go.name = "PlayModeValueSaverTests_Renamed";
            camera.fieldOfView = 60f;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(33f, camera.fieldOfView, Tolerance);
            Assert.IsNull(PlayModeValueSaver.Load());
        }

        [Test]
        public void MovedObject_IsStillFoundWithinTheSession()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 33f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            _go.transform.SetParent(null);
            camera.fieldOfView = 60f;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(33f, camera.fieldOfView, Tolerance);
        }

        [Test]
        public void InALaterSession_TheObjectIsFoundByItsPath()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 33f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            PlayModeValueSaver.ExpireIdsForTests();
            camera.fieldOfView = 60f;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(33f, camera.fieldOfView, Tolerance, "the hierarchy path must still lead to it");
            Assert.IsNull(PlayModeValueSaver.Load());
        }

        [Test]
        public void InALaterSession_ARenamedObjectIsRefused()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 33f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            PlayModeValueSaver.ExpireIdsForTests();
            _go.name = "PlayModeValueSaverTests_Renamed";
            camera.fieldOfView = 60f;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(60f, camera.fieldOfView, Tolerance,
                            "with only the path to go by, a different name means a different object");
        }

        [Test]
        public void PartialFailure_KeepsTheUnresolvedRecord()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 33f;

            var other = new GameObject("PlayModeValueSaverTests_Other");
            other.transform.SetParent(_parent.transform);
            var otherCamera = other.AddComponent<Camera>();
            otherCamera.fieldOfView = 44f;

            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            PlayModeValueSaver.SaveComponents(new Object[] { otherCamera });

            PlayModeValueSaver.ExpireIdsForTests();
            other.name = "PlayModeValueSaverTests_OtherRenamed";
            camera.fieldOfView = 60f;
            otherCamera.fieldOfView = 60f;

            PlayModeValueSaver.Restore();

            Assert.AreEqual(33f, camera.fieldOfView, Tolerance, "the resolvable record must be applied");
            Assert.AreEqual(60f, otherCamera.fieldOfView, Tolerance, "the renamed one must be refused");

            var left = PlayModeValueSaver.Load();
            Assert.AreEqual(1, left.Records.Count, "the refused record must survive for a retry");
        }

        [Test]
        public void SavedProperty_IsRestoredAndTheRestIsNot()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 25f;
            camera.nearClipPlane = 0.5f;

            PlayModeValueSaver.SaveProperty(new Object[] { camera }, "field of view");

            camera.fieldOfView = 60f;
            camera.nearClipPlane = 2f;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(25f, camera.fieldOfView, Tolerance, "the saved property must come back");
            Assert.AreEqual(2f, camera.nearClipPlane, Tolerance, "what was not saved must be left alone");
        }

        [Test]
        public void SavedProperty_ReplacesItsValueInAnExistingRecord()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 25f;
            camera.nearClipPlane = 0.5f;
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            camera.fieldOfView = 30f;
            camera.nearClipPlane = 2f;
            PlayModeValueSaver.SaveProperty(new Object[] { camera }, "field of view");

            camera.fieldOfView = 60f;
            camera.nearClipPlane = 3f;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(30f, camera.fieldOfView, Tolerance, "the property takes its later value");
            Assert.AreEqual(0.5f, camera.nearClipPlane, Tolerance, "the rest keeps the whole-component save");
        }

        [Test]
        public void SavingAnArrayElement_SavesTheWholeArray()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Numbers = new[] { 1, 2, 3 };

            PlayModeValueSaver.SaveProperty(new Object[] { behaviour }, "Numbers.Array.data[2]");

            behaviour.Numbers = new[] { 9 };
            PlayModeValueSaver.Restore();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, behaviour.Numbers,
                                      "an element is only meaningful together with the array's length");
        }

        [Test]
        public void ForgettingARecord_LeavesTheOthers()
        {
            var camera = _go.AddComponent<Camera>();
            camera.fieldOfView = 25f;
            _go.transform.localPosition = new Vector3(1f, 2f, 3f);
            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            PlayModeValueSaver.SaveComponents(new Object[] { _go.transform });

            PlayModeValueSaver.Forget(PlayModeValueSaver.RecordKind.Values, 0);

            var set = PlayModeValueSaver.Load();
            Assert.AreEqual(1, set.Records.Count);

            camera.fieldOfView = 60f;
            _go.transform.localPosition = Vector3.zero;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(60f, camera.fieldOfView, Tolerance, "the forgotten save must not be applied");
            Assert.AreEqual(1f, _go.transform.localPosition.x, Tolerance, "the other one still is");
        }

        [Test]
        public void SavingAgainAfterAForget_FindsTheRightRecord()
        {
            var camera = _go.AddComponent<Camera>();
            var light = _go.AddComponent<Light>();
            camera.fieldOfView = 25f;
            _go.transform.localPosition = new Vector3(1f, 2f, 3f);
            PlayModeValueSaver.SaveComponents(new Object[] { camera });
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            PlayModeValueSaver.SaveComponents(new Object[] { _go.transform });

            // Removing the first record shifts the others.
            PlayModeValueSaver.Forget(PlayModeValueSaver.RecordKind.Values, 0);
            light.intensity = 7f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });
            PlayModeValueSaver.SaveComponents(new Object[] { camera });

            camera.fieldOfView = 60f;
            light.intensity = 2f;
            _go.transform.localPosition = Vector3.zero;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(7f, light.intensity, Tolerance, "the second save of the light is applied");
            Assert.AreEqual(25f, camera.fieldOfView, Tolerance);
            Assert.AreEqual(1f, _go.transform.localPosition.x, Tolerance);
        }
    }
}
