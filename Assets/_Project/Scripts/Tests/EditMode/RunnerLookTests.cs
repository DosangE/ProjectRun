using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProtoHarness.ChainRush.Visuals;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProtoHarness.Tests.EditMode
{
    // A runner's look (RunnerLook): a model on the same rig lends its two faces (mesh and materials) to the race's runner
    // model, which keeps its own bones; a look whose bones do not match is refused before the model changes.
    public sealed class RunnerLookTests
    {
        private static readonly string[] BoneNames = { "Root", "Pelvis" };
        private readonly List<Object> made = new List<Object>();
        private Shader shader;

        [SetUp]
        public void SetUp()
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "URP Lit shader is required for the test materials.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object thing in made)
                if (thing != null) Object.DestroyImmediate(thing);
            made.Clear();
        }

        [Test]
        public void Apply_MatchingLook_LendsBothFacesAndShowsTheClosedOne()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            Transform[] closedBones = model.ClosedFace.bones, openBones = model.OpenFace.bones;
            Vector3[] positions = LocalPositions(model.transform);
            RunnerLook.Apply(model, look, false);
            Assert.That(model.ClosedFace.sharedMesh, Is.SameAs(look.ClosedFace.sharedMesh));
            Assert.That(model.ClosedFace.sharedMaterials, Is.EqualTo(look.ClosedFace.sharedMaterials));
            Assert.That(model.ClosedFace.sharedMaterials.Length, Is.EqualTo(3), "Every material of the closed face is lent.");
            Assert.That(model.OpenFace.sharedMesh, Is.SameAs(look.OpenFace.sharedMesh));
            Assert.That(model.OpenFace.sharedMaterials, Is.EqualTo(look.OpenFace.sharedMaterials));
            Assert.That(model.ClosedFace.bones, Is.EqualTo(closedBones), "The model keeps its own bones.");
            Assert.That(model.OpenFace.bones, Is.EqualTo(openBones));
            Assert.That(LocalPositions(model.transform), Is.EqualTo(positions), "The model's transforms stay as they were.");
            Assert.That(model.name, Is.EqualTo("Armored Runner"), "Only meshes and materials change.");
            Assert.That(model.ClosedFace.enabled, Is.True);
            Assert.That(model.OpenFace.enabled, Is.False);
            Assert.That(model.OpenFaceSelected, Is.False);
        }

        [Test]
        public void Apply_OpenFace_ShowsTheGogglesOnly()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            RunnerLook.Apply(model, look, true);
            Assert.That(model.ClosedFace.enabled, Is.False);
            Assert.That(model.OpenFace.enabled, Is.True);
            Assert.That(model.OpenFaceSelected, Is.True);
            Assert.That(model.SelectedFace, Is.SameAs(model.OpenFace));
        }

        [Test]
        public void SetFaceShown_HidesAndShowsOnlyTheSelectedFace()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            model.Select(true);
            model.SetFaceShown(false);
            Assert.That(model.OpenFace.enabled, Is.False);
            Assert.That(model.ClosedFace.enabled, Is.False, "The other face never comes on.");
            model.SetFaceShown(true);
            Assert.That(model.OpenFace.enabled, Is.True);
            Assert.That(model.ClosedFace.enabled, Is.False);
        }

        [Test]
        public void Apply_BoneNameDiffers_ThrowsAndLeavesTheModel()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            look.ClosedFace.bones[1].name = "Hips";
            AssertRefused(model, look, "closed face bone 1");
        }

        [Test]
        public void Apply_BoneCountDiffers_ThrowsAndLeavesTheModel()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            look.OpenFace.bones = new[] { look.OpenFace.bones[0] };
            AssertRefused(model, look, "open face");
        }

        [Test]
        public void Apply_MaterialMissing_ThrowsAndLeavesTheModel()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            // The open face is checked after the closed one, which fits, and still nothing changes.
            look.OpenFace.sharedMaterials = new Material[] { look.OpenFace.sharedMaterials[0], null };
            AssertRefused(model, look, "open face material 1");
        }

        [Test]
        public void Apply_Null_Throws()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            Assert.Throws<ArgumentNullException>(() => RunnerLook.Apply(null, model, false));
            Assert.Throws<ArgumentNullException>(() => RunnerLook.Apply(model, null, false));
        }

        [Test]
        public void Apply_ModelWithoutAnAnimator_Throws()
        {
            RunnerVisual model = Build("Armored Runner", "model", 0f);
            RunnerVisual look = Build("RunnerBeta", "look", 10f);
            Set(model, "animator", null);
            var error = Assert.Throws<ArgumentException>(() => RunnerLook.Apply(model, look, false));
            Assert.That(error.Message, Does.Contain("animator"));
        }

        // ---- helpers -------------------------------------------------------------------------------

        // The look is refused with its name and the place in the message, and the model keeps every mesh, material and
        // state it had.
        private static void AssertRefused(RunnerVisual model, RunnerVisual look, string where)
        {
            Mesh closedMesh = model.ClosedFace.sharedMesh, openMesh = model.OpenFace.sharedMesh;
            Material[] closedMaterials = model.ClosedFace.sharedMaterials, openMaterials = model.OpenFace.sharedMaterials;
            bool closedOn = model.ClosedFace.enabled, openOn = model.OpenFace.enabled, selectedOpen = model.OpenFaceSelected;
            var error = Assert.Throws<ArgumentException>(() => RunnerLook.Apply(model, look, true));
            Assert.That(error.Message, Does.Contain(look.name));
            Assert.That(error.Message, Does.Contain("'" + where + "'"));
            Assert.That(model.ClosedFace.sharedMesh, Is.SameAs(closedMesh));
            Assert.That(model.OpenFace.sharedMesh, Is.SameAs(openMesh));
            Assert.That(model.ClosedFace.sharedMaterials, Is.EqualTo(closedMaterials));
            Assert.That(model.OpenFace.sharedMaterials, Is.EqualTo(openMaterials));
            Assert.That(model.ClosedFace.enabled, Is.EqualTo(closedOn));
            Assert.That(model.OpenFace.enabled, Is.EqualTo(openOn));
            Assert.That(model.OpenFaceSelected, Is.EqualTo(selectedOpen));
        }

        // root (RunnerVisual)
        //   Rig (Animator)
        //     Root, Pelvis, ChainOrigin (bones, in that order for the two skinned ones)
        //   Closed face (skinned to Root and Pelvis, three materials)
        //   Open face (skinned to Root and Pelvis, two materials)
        // Every face has its own mesh and materials, named after tag; offset moves the bones so transforms differ.
        private RunnerVisual Build(string rootName, string tag, float offset)
        {
            Transform root = Track(new GameObject(rootName)).transform;
            Transform rig = Track(new GameObject("Rig")).transform;
            rig.SetParent(root, false);
            var animator = rig.gameObject.AddComponent<Animator>();
            var bones = new Transform[BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = Track(new GameObject(BoneNames[i])).transform;
                bones[i].SetParent(rig, false);
                bones[i].localPosition = new Vector3(offset + i, offset, i * 0.5f);
            }
            Transform chain = Track(new GameObject("ChainOrigin")).transform;
            chain.SetParent(bones[1], false);
            SkinnedMeshRenderer closed = Face("Closed face", root, bones, tag, 3);
            SkinnedMeshRenderer open = Face("Open face", root, bones, tag, 2);
            var visual = root.gameObject.AddComponent<RunnerVisual>();
            Set(visual, "animator", animator);
            Set(visual, "chainOrigin", chain);
            Set(visual, "closedFace", closed);
            Set(visual, "openFace", open);
            closed.enabled = true;
            open.enabled = false;
            Assert.That(visual.TryValidate(out string error), Is.True, error);
            return visual;
        }

        private SkinnedMeshRenderer Face(string name, Transform parent, Transform[] bones, string tag, int materialCount)
        {
            var face = Track(new GameObject(name)).transform;
            face.SetParent(parent, false);
            var mesh = Track(new Mesh { name = tag + " " + name });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var poses = new Matrix4x4[bones.Length];
            for (int i = 0; i < poses.Length; i++) poses[i] = Matrix4x4.identity;
            mesh.bindposes = poses;
            var renderer = face.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = (Transform[])bones.Clone();
            renderer.rootBone = bones[0];
            var materials = new Material[materialCount];
            for (int i = 0; i < materialCount; i++) materials[i] = Track(new Material(shader) { name = tag + " " + name + " " + i });
            renderer.sharedMaterials = materials;
            return renderer;
        }

        // The private serialized fields RunnerVisual is wired with in the editor.
        private static void Set(RunnerVisual visual, string field, Object value)
        {
            var so = new SerializedObject(visual);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3[] LocalPositions(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[all.Length];
            for (int i = 0; i < all.Length; i++) positions[i] = all[i].localPosition;
            return positions;
        }

        private T Track<T>(T thing) where T : Object
        {
            made.Add(thing);
            return thing;
        }
    }
}
