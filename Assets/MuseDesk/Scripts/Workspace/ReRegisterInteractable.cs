using System.Collections;
using UnityEngine;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// Forces Oculus interaction components to cleanly re-register after the object is activated at
    /// runtime. The decision paper starts inactive and is SetActive(true) when a decision arrives;
    /// ISDK interactables that were inactive when the interaction service initialized can fail to
    /// register (so the paper never responds to grab). Toggling each component's `enabled` off then
    /// on re-runs its OnEnable registration a frame after activation. Harmless if already registered.
    /// </summary>
    public class ReRegisterInteractable : MonoBehaviour
    {
        [Tooltip("The HandGrabInteractable, Grabbable, etc. to re-register (set by the builder).")]
        public Behaviour[] interactables;

        void OnEnable() { StartCoroutine(Cycle()); }

        IEnumerator Cycle()
        {
            yield return null;                 // let activation settle one frame
            if (interactables == null) yield break;
            foreach (var b in interactables) if (b != null) b.enabled = false;
            yield return null;
            foreach (var b in interactables) if (b != null) b.enabled = true;
        }
    }
}
