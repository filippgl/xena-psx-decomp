using UnityEngine;

namespace Xena.Assets
{
    // Optional matched visual sets; keep both rigs driven by the shared pose
    // provider. Toggle renderers, never actor roots, scripts or colliders.
    public sealed class XenaVisualVariant : MonoBehaviour, IXenaVisualModeTarget
    {
        [SerializeField] private Renderer[] original, remaster;
        private void OnEnable() { ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode); }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            // Leave the original visible until an improved set is supplied.
            bool upgraded = remaster != null && remaster.Length > 0 && mode == XenaVisualMode.Remaster;
            if (original != null) foreach (var item in original) if (item != null) item.enabled = !upgraded;
            if (remaster != null) foreach (var item in remaster) if (item != null) item.enabled = upgraded;
        }
    }
}
