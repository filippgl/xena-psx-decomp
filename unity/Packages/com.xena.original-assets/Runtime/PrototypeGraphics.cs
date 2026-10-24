using System;
using UnityEngine;

namespace Xena.Assets
{
    public enum XenaVisualMode { Original, Remaster }
    public interface IXenaVisualModeTarget { void ApplyVisualMode(XenaVisualMode mode); }

    // Presentation only. Motors, animation clocks, hitboxes and level state are
    // shared by both modes. New visual providers implement the same small hook.
    public sealed class PrototypeGraphics : MonoBehaviour
    {
        public static PrototypeGraphics Active { get; private set; }
        public XenaVisualMode Mode { get; private set; } = XenaVisualMode.Remaster;
        public event Action<XenaVisualMode> Changed;
        private PrototypeInput input;
        private void Awake() { Active = this; input = GetComponent<PrototypeInput>(); }
        private void Start() { Apply(); }
        private void OnDestroy() { if (Active == this) Active = null; }
        private void Update()
        {
            if (input.GraphicsTogglePressed) SetMode(Mode == XenaVisualMode.Original ? XenaVisualMode.Remaster : XenaVisualMode.Original);
            XenaVisualMode configured = input.Preferences.Data.originalGraphics ? XenaVisualMode.Original : XenaVisualMode.Remaster;
            if (configured != Mode) { Mode = configured; Apply(); }
        }
        public void SetMode(XenaVisualMode mode)
        {
            Mode = mode; input.Preferences.Data.originalGraphics = mode == XenaVisualMode.Original;
            Apply();
        }
        private void Apply()
        {
            Mode = input.Preferences.Data.originalGraphics ? XenaVisualMode.Original : XenaVisualMode.Remaster;
            foreach (var preview in FindObjectsByType<XenaAssetPreview>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                preview.SetUpscale(Mode == XenaVisualMode.Remaster);
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (component is IXenaVisualModeTarget target) target.ApplyVisualMode(Mode);
            Changed?.Invoke(Mode);
        }
    }

}
