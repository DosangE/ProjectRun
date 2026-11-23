using System;
using ProtoHarness.ChainRush.Visuals;
using UnityEngine;

namespace ProtoHarness.ChainRush.Flow
{
    // Presentation only: the lobby's runner preview (user 2026-10-20). A stage made here, far from anything else, holds
    // a copy of every runner's look (RunnerProfile.Look) on a turntable, a camera that draws the shown one into Texture
    // and a light of its own (the lobby scene has none), so the scene needs no edit. The copies are made once; showing
    // another runner only switches which is active, and turning only rotates the turntable.
    // The lobby draws Texture over its OnGUI background, which covers the main camera, so the stage needs no layer.
    public sealed class RunnerPreview
    {
        // A drag across the 1600-wide lobby turns the runner about two turns.
        public const float DegreesPerPixel = 0.45f;
        // Seen from the front and a little from the side (the model faces +Z, the camera looks back along -Z).
        public const float StartYaw = -30f;
        private static readonly Vector3 StagePosition = new Vector3(0f, -1000f, 0f);
        private const float FieldOfView = 30f;
        // Room around the tallest look (user 2026-11-16: "러너 크기를 좀 더 키워줘"), and how far the camera looks down on it.
        private const float Margin = 1.1f;
        private const float Pitch = 8f;
        // The pose a skinned look stands in: the first frame of its Idle clip (the runner controller's).
        private const string IdleClip = "Runner_Idle";

        private readonly GameObject stage;
        private readonly Transform turntable;
        private readonly GameObject[] models;
        private readonly Camera camera;
        private readonly RenderTexture texture;
        private int shown = -1;
        private float yaw = StartYaw;

        // looks: per runner, its look or null (that runner then shows nothing). The texture is width x height, cleared
        // to background.
        public RunnerPreview(GameObject[] looks, int width, int height, Color background)
        {
            if (looks == null) throw new ArgumentNullException(nameof(looks));
            if (width <= 0 || height <= 0) throw new ArgumentException($"RunnerPreview: the texture must be positive, not {width} x {height}.");
            stage = new GameObject("Runner preview");
            stage.transform.position = StagePosition;
            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(stage.transform, false);

            // Each copy is centred on the turntable's axis (XZ) and stands with its feet (bounds.min.y) on a shared
            // floor at -halfHeight, so taller helmets or antennas do not push a runner's feet below the others.
            models = new GameObject[looks.Length];
            var measured = new Bounds[looks.Length];
            float maxHeight = 0f, ring = 0f;
            for (int i = 0; i < looks.Length; i++)
            {
                if (looks[i] == null) continue;
                GameObject model = UnityEngine.Object.Instantiate(looks[i], turntable, false);
                model.name = looks[i].name;
                model.SetActive(true);
                try
                {
                    Stand(model);
                }
                catch (ArgumentException)
                {
                    Release();
                    throw;
                }
                if (!TryMeasure(model, out Bounds bounds))
                {
                    Release();
                    throw new ArgumentException($"RunnerPreview: the look '{looks[i].name}' has nothing to draw.", nameof(looks));
                }
                measured[i] = bounds;
                maxHeight = Mathf.Max(maxHeight, bounds.size.y);
                ring = Mathf.Max(ring, new Vector2(bounds.extents.x, bounds.extents.z).magnitude);
                model.SetActive(false);
                models[i] = model;
            }
            float halfHeight = maxHeight * 0.5f;
            float floorY = turntable.position.y - halfHeight;
            float radius = 0f;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null) continue;
                Bounds bounds = measured[i];
                Vector3 shift = new Vector3(
                    turntable.position.x - bounds.center.x,
                    floorY - bounds.min.y,
                    turntable.position.z - bounds.center.z);
                models[i].transform.localPosition += turntable.InverseTransformVector(shift);
                radius = Mathf.Max(radius, new Vector3(bounds.extents.x, halfHeight, bounds.extents.z).magnitude);
            }

            texture = new RenderTexture(width, height, 24) { name = "Runner preview", antiAliasing = 4 };
            texture.Create();
            camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.fieldOfView = FieldOfView;
            camera.targetTexture = texture;
            camera.enabled = false;
            // Far enough that the tallest look fits the view's height and the widest turn (its ring on the turntable) its
            // width, plus half that ring so the near side, which looks larger, is not cut. The old fit took the bounding
            // sphere of every look, which left a runner a third of the view empty above and below.
            float tanVertical = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHorizontal = tanVertical * width / height;
            float fit = Mathf.Max(halfHeight / tanVertical, ring / tanHorizontal) * Margin;
            float distance = Mathf.Max(fit + ring * 0.5f, 0.1f);
            camera.transform.localRotation = Quaternion.Euler(Pitch, 180f, 0f);
            camera.transform.localPosition = -(camera.transform.localRotation * Vector3.forward) * distance;
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 2f;

