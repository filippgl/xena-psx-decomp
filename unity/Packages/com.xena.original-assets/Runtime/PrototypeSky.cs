using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xena.Assets
{
    // Original (docs/xena-level-background.md, VERIFIED on the native port): the camera clears to the level fog
    // colour (ENVIRO.BIN +0x4C, level 01 RGB(0,74,97)); a screen-space background (Xena/OriginalHorizon) draws the
    // sky fill (21,97,214), the HORIZ.STM strip (tools/export_horizon.mjs) and the fog fill; level surfaces fade
    // to the fog colour by the hyperbolic depth cue between N = 9780 and F = 13108 units (24.45..32.77 m).
    // No post-processing.
    // Remaster: CC0 HDRI sky dome, horizon-coloured depth fog, global post
    // volume (bloom, ACES, grading, vignette) and SMAA.
    public sealed class PrototypeSky : MonoBehaviour, IXenaVisualModeTarget
    {
        public static readonly Color32 OriginalBackground = new Color32(0, 74, 97, 255), OriginalSky = new Color32(21, 97, 214, 255);
        public const float OriginalFogNear = 9780 / 400f, OriginalFogFar = 13108 / 400f;
        private MeshRenderer horizon; private Material horizonMaterial;
        public bool HorizonActive => horizon != null && horizon.enabled;
        // Original fog factor at view depth z (m): the shaders' formula (test oracle).
        public static float OriginalFogFactor(float z) { float a = OriginalFogFar / (OriginalFogFar - OriginalFogNear); return Mathf.Clamp01(a - a * OriginalFogNear / Mathf.Max(0.01f, z)); }
        private void EnsureHorizon(Camera view)
        {
            if (horizon != null) return;
            var tex = Resources.Load<Texture2D>("Original/Horizon01"); var template = Resources.Load<Material>("Original/OriginalHorizon"); if (tex == null || template == null) return;
            horizonMaterial = new Material(template) { name = "OriginalHorizon" }; horizonMaterial.SetTexture("_MainTex", tex);
            horizonMaterial.SetColor("_SkyColor", OriginalSky); horizonMaterial.SetColor("_FogColor", OriginalBackground);
            var go = new GameObject("OriginalHorizon"); go.transform.SetParent(view.transform, false);
            var mesh = new Mesh { name = "OriginalHorizon" }; mesh.vertices = new Vector3[3]; mesh.triangles = new[] { 0, 1, 2 }; mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; horizon = go.AddComponent<MeshRenderer>(); horizon.sharedMaterial = horizonMaterial;
            horizon.shadowCastingMode = ShadowCastingMode.Off; horizon.receiveShadows = false;
        }
        private void LateUpdate()
        {
            if (!HorizonActive) return;
            var view = Camera.main; if (view == null) return;
            Vector3 e = view.transform.eulerAngles; float pitch = e.x > 180 ? e.x - 360 : e.x;
            Shader.SetGlobalVector("_XenaHorizonView", new Vector4(e.y * 4096 / 360f, pitch * 4096 / 360f, 0, 0));
        }
        public const float FogStart = 37, FogEnd = 85; // user: 15 m farther than 22/70 (Silent Hill feel)
        public bool SkyFogActive { get; private set; }
        private Material remasterSky;
        private Texture2D landscape;
        public bool LandscapeActive { get; private set; }
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
            foreach (string root in new[] { "OriginalLevel01", "ControlledXena", "Level01Bandits", "Level01Props", "Level01Frogs" })
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
            float fogA = OriginalFogFar / (OriginalFogFar - OriginalFogNear);
            Shader.SetGlobalVector("_XenaOriginalFog", new Vector4(remaster ? 0 : 1, fogA, fogA * OriginalFogNear, 0));
            Shader.SetGlobalVector("_XenaOriginalFogColor", new Vector4(OriginalBackground.r / 255f, OriginalBackground.g / 255f, OriginalBackground.b / 255f, 1)); // encoded RGB
            EnsureHorizon(view); if (horizon != null) horizon.enabled = !remaster;
            if (!remaster)
            {
                Shader.SetGlobalVector("_XenaLandscapeParams", Vector4.zero); LandscapeActive = false;
                view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = OriginalBackground;
                LateUpdate();
                return;
            }
            if (remasterSky == null) remasterSky = Resources.Load<Material>("Remaster/Sky");
            if (remasterSky == null) throw new System.InvalidOperationException("Remaster sky material unavailable");
            RenderSettings.skybox = remasterSky; view.clearFlags = CameraClearFlags.Skybox;
            // Aerial perspective into the panorama itself (start/end metres).
            Shader.SetGlobalTexture("_XenaSkyPanorama", remasterSky.GetTexture("_MainTex"));
            Shader.SetGlobalVector("_XenaSkyFogParams", new Vector4(remasterSky.GetFloat("_Rotation"), remasterSky.GetFloat("_Exposure"), FogStart, FogEnd));
            Shader.SetGlobalColor("_XenaHazeColor", remasterSky.GetColor("_HazeColor").linear);
            // Painted 360-degree landscape (Codex, tools/stitch_panorama.py): horizon at 55% from
            // the top as requested in the prompts; fogged far geometry dissolves into it too.
            if (landscape == null) landscape = Resources.Load<Texture2D>("Remaster/Panorama");
            LandscapeActive = landscape != null;
            if (landscape != null) Shader.SetGlobalTexture("_XenaLandscape", landscape);
            Shader.SetGlobalVector("_XenaLandscapeParams", landscape == null ? Vector4.zero : new Vector4(0, 0.55f, (float)landscape.width / landscape.height, 1));
        }
    }
}
