using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProtoHarness.ChainRush.Flow;
using ProtoHarness.ChainRush.Flow.Lobby;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProtoHarness.Tests.EditMode
{
    // The lobby's runner preview (RunnerPreview): one copy per look on a turntable, only the shown one active, centred on
    // the turntable's axis; a drag turns it and the angle stays when another runner is shown; a runner without a look
    // shows nothing; releasing destroys what it made.
    public sealed class RunnerPreviewTests
    {
        private static readonly Color Background = new Color(0.026f, 0.056f, 0.081f, 1f);
        private readonly List<Object> made = new List<Object>();
        private RunnerPreview preview;

        [TearDown]
        public void TearDown()
        {
            preview?.Release();
            preview = null;
            foreach (Object thing in made)
                if (thing != null) Object.DestroyImmediate(thing);
            made.Clear();
        }

        [Test]
        public void Show_ARunner_OnlyItsCopyIsVisibleAndTheCameraDraws()
        {
            preview = new RunnerPreview(new[] { Look("A", Vector3.zero), Look("B", Vector3.zero), Look("C", Vector3.zero) }, 64, 96, Background);
            Assert.That(preview.Shown, Is.EqualTo(-1));
            Assert.That(preview.Rendering, Is.False, "Nothing is drawn before Show.");
            for (int i = 0; i < 3; i++) Assert.That(preview.IsVisible(i), Is.False, "runner " + i);

            preview.Show(1);
            Assert.That(preview.Shown, Is.EqualTo(1));
            Assert.That(preview.Rendering, Is.True);
            Assert.That(new[] { preview.IsVisible(0), preview.IsVisible(1), preview.IsVisible(2) }, Is.EqualTo(new[] { false, true, false }));

            preview.Show(2);
            Assert.That(new[] { preview.IsVisible(0), preview.IsVisible(1), preview.IsVisible(2) }, Is.EqualTo(new[] { false, false, true }));

            preview.Hide();
            Assert.That(preview.Rendering, Is.False);
            Assert.That(preview.IsVisible(2), Is.True, "The runner stays on the turntable for the next Show.");
            Assert.That(preview.Texture.width, Is.EqualTo(64));
            Assert.That(preview.Texture.height, Is.EqualTo(96));
        }

        [Test]
        public void Turn_DragRight_TurnsByDegreesPerPixelWrapsAndKeepsTheAngleAcrossRunners()
        {
            preview = new RunnerPreview(new[] { Look("A", Vector3.zero), Look("B", Vector3.zero) }, 64, 96, Background);
            Assert.That(preview.Yaw, Is.EqualTo(Mathf.Repeat(RunnerPreview.StartYaw, 360f)).Within(1e-4f));
            preview.Show(0);
            preview.Turn(100f);
            float expected = Mathf.Repeat(RunnerPreview.StartYaw - 100f * RunnerPreview.DegreesPerPixel, 360f);
            Assert.That(preview.Yaw, Is.EqualTo(expected).Within(1e-3f));
            preview.Show(1);
            Assert.That(preview.Yaw, Is.EqualTo(expected).Within(1e-3f), "Showing another runner keeps the angle.");
            preview.Turn(-360f / RunnerPreview.DegreesPerPixel);
            Assert.That(preview.Yaw, Is.EqualTo(expected).Within(1e-2f), "A whole turn comes back to the same angle.");
            Assert.That(preview.Yaw, Is.GreaterThanOrEqualTo(0f).And.LessThan(360f));
        }

        [Test]
        public void New_LookOffItsOrigin_TheCopyIsCentredOnTheTurntableAxis()
        {
            preview = new RunnerPreview(new[] { Look("Off", new Vector3(5f, 1f, -3f)) }, 64, 96, Background);
            preview.Show(0);
            Vector3 axis = Centre(0);
            for (int turn = 1; turn <= 3; turn++)
            {
                preview.Turn(90f / RunnerPreview.DegreesPerPixel);
                Vector3 centre = Centre(turn);
                Assert.That(centre.x, Is.EqualTo(axis.x).Within(1e-3f), "turn " + turn);
                Assert.That(centre.z, Is.EqualTo(axis.z).Within(1e-3f), "turn " + turn);
            }
        }

        // The runner FBX carries every runner's faces and switches the other nine off: a renderer that is off is not drawn,
        // so it must not push the camera back (2026-11-16: it made the runners a third smaller than they needed to be).
        [Test]
        public void New_LooksOfDifferentHeights_AlignTheirFeetOnTheSameFloor()
        {
            GameObject shortLook = LookWithSize("Short", new Vector3(0f, 0.9f, 0f), new Vector3(1f, 1.8f, 0.5f));
            GameObject tallLook = LookWithSize("Tall", new Vector3(0f, 1.15f, 0f), new Vector3(1f, 2.3f, 0.5f));
            preview = new RunnerPreview(new[] { shortLook, tallLook }, 64, 96, Background);

            preview.Show(0);
            Bounds shortBounds = ShownBounds();
            preview.Show(1);
            Bounds tallBounds = ShownBounds();

            Assert.That(shortBounds.min.y, Is.EqualTo(tallBounds.min.y).Within(1e-3f), "Feet sit on the same floor regardless of antenna or helmet height.");
            Assert.That(shortBounds.center.x, Is.EqualTo(tallBounds.center.x).Within(1e-3f));
            Assert.That(shortBounds.center.z, Is.EqualTo(tallBounds.center.z).Within(1e-3f));
            Assert.That(tallBounds.max.y, Is.GreaterThan(shortBounds.max.y + 0.4f));
        }

        [Test]
        public void LobbyView_EvaluateStripeScale_ContinuesFromInterruptedScale()
        {
            Assert.That(LobbyView.EvaluateStripeScale(1.06f, 0f), Is.EqualTo(1.06f).Within(1e-5f));
            float midRise = LobbyView.EvaluateStripeScale(1.06f, 0.06f);
            Assert.That(midRise, Is.GreaterThan(1.06f).And.LessThan(1.08f));
            Assert.That(LobbyView.EvaluateStripeScale(1.06f, 0.12f), Is.EqualTo(1.08f).Within(1e-4f));
            Assert.That(LobbyView.EvaluateStripeScale(1.06f, 0.35f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void New_ARendererThatIsOff_DoesNotPushTheCameraBack()
        {
            preview = new RunnerPreview(new[] { Look("Plain", Vector3.zero) }, 64, 96, Background);
            float plain = CameraDistance();
            preview.Release();

            GameObject withSpare = Look("WithSpare", Vector3.zero);
            GameObject spare = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spare.transform.SetParent(withSpare.transform, false);
            spare.transform.localScale = new Vector3(8f, 8f, 8f);
            spare.GetComponent<Renderer>().enabled = false;
            preview = new RunnerPreview(new[] { withSpare }, 64, 96, Background);
            Assert.That(CameraDistance(), Is.EqualTo(plain).Within(1e-3f), "A renderer that is switched off is not measured.");
        }

        [Test]
        public void New_RunnerWithoutALook_ShowsNothingForIt()
        {
            preview = new RunnerPreview(new[] { Look("A", Vector3.zero), null }, 64, 96, Background);
            Assert.That(preview.HasModel(0), Is.True);
            Assert.That(preview.HasModel(1), Is.False);
            preview.Show(0);
            preview.Show(1);
            Assert.That(preview.IsVisible(0), Is.False, "The previous runner is taken off.");
            Assert.That(preview.IsVisible(1), Is.False);
            Assert.That(preview.Shown, Is.EqualTo(1));
        }

        [Test]
        public void New_BadArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new RunnerPreview(null, 64, 96, Background));
            Assert.Throws<ArgumentException>(() => new RunnerPreview(new GameObject[0], 0, 96, Background));
            GameObject empty = Track(new GameObject("Empty look"));
            var error = Assert.Throws<ArgumentException>(() => new RunnerPreview(new[] { Look("A", Vector3.zero), empty }, 64, 96, Background));
            Assert.That(error.Message, Does.Contain("Empty look"));
            Assert.That(GameObject.Find("Runner preview"), Is.Null, "A refused preview leaves no stage behind.");

            preview = new RunnerPreview(new[] { Look("A", Vector3.zero) }, 64, 96, Background);
            Assert.Throws<ArgumentOutOfRangeException>(() => preview.Show(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => preview.Show(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => preview.HasModel(1));
        }

        [Test]
        public void Release_DestroysTheStageAndTheTexture()
        {
            preview = new RunnerPreview(new[] { Look("A", Vector3.zero) }, 64, 96, Background);
            Assert.That(GameObject.Find("Runner preview"), Is.Not.Null);
            preview.Release();
            Assert.That(GameObject.Find("Runner preview"), Is.Null);
            Assert.That(preview.Texture == null, Is.True, "The texture is destroyed.");
            preview.Release();
            preview = null;
        }

        // ---- helpers -------------------------------------------------------------------------------

        // A look whose only part, a cube, sits at offset from the look's root.
        private GameObject Look(string name, Vector3 offset) => LookWithSize(name, offset, new Vector3(1f, 2f, 0.5f));

        private GameObject LookWithSize(string name, Vector3 offset, Vector3 scale)
        {
            GameObject root = Track(new GameObject(name));
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = offset;
            cube.transform.localScale = scale;
            return root;
        }

        private Bounds ShownBounds()
        {
            GameObject stage = GameObject.Find("Runner preview");
            Assert.That(stage, Is.Not.Null);
            Renderer[] renderers = stage.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.EqualTo(1), "Only the shown copy is active.");
            return renderers[0].bounds;
        }

        // How far the preview camera stands from the turntable's axis.
        private static float CameraDistance()
        {
            GameObject camera = GameObject.Find("Preview camera");
            Assert.That(camera, Is.Not.Null);
            return camera.transform.localPosition.magnitude;
        }

        // The world centre of the shown copy's renderers.
        private Vector3 Centre(int turn)
        {
            GameObject stage = GameObject.Find("Runner preview");
            Assert.That(stage, Is.Not.Null, "turn " + turn);
            Renderer[] renderers = stage.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.EqualTo(1), "Only the shown copy is active.");
            return renderers[0].bounds.center;
        }

        private T Track<T>(T thing) where T : Object
        {
            made.Add(thing);
            return thing;
        }
    }
}
