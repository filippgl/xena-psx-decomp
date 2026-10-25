using UnityEngine;

namespace Xena.Assets
{
    // Screen-facing sprite: the original draws pickups with the identity camera
    // rotation (func_80071F9C/func_80010000), i.e. parallel to the screen.
    // Remaster swaps in the Codex-remastered <name>-hd texture when present.
    public sealed class PrototypeScreenSprite : MonoBehaviour, IXenaVisualModeTarget
    {
        public Texture2D Original, Remaster;
        public bool UsingRemaster { get; private set; }
        private void Start() { ApplyVisualMode(PrototypeGraphics.Active == null ? XenaVisualMode.Remaster : PrototypeGraphics.Active.Mode); }
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            var renderer = GetComponent<MeshRenderer>(); if (renderer == null || Original == null) return;
            UsingRemaster = mode == XenaVisualMode.Remaster && Remaster != null;
            renderer.sharedMaterial.mainTexture = UsingRemaster ? Remaster : Original;
        }
        private void LateUpdate() { if (Camera.main != null) transform.rotation = Camera.main.transform.rotation; }
    }
}