            var light = new GameObject("Preview light").AddComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional;
            light.transform.localRotation = Quaternion.Euler(35f, 160f, 0f);
            light.intensity = 1.2f;

            Turn(0f);
        }

        // What the camera draws (the shown runner), for the lobby to put on screen.
        public Texture Texture => texture;
        // The runner shown, or -1 before Show.
        public int Shown => shown;
        // The turntable's angle in degrees, in [0, 360).
        public float Yaw => yaw;
        // Whether the camera is drawing (between Show and Hide).
        public bool Rendering => camera.enabled;
        public bool HasModel(int runner) => models[Check(runner)] != null;
        // Whether that runner's copy is the one on the turntable.
        public bool IsVisible(int runner) => models[Check(runner)] != null && models[runner].activeSelf;

        // Puts that runner on the turntable (keeping the angle) and starts drawing.
        public void Show(int runner)
        {
            Check(runner);
            if (shown >= 0 && models[shown] != null) models[shown].SetActive(false);
            shown = runner;
            if (models[shown] != null) models[shown].SetActive(true);
            camera.enabled = true;
        }

        // Stops drawing; the runner stays on the turntable for the next Show.
        public void Hide() => camera.enabled = false;

        // A horizontal drag of pixels (lobby units, 1600 wide): dragging right turns the runner's front to the right.
        public void Turn(float pixels)
        {
            yaw = Mathf.Repeat(yaw - pixels * DegreesPerPixel, 360f);
            turntable.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Destroys the stage and the texture. Safe to call again.
        public void Release()
        {
            if (camera != null) camera.targetTexture = null;
            if (texture != null) texture.Release();
            DestroyNow(texture);
            DestroyNow(stage);
        }

        private int Check(int runner)
        {
            if (runner < 0 || runner >= models.Length)
                throw new ArgumentOutOfRangeException(nameof(runner), runner, $"RunnerPreview: there are {models.Length} runners.");
            return runner;
        }

        // A skinned model (RunnerVisual) stands in the first frame of its Idle clip and stays there: measuring and drawing it
        // need a pose, not an animation, and the controller's own start is the rig's import pose. A model without a
        // RunnerVisual is drawn as it is.
        private static void Stand(GameObject model)
        {
            RunnerVisual visual = model.GetComponentInChildren<RunnerVisual>(true);
            if (visual == null) return;
            Animator animator = visual.Animator;
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new ArgumentException($"RunnerPreview: the look '{model.name}' has no Animator with a controller to pose it.", nameof(model));
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name != IdleClip) continue;
                clip.SampleAnimation(animator.gameObject, 0f);
                animator.enabled = false;
                return;
            }
            throw new ArgumentException($"RunnerPreview: the look '{model.name}' has no {IdleClip} clip to pose it.", nameof(model));
        }

        // The bounds of what model draws, false when it draws nothing. A renderer that is switched off is not drawn (a
        // runner's other faces), so it is not measured; a skinned one is measured on its posed vertices, because its own
        // bounds cover every clip.
        private static bool TryMeasure(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Mesh baked = null;
            try
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    Bounds part = renderer.bounds;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (baked == null) baked = new Mesh();
                        skinned.BakeMesh(baked, true);
                        Vector3[] vertices = baked.vertices;
                        if (vertices.Length == 0) continue;
                        part = new Bounds(skinned.transform.TransformPoint(vertices[0]), Vector3.zero);
                        for (int i = 1; i < vertices.Length; i++) part.Encapsulate(skinned.transform.TransformPoint(vertices[i]));
                    }
                    if (!any) bounds = part;
                    else bounds.Encapsulate(part);
                    any = true;
                }
            }
            finally
            {
                DestroyNow(baked);
            }
            return any;
        }

        // The lobby releases the preview while playing; an EditMode test cannot wait for Destroy.
        private static void DestroyNow(UnityEngine.Object thing)
        {
            if (thing == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
            else UnityEngine.Object.DestroyImmediate(thing);
        }
    }
}
