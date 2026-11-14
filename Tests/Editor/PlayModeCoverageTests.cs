using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PlayModeKeeper.Tests
{
    public class PlayModeCoverageTests
    {
        private const float Tolerance = 1e-4f;
        private const string MeshPath = "Assets/PlayModeCoverageTests_Mesh.asset";
        private const string PrefabPath = "Assets/PlayModeCoverageTests_Prefab.prefab";

        private GameObject _go;
        private GameObject _other;

        [SetUp]
        public void SetUp()
        {
            PlayModeValueSaver.ClearSaved();
            PlayModeKeeperSettings.OverrideForTests(true, PlayModeKeeperSettings.DefaultMaxArraySize);
            _go = new GameObject("PlayModeCoverageTests_Target");
            _other = new GameObject("PlayModeCoverageTests_Other");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
            if (_other != null)
                Object.DestroyImmediate(_other);
            PlayModeValueSaver.ClearSaved();
            PlayModeKeeperSettings.OverrideForTests(null, null);
            AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        [Test]
        public void BuiltInComponent_EnabledFlagIsRestored()
        {
            var light = _go.AddComponent<Light>();
            light.enabled = false;
            PlayModeValueSaver.SaveComponents(new Object[] { light });

            light.enabled = true;
            PlayModeValueSaver.Restore();

            Assert.IsFalse(light.enabled, "the checkbox on the title bar is part of what was saved");
        }

        [Test]
        public void Script_EnabledFlagIsRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.enabled = false;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.enabled = true;
            PlayModeValueSaver.Restore();

            Assert.IsFalse(behaviour.enabled);
        }

        [Test]
        public void Collider_EnabledFlagIsRestored()
        {
            // Not a Behaviour, but it has the same checkbox.
            var collider = _go.AddComponent<BoxCollider>();
            collider.enabled = false;
            PlayModeValueSaver.SaveComponents(new Object[] { collider });

            collider.enabled = true;
            PlayModeValueSaver.Restore();

            Assert.IsFalse(collider.enabled);
        }

        [Test]
        public void HiddenField_IsRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Hidden = 7.5f;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Hidden = 0f;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(7.5f, behaviour.Hidden, Tolerance,
                            "a custom editor draws hidden fields, so they are tuned like any other");
        }

        [Test]
        public void HiddenField_IsLeftOutWhenThePreferenceSaysSo()
        {
            PlayModeKeeperSettings.OverrideForTests(false, PlayModeKeeperSettings.DefaultMaxArraySize);
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Hidden = 7.5f;
            behaviour.Visible = 1.5f;
            behaviour.enabled = false;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Hidden = 0f;
            behaviour.Visible = 0f;
            behaviour.enabled = true;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(0f, behaviour.Hidden, Tolerance, "the hidden field was not part of the save");
            Assert.AreEqual(1.5f, behaviour.Visible, Tolerance, "the visible one still is");
            Assert.IsFalse(behaviour.enabled, "and so is the enabled flag");
        }

        [Test]
        public void FlagsEnum_WithSeveralBitsSet_IsRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Flags = KeeperTestFlags.A | KeeperTestFlags.C;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Flags = KeeperTestFlags.B;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(KeeperTestFlags.A | KeeperTestFlags.C, behaviour.Flags,
                            "a combination has no index among the names; the number is what counts");
        }

        [Test]
        public void UnsignedLong_AboveTheSignedRange_IsRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Big = ulong.MaxValue - 5;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Big = 1;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(ulong.MaxValue - 5, behaviour.Big);
        }

        [Test]
        public void Curve_IsRestoredKeyForKey()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 2f),
                new Keyframe(0.5f, 1f, -1f, 3f),
                new Keyframe(1f, 0.25f));
            curve.preWrapMode = WrapMode.PingPong;
            curve.postWrapMode = WrapMode.Loop;
            behaviour.Curve = curve;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Curve = AnimationCurve.Linear(0f, 5f, 2f, 6f);
            PlayModeValueSaver.Restore();

            var restored = behaviour.Curve;
            Assert.AreEqual(3, restored.length, "the keys must come back");
            Assert.AreEqual(0.5f, restored[1].time, Tolerance);
            Assert.AreEqual(1f, restored[1].value, Tolerance);
            Assert.AreEqual(-1f, restored[1].inTangent, Tolerance);
            Assert.AreEqual(3f, restored[1].outTangent, Tolerance);
            Assert.AreEqual(0.25f, restored[2].value, Tolerance);
            Assert.AreEqual(WrapMode.PingPong, restored.preWrapMode);
            Assert.AreEqual(WrapMode.Loop, restored.postWrapMode);
        }

        [Test]
        public void Gradient_IsRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.green, 0.4f),
                    new GradientColorKey(Color.blue, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 1f) });
            behaviour.Gradient = gradient;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Gradient = new Gradient();
            PlayModeValueSaver.Restore();

            var restored = behaviour.Gradient;
            Assert.AreEqual(3, restored.colorKeys.Length, "the colour keys must come back");
            Assert.AreEqual(0.4f, restored.colorKeys[1].time, 0.01f);
            Assert.AreEqual(1f, restored.colorKeys[1].color.g, Tolerance);
            Assert.AreEqual(0f, restored.colorKeys[1].color.r, Tolerance);
            Assert.AreEqual(2, restored.alphaKeys.Length);
            Assert.AreEqual(0.25f, restored.alphaKeys[1].alpha, Tolerance);
        }

        [Test]
        public void ManagedReference_TypeAndFieldsAreRestored()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Shape = new KeeperTestCircle { Radius = 2.5f };
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Shape = new KeeperTestBox { Size = new Vector2(1f, 1f) };
            PlayModeValueSaver.Restore();

            Assert.IsInstanceOf<KeeperTestCircle>(behaviour.Shape, "the type of the instance must come back");
            Assert.AreEqual(2.5f, ((KeeperTestCircle)behaviour.Shape).Radius, Tolerance, "and its fields with it");
        }

        [Test]
        public void ManagedReference_ThatWasEmptied_IsRestoredEmpty()
        {
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Shape = null;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Shape = new KeeperTestBox();
            PlayModeValueSaver.Restore();

            Assert.IsNull(behaviour.Shape);
        }

        [Test]
        public void ArrayLongerThanTheLimit_IsLeftOut()
        {
            PlayModeKeeperSettings.OverrideForTests(true, 4);
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Numbers = new[] { 1, 2, 3, 4, 5, 6 };
            behaviour.Visible = 3f;
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Numbers = new[] { 9 };
            behaviour.Visible = 0f;
            PlayModeValueSaver.Restore();

            CollectionAssert.AreEqual(new[] { 9 }, behaviour.Numbers, "an array that was not saved is not touched");
            Assert.AreEqual(3f, behaviour.Visible, Tolerance, "the rest of the component is saved as usual");
        }

        [Test]
        public void AssetReference_IsRestored()
        {
            var mesh = CreateMeshAsset();
            var filter = _go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            PlayModeValueSaver.SaveComponents(new Object[] { filter });

            filter.sharedMesh = null;
            PlayModeValueSaver.Restore();

            Assert.AreSame(mesh, filter.sharedMesh, "a reference to an asset is found again by its GUID");
        }

        [Test]
        public void AssetReference_SurvivesALaterSession()
        {
            var mesh = CreateMeshAsset();
            var filter = _go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            PlayModeValueSaver.SaveComponents(new Object[] { filter });

            PlayModeValueSaver.ExpireIdsForTests();
            filter.sharedMesh = null;
            PlayModeValueSaver.Restore();

            Assert.AreSame(mesh, filter.sharedMesh, "a GUID does not depend on the session");
        }

        [Test]
        public void SceneReference_IsRestored()
        {
            var camera = _other.AddComponent<Camera>();
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            PlayModeValueSaver.SaveComponents(new Object[] { canvas });

            canvas.worldCamera = null;
            PlayModeValueSaver.Restore();

            Assert.AreSame(camera, canvas.worldCamera);
        }

        [Test]
        public void ClearedReference_IsRestoredAsCleared()
        {
            var camera = _other.AddComponent<Camera>();
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = null;
            PlayModeValueSaver.SaveComponents(new Object[] { canvas });

            canvas.worldCamera = camera;
            PlayModeValueSaver.Restore();

            Assert.IsTrue(canvas.worldCamera == null, "emptying a field is a change like any other");
        }

        [Test]
        public void ReferenceToSomethingThatOnlyExistsDuringPlay_IsLeftAlone()
        {
            var mesh = CreateMeshAsset();
            var filter = _go.AddComponent<MeshFilter>();
            var runtimeMesh = new Mesh();
            filter.sharedMesh = runtimeMesh;
            PlayModeValueSaver.SaveComponents(new Object[] { filter });

            filter.sharedMesh = mesh;
            Object.DestroyImmediate(runtimeMesh);
            PlayModeValueSaver.Restore();

            Assert.AreSame(mesh, filter.sharedMesh,
                           "a reference that could not be stored must not overwrite the one in the scene");
        }

        [Test]
        public void ReferenceWhoseTargetWasDestroyed_IsLeftAlone()
        {
            var doomed = new GameObject("PlayModeCoverageTests_Doomed");
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.Reference = doomed;
            // Destroying the target makes the field read as null without clearing it.
            Object.DestroyImmediate(doomed);
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.Reference = _other;
            PlayModeValueSaver.Restore();

            Assert.AreSame(_other, behaviour.Reference,
                           "a reference to something destroyed must not be saved as an empty field");
        }

        [Test]
        public void ReferenceArray_ThatGrew_DoesNotInventReferences()
        {
            var mesh = CreateMeshAsset();
            var runtimeMesh = new Mesh();
            var behaviour = _go.AddComponent<KeeperTestBehaviour>();
            behaviour.References = new Object[] { mesh, _other, runtimeMesh };
            PlayModeValueSaver.SaveComponents(new Object[] { behaviour });

            behaviour.References = new Object[] { mesh };
            Object.DestroyImmediate(runtimeMesh);
            PlayModeValueSaver.Restore();

            Assert.AreEqual(3, behaviour.References.Length, "the length must come back");
            Assert.AreSame(mesh, behaviour.References[0]);
            Assert.AreSame(_other, behaviour.References[1]);
            Assert.IsTrue(behaviour.References[2] == null,
                          "a new slot that could not be filled must be empty, not a copy of its neighbour");
        }

        [Test]
        public void SavingAComponent_SavesWhatHoldsTheRestOfItsSettings()
        {
            var light = _go.AddComponent<Light>();
            var extra = _go.AddComponent<KeeperTestAdditionalData>();
            extra.Quality = 3;

            PlayModeValueSaver.SaveComponents(new Object[] { light });

            extra.Quality = 0;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(3, extra.Quality);
        }

        [Test]
        public void SavingAnUnrelatedComponent_DoesNotTakeTheCompanionAlong()
        {
            _go.AddComponent<Light>();
            var extra = _go.AddComponent<KeeperTestAdditionalData>();
            var collider = _go.AddComponent<BoxCollider>();
            extra.Quality = 3;
            PlayModeValueSaver.SaveComponents(new Object[] { collider });

            extra.Quality = 5;
            PlayModeValueSaver.Restore();

            Assert.AreEqual(5, extra.Quality, "the companion of another component was not saved");
        }

        [Test]
        public void PrefabInstance_GetsAnOverrideOnlyForWhatChanged()
        {
            var source = new GameObject("PlayModeCoverageTests_Source");
            var sourceLight = source.AddComponent<Light>();
            sourceLight.intensity = 1f;
            sourceLight.range = 10f;
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var light = instance.GetComponent<Light>();
                light.intensity = 5f;
                PlayModeValueSaver.SaveComponents(new Object[] { light });

                light.intensity = 1f;
                PrefabUtility.RevertObjectOverride(light, InteractionMode.AutomatedAction);
                Assert.AreEqual(1f, light.intensity, Tolerance, "precondition: no override left");

                PlayModeValueSaver.Restore();

                Assert.AreEqual(5f, light.intensity, Tolerance, "the saved value must be written");
                var modifications = PrefabUtility.GetPropertyModifications(instance);
                int onLight = 0;
                for (int i = 0; i < modifications.Length; i++)
                {
                    if (modifications[i].target is Light)
                        onLight++;
                }
                Assert.AreEqual(1, onLight,
                                "only the value that differs may become an override; the rest already matched");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void UnchangedComponent_IsNotWrittenTo()
        {
            var light = _go.AddComponent<Light>();
            light.intensity = 2f;
            PlayModeValueSaver.SaveComponents(new Object[] { light });

            EditorUtility.ClearDirty(light);
            PlayModeValueSaver.Restore();

            Assert.IsFalse(EditorUtility.IsDirty(light),
                           "a restore of values that already match must leave the component untouched");
            Assert.IsNull(PlayModeValueSaver.Load(), "and the record is still used up");
        }

        private static Mesh CreateMeshAsset()
        {
            AssetDatabase.CreateAsset(new Mesh(), MeshPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        }
    }
}
