using System;
using UnityEngine;

namespace ProtoHarness.ChainRush.Visuals
{
    // Presentation only: dresses the race's runner model (RunnerTilt.Body's RunnerVisual) in a runner's look
    // (RunnerProfile.Look, Art/Models/Runner*.prefab, also a RunnerVisual). Both are instances of the one runner FBX, so
    // they share a rig: a look's face is lent to the model's face by copying its mesh and materials onto the model's own
    // skinned renderer, whose bones stay the model's. The bones are checked by name and order first, because a mesh is
    // skinned to its bones by index. The model's rig, root, capsule and chain origin are never touched, so the
    // simulation cannot change.
    public static class RunnerLook
    {
        // Checks the whole pair first and throws before anything is changed, so a look that does not fit leaves the
        // model exactly as it was. openFace picks which of the two faces shows: false the closed one (visor), true the
        // open one (goggles).
        public static void Apply(RunnerVisual model, RunnerVisual look, bool openFace)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (look == null) throw new ArgumentNullException(nameof(look));
            if (!model.TryValidate(out string modelError)) throw new ArgumentException(modelError, nameof(model));
            if (!look.TryValidate(out string lookError)) throw new ArgumentException(lookError, nameof(look));
            Check(look.name, "closed face", model.ClosedFace, look.ClosedFace);
            Check(look.name, "open face", model.OpenFace, look.OpenFace);
            Lend(model.ClosedFace, look.ClosedFace);
            Lend(model.OpenFace, look.OpenFace);
            model.Select(openFace);
        }

        private static void Lend(SkinnedMeshRenderer to, SkinnedMeshRenderer from)
        {
            to.sharedMesh = from.sharedMesh;
            to.sharedMaterials = from.sharedMaterials;
        }

        // The look's mesh must fit the model's bones: the same count, the same names in the same order, and one
        // bind pose for each. Its materials must all be set.
        private static void Check(string lookName, string face, SkinnedMeshRenderer to, SkinnedMeshRenderer from)
        {
            Transform[] toBones = to.bones, fromBones = from.bones;
            if (toBones.Length != fromBones.Length)
                throw Mismatch(lookName, face, $"the model's face has {toBones.Length} bones, the look's {fromBones.Length}");
            for (int i = 0; i < toBones.Length; i++)
            {
                string toName = toBones[i] != null ? toBones[i].name : "(none)";
                string fromName = fromBones[i] != null ? fromBones[i].name : "(none)";
                if (toName != fromName)
                    throw Mismatch(lookName, $"{face} bone {i}", $"it is '{toName}' in the model, '{fromName}' in the look");
            }
            if (from.sharedMesh.bindposes.Length != fromBones.Length)
                throw Mismatch(lookName, face, $"the look's mesh has {from.sharedMesh.bindposes.Length} bind poses for {fromBones.Length} bones");
            Material[] materials = from.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == null) throw Mismatch(lookName, $"{face} material {i}", "the look's material is not set");
        }

        private static ArgumentException Mismatch(string lookName, string where, string what)
            => new ArgumentException($"RunnerLook: the look '{lookName}' does not fit the model at '{where}': {what}.", "look");
    }
}
