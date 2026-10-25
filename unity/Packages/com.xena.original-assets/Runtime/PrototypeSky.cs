using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xena.Assets
{
    // Original: the level01 background is a flat clear colour; the native
    // frame 9200 sky region measures RGB(0,72,95). Original fog/depth cueing
    // is not decoded yet and is not imitated; no post-processing.
    // Remaster: CC0 HDRI sky dome, horizon-coloured depth fog, global post
    // volume (bloom, ACES, grading, vignette) and SMAA.
    public sealed class PrototypeSky : MonoBehaviour, IXenaVisualModeTarget
    {
        public static readonly Color32 OriginalBackground = new Color32(0, 72, 95, 255);
        public const float FogStart = 22, FogEnd = 70;
        public bool SkyFogActive { get; private set; }
        private Material remasterSky;
        private Volume post;
        private Light sun;
        private Texture2D rockAlbedo, rockNormal, rockOcclusion;
        public bool RockActive { get; private set; }
        public static readonly Vector3 SunDirection = new Vector3(0.35f, 0.75f, 0.25f);
        public bool SunActive => sun != null && sun.enabled;
        public bool PostActive => post != null && post.enabled;
        public void ApplyVisualMode(XenaVisualMode mode)
        {
            Camera view = Camera.main; if (view == null) return;
            bool remaster = mode == XenaVisualMode.Remaster;
            var data = view.GetUniversalAdditionalCameraData();
            if (post == null)
            {
                var profile = Resources.Load<VolumeProfile>("Remaster/Post");
                if (profile == null) throw new System.InvalidOperationException("Remaster post profile unavailable");
                post = new GameObject("RemasterPost").AddComponent<Volume>(); post.isGlobal = true; post.sharedProfile = profile;
            }
            post.enabled = remaster; data.renderPostProcessing = remaster; SkyFogActive = remaster;
            if (sun == null)
            {
                sun = new GameObject("RemasterSun").AddComponent<Light>(); sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.LookRotation(-SunDirection.normalized); sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 1; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f; sun.intensity = 1.1f; sun.color = new Color(1, 0.96f, 0.88f);
            }
            sun.enabled = remaster;
            // The importer disables shadows (original look); Remaster turns them on.
            foreach (string root in new[] { "OriginalLevel01", "ControlledXena", "Level01Bandits", "Level01Props" })
            {
                GameObject owner = GameObject.Find(root); if (owner == null) continue;
                foreach (Renderer item in owner.GetComponentsInChildren<Renderer>(true))
                {
                    item.shadowCastingMode = remaster ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
                    item.receiveShadows = remaster;
                }
            }
            data.antialiasing = remaster ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            data.antialiasingQuality = AntialiasingQuality.High;
            RenderSettings.fog = false;
            Shader.SetGlobalFloat("_XenaSkyFogEnabled", remaster ? 1 : 0);
            Shader.SetGlobalFloat("_XenaRockEnabled", remaster ? 1 : 0);
            if (remaster && rockAlbedo == null)
            {
                rockAlbedo = Resources.Load<Texture2D>("Remaster/RockColor"); rockNormal = Resources.Load<Texture2D>("Remaster/RockNormal"); rockOcclusion = Resources.Load<Texture2D>("Remaster/RockAO");
                if (rockAlbedo == null || rockNormal == null || rockOcclusion == null) throw new System.InvalidOperationException("Remaster rock textures unavailable");
                Shader.SetGlobalTexture("_XenaRockAlbedo", rockAlbedo); Shader.SetGlobalTexture("_XenaRockNormal", rockNormal); Shader.SetGlobalTexture("_XenaRockAO", rockOcclusion);
                Shader.SetGlobalVector("_XenaRockParams", new Vector4(0.33f, 1f, 1f, 1f));
            }
            RockActive = remaster && rockAlbedo != null;
            if (!remaster)
            {
                view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = OriginalBackground;
                return;
            }
            if (remasterSky == null) remasterSky = Resources.Load<Material>("Remaster/Sky");
            if (remasterSky == null) throw new System.InvalidOperationException("Remaster sky material unavailable");
            RenderSettings.skybox = remasterSky; view.clearFlags = CameraClearFlags.Skybox;
            // Aerial perspective into the panorama itself (start/end metres).
            Shader.SetGlobalTexture("_XenaSkyPanorama", remasterSky.GetTexture("_MainTex"));
            Shader.SetGlobalVector("_XenaSkyFogParams", new Vector4(remasterSky.GetFloat("_Rotation"), remasterSky.GetFloat("_Exposure"), FogStart, FogEnd));
            Shader.SetGlobalColor("_XenaHazeColor", remasterSky.GetColor("_HazeColor").linear);
        }
    }
}
