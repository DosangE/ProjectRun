using UnityEngine;

namespace ProtoHarness.ChainRush.Visuals
{
    // Presentation only: the runner's skinned model, one instance of Art/Models/RunnerFlatFoot.fbx. The FBX carries
    // every runner's two faces (closed visor, open goggles) on one shared rig; this names the two that can show, the
    // Animator that poses the rig and the rig's chain origin (the hand the chain leaves from). Nothing here reads or
    // writes the simulation, so the root and the physics capsule cannot change.
    // RunnerLook.Apply lends a look's two faces (mesh and materials) to the race's model and picks which one shows.
    public sealed class RunnerVisual : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Transform chainOrigin;
        [Tooltip("The face with the visor. Its bones are this rig's.")]
        [SerializeField] private SkinnedMeshRenderer closedFace;
        [Tooltip("The face with the goggles. Its bones are this rig's.")]
        [SerializeField] private SkinnedMeshRenderer openFace;
        private bool showOpenFace;

        public Animator Animator => animator;
        public Transform ChainOrigin => chainOrigin;
        public SkinnedMeshRenderer ClosedFace => closedFace;
        public SkinnedMeshRenderer OpenFace => openFace;
        public bool OpenFaceSelected => showOpenFace;
        // The face that is selected, which the respawn blink turns off and on (SetFaceShown).
        public SkinnedMeshRenderer SelectedFace => showOpenFace ? openFace : closedFace;

        private void Awake()
        {
            if (!TryValidate(out string error))
            {
                Debug.LogError(error, this);
                enabled = false;
                return;
            }
            // The FBX has ten faces; only the two named above ever show, whichever one RunnerLook selects.
            SkinnedMeshRenderer[] all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != closedFace && all[i] != openFace) all[i].enabled = false;
            Select(showOpenFace);
        }

        // A reference that is still empty is Awake's to report (it switches this off); here only references that are all
        // set but do not fit show up while the model is edited.
        private void OnValidate()
        {
            if (animator == null || chainOrigin == null || closedFace == null || openFace == null) return;
            if (!TryValidate(out string error)) Debug.LogError(error, this);
        }

        // Whether every reference is set and fits: the faces are two different renderers of this model with a mesh,
        // the Animator and the chain origin belong to it too.
        public bool TryValidate(out string error)
        {
            error = null;
            if (animator == null || chainOrigin == null || closedFace == null || openFace == null)
                error = $"RunnerVisual '{name}': animator, chain origin, closed face and open face must be assigned.";
            else if (closedFace == openFace)
                error = $"RunnerVisual '{name}': the closed face and the open face must be two different renderers.";
            else if (!animator.transform.IsChildOf(transform) || !chainOrigin.IsChildOf(transform)
                || !closedFace.transform.IsChildOf(transform) || !openFace.transform.IsChildOf(transform))
                error = $"RunnerVisual '{name}': the animator, chain origin and faces must be this model's own children.";
            else if (closedFace.sharedMesh == null || openFace.sharedMesh == null)
                error = $"RunnerVisual '{name}': both faces need a mesh.";
            return error == null;
        }

        // Shows one face and hides the other. Needs no Awake first (RunnerLook can run before it): it only sets
        // renderer states the references name.
        public void Select(bool open)
        {
            showOpenFace = open;
            closedFace.enabled = !open;
            openFace.enabled = open;
        }

        // The respawn blink: the selected face only, so a hidden face never comes back on.
        public void SetFaceShown(bool shown) => SelectedFace.enabled = shown;
    }
}
