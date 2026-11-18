using System;
using System.IO;
using ProtoHarness.ChainRush;
using ProtoHarness.ChainRush.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProtoHarness.Editor.ChainRush
{
    // Rebuilds the runner model and the five looks around the runner FBX (Art/Models/RunnerFlatFoot.fbx, DECISIONS
    // 2026-11-14 "러너 캐릭터 아트"): each prefab keeps its GUID (the scenes and the runner profiles still point at it)
    // and gets the FBX as its only child, a RunnerVisual on its root and the runner controller on the FBX's Animator.
    // Then each scene's GrappleController is wired to the new chain origin bone. Run it from the menu; it saves the two
    // scenes it changes. It can be run again: the prefabs are rebuilt from scratch each time.
    public static class RunnerVisualBuilder
    {
        private const string ModelFolder = "Assets/_Project/Art/Models/";
        private const string FbxPath = ModelFolder + "RunnerFlatFoot.fbx";
        private const string ControllerPath = "Assets/_Project/Art/Animations/Runner.controller";
        // The scene's runner model, shared by every scene (ChainRushSceneBuilder.RunnerModelPath); it wears ALPHA as saved.
        private const string ModelPath = ChainRushSceneBuilder.RunnerModelPath;
        // The physics capsule is 1.8 m high and centred on the runner's root; the FBX stands on its own origin (the soles).
        private const float FeetBelowRoot = -0.9f;
        private static readonly string[] Runners = { "Alpha", "Beta", "Theta", "Delta", "Omega" };
        private static readonly string[] Scenes =
        {
            "Assets/_Project/Scenes/ChainRushCircuit.unity",
            "Assets/_Project/Scenes/ChainRushTestBench.unity",
        };

        [MenuItem("ProtoHarness/Chain Rush/Build Runner Visuals")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before building the runner visuals.");
            EnsureAvatar();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) throw new InvalidOperationException("The runner FBX is missing: " + FbxPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("The runner controller is missing: " + ControllerPath);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildPrefab(ModelPath, "Alpha", fbx, controller);
            foreach (string runner in Runners) BuildPrefab($"{ModelFolder}Runner{runner}.prefab", runner, fbx, controller);
            AssetDatabase.SaveAssets();
            foreach (string scene in Scenes) ConnectChainOrigin(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("RunnerVisualBuilder: 6 runner prefabs rebuilt around RunnerFlatFoot.fbx, 2 scenes' rope origin connected and saved.");
        }

        // One prefab: the FBX as its only child, its two faces (the runner's closed and open) named on a RunnerVisual on
        // the root, the other eight faces off.
        private static void BuildPrefab(string path, string runner, GameObject fbx, RuntimeAnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException("The runner prefab is missing: " + path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Refuses anything on the root it does not know rather than deleting it unseen.
                foreach (Component component in root.GetComponents<Component>())
                {
                    if (component is Transform) continue;
                    if (component is RunnerVisual) { UnityEngine.Object.DestroyImmediate(component); continue; }
                    throw new InvalidOperationException($"{path}: the root has a {component.GetType().Name}, which this builder does not replace.");
                }
                for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                model.transform.localPosition = new Vector3(0f, FeetBelowRoot, 0f);
                model.transform.localRotation = Quaternion.identity;
                Animator animator = model.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                    animator.avatar = FindAvatar();
                }
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                SkinnedMeshRenderer closed = null, open = null;
                foreach (SkinnedMeshRenderer face in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (face.name == $"Runner{runner}Closed") closed = face;
                    else if (face.name == $"Runner{runner}Open") open = face;
                    face.enabled = false;
                }
                if (closed == null || open == null) throw new InvalidOperationException($"{FbxPath} has no Runner{runner}Closed and Runner{runner}Open.");
                closed.enabled = true;
                Transform chainOrigin = Find(model.transform, "ChainOrigin");
                if (chainOrigin == null) throw new InvalidOperationException($"{FbxPath} has no ChainOrigin bone.");

                var visual = root.AddComponent<RunnerVisual>();
                var data = new SerializedObject(visual);
                data.FindProperty("animator").objectReferenceValue = animator;
                data.FindProperty("chainOrigin").objectReferenceValue = chainOrigin;
                data.FindProperty("closedFace").objectReferenceValue = closed;
                data.FindProperty("openFace").objectReferenceValue = open;
                data.ApplyModifiedPropertiesWithoutUndo();
                if (!visual.TryValidate(out string error)) throw new InvalidOperationException(error);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || saved == null) throw new IOException("Could not save the runner prefab " + path);
                if (AssetDatabase.AssetPathToGUID(path) != guid) throw new InvalidOperationException("Rebuilding changed the runner prefab GUID: " + path);
                Debug.Log($"{saved.name}: {saved.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} faces, {closed.bones.Length} bones, wears {runner}.", saved);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // The scene's grapple hangs its chain from the model's chain origin; the old one went with the old hierarchy.
        private static void ConnectChainOrigin(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                GrappleController grapple = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (!root.TryGetComponent(out GrappleController found)) continue;
                    if (grapple != null) throw new InvalidOperationException($"{scenePath} has more than one GrappleController at its root.");
                    grapple = found;
                }
                if (grapple == null) throw new InvalidOperationException($"{scenePath} has no GrappleController at its root.");
                Transform body = grapple.TryGetComponent(out RunnerTilt tilt) ? tilt.Body : null;
                RunnerVisual visual = body != null ? body.GetComponentInChildren<RunnerVisual>(true) : null;
                if (visual == null) throw new InvalidOperationException($"{scenePath}: the runner's body has no RunnerVisual.");
                var data = new SerializedObject(grapple);
                data.FindProperty("ropeOrigin").objectReferenceValue = visual.ChainOrigin;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scenePath);
                Debug.Log($"{scene.name}: rope origin is {visual.ChainOrigin.name} of {visual.name}.");
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- the lobby's runner tiles ----------------------------------------------------------------------------------

        // The lobby's LobbyMenu.runnerArtwork (ChainRushLobby.unity, GUID dcd2dd54...), which RunnerPage.DrawRow cuts by
        // column into its tiles (NightUi.Portrait). It is a transparent row of five cells, one runner in front each.
        private const string LineupPath = "Assets/_Project/Art/Textures/T_RunnerNightAtlas.png";
        // One cell per runner, the lobby tile's shape (RunnerPage.DrawRow draws 84 x 105 of a cell).
        private const int CellWidth = 320, CellHeight = 400;
        private const float LineupFieldOfView = 22f, LineupDistance = 5.8f, LineupAimHeight = 0.98f, LineupYaw = 25f;

        // Draws each runner's look (the same prefabs, materials and shaders the race uses) in its Idle pose, a little from
        // the side, on a transparent background, into one row of five cells and saves it over T_RunnerNightAtlas.png (its
        // import settings stay). Run it again after a look changes.
        [MenuItem("ProtoHarness/Chain Rush/Rebuild Runner Lobby Portraits")]
        public static void RebuildLineup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before rebuilding the runner portraits.");
            string file = Path.GetFullPath(LineupPath);
            if (!File.Exists(file)) throw new InvalidOperationException("The lobby's runner atlas is missing: " + LineupPath);
            var background = new Color(0f, 0f, 0f, 0f);

            var atlas = new Texture2D(CellWidth * Runners.Length, CellHeight, TextureFormat.RGBA32, false);
            Scene preview = EditorSceneManager.NewPreviewScene();
            var target = new RenderTexture(CellWidth, CellHeight, 24) { antiAliasing = 8 };
            Camera camera = null;
            try
            {
                var cameraObject = new GameObject("Lineup camera");
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                camera = cameraObject.AddComponent<Camera>();
                camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.fieldOfView = LineupFieldOfView;
                camera.nearClipPlane = 0.5f;
                camera.farClipPlane = 20f;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0f, LineupAimHeight + 0.35f, LineupDistance);
                camera.transform.LookAt(new Vector3(0f, LineupAimHeight, 0f));
                AddLight(preview, "Key", Quaternion.Euler(40f, -25f, 0f), 1.3f);
                AddLight(preview, "Fill", Quaternion.Euler(20f, 150f, 0f), 0.5f);

                for (int i = 0; i < Runners.Length; i++)
                {
                    string path = $"{ModelFolder}Runner{Runners[i]}.prefab";
                    var look = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (look == null) throw new InvalidOperationException("The runner prefab is missing: " + path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(look, preview);
                    try
                    {
                        RunnerVisual visual = instance.GetComponentInChildren<RunnerVisual>(true);
                        if (visual == null || visual.Animator == null || visual.Animator.runtimeAnimatorController == null)
                            throw new InvalidOperationException($"{path} has no RunnerVisual with an Animator and a controller.");
                        AnimationClip idle = null;
                        foreach (AnimationClip clip in visual.Animator.runtimeAnimatorController.animationClips)
                            if (clip.name == "Runner_Idle") idle = clip;
                        if (idle == null) throw new InvalidOperationException($"{path}: the controller has no Runner_Idle clip.");
                        idle.SampleAnimation(visual.Animator.gameObject, 0f);
                        visual.Select(false);
                        // The prefab's origin is the capsule's centre and the FBX stands FeetBelowRoot under it: lifted so the
                        // soles are at 0, where the camera is aimed.
                        instance.transform.SetPositionAndRotation(new Vector3(0f, -FeetBelowRoot, 0f), Quaternion.Euler(0f, LineupYaw, 0f));
                        // The first render of a preview scene comes out without its shadows, so it is drawn twice.
                        camera.Render();
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        atlas.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), i * CellWidth, 0);
                        RenderTexture.active = previous;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }
                atlas.Apply();
                File.WriteAllBytes(file, atlas.EncodeToPNG());
            }
            finally
            {
                // The camera still holds the texture until the preview scene closes; Unity refuses to release it then.
                if (camera != null) camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(atlas);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            AssetDatabase.ImportAsset(LineupPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"RunnerVisualBuilder: {LineupPath} redrawn, {Runners.Length} runners, {CellWidth * Runners.Length} x {CellHeight}.");
        }

        private static void AddLight(Scene scene, string name, Quaternion rotation, float intensity)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
        }

        // A Generic rig plays its clips through an Avatar, which the importer only makes when asked: it came in as
        // NoAvatar (found 2026-11-16 by the first run of this builder), so the FBX had no Animator either.
        private static void EnsureAvatar()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("The runner FBX has no model importer: " + FbxPath);
            if (importer.animationType != ModelImporterAnimationType.Generic)
                throw new InvalidOperationException($"{FbxPath} is {importer.animationType}, not Generic.");
            if (importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel) return;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            Debug.Log($"RunnerVisualBuilder: {FbxPath} now makes its own Avatar (was {ModelImporterAvatarSetup.NoAvatar}).");
        }

        private static Avatar FindAvatar()
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
                if (asset is Avatar avatar) return avatar;
            throw new InvalidOperationException($"{FbxPath} has no Avatar: its rig is not Generic.");
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
