using UnityEngine;

namespace Xena.Assets
{
    public sealed class XenaLevelLighting : MonoBehaviour
    {
        [SerializeField] private Renderer[] targets;
        [SerializeField] private bool lightingEnabled = true;
        [SerializeField, Range(-64, 64)] private int brightness;

        public void Configure(Renderer[] renderers) { targets = renderers; }
        private void OnEnable() { Apply(); }
        public void SetLighting(bool enabled) { lightingEnabled = enabled; Apply(); }
        public void SetBrightness(int value) { brightness = Mathf.Clamp(value, -64, 64); Apply(); }
        private void Apply()
        {
            if (targets == null) return;
            var block = new MaterialPropertyBlock();
            foreach (Renderer target in targets)
            {
                if (target == null) continue;
                target.GetPropertyBlock(block);
                block.SetFloat("_UseVertexLighting", lightingEnabled ? 1 : 0);
                block.SetFloat("_LightBias", brightness);
                target.SetPropertyBlock(block); block.Clear();
            }
        }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 280, 100), GUI.skin.box);
            bool next = GUILayout.Toggle(lightingEnabled, "Original vertex lighting");
            if (next != lightingEnabled) SetLighting(next);
            GUILayout.Label("Brightness: " + brightness);
            int value = Mathf.RoundToInt(GUILayout.HorizontalSlider(brightness, -64, 64));
            if (value != brightness) SetBrightness(value);
            GUILayout.EndArea();
        }
    }
}
