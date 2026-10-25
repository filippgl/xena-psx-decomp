using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Remaster presentation for level01 water: @_stream (object14) surface,
    // @rivlet2 (object40) falling water with base mist and rndbow (object0).
    // Original keeps the importer's original/upscale bindings untouched;
    // geometry, colliders and simulation are shared by both modes.
    public sealed class PrototypeRemasterEnvironment : MonoBehaviour, IXenaVisualModeTarget
    {
        private const string Stream = "level-01-object-14", Waterfall = "level-01-object-40", Rainbow = "level-01-object-0", Terrain = "level-01-object-1";
        // Terrain submesh page-9 is entirely the falling-water texture column
        // (all 180 triangles map u0..64, v80..190 of that 256px page).
        private const string TerrainFallPage = "page-9_";
        private sealed class Target { public MeshRenderer renderer; public Material material; public int slot = -1; }
        private readonly List<Target> targets = new List<Target>();
        private readonly List<ParticleSystem> mists = new List<ParticleSystem>();
        private readonly List<GameObject> curtains = new List<GameObject>();
        private Material terrainFall;
        public int Curtains => curtains.Count - Foams;
        // Largest curtain and its facing, for presentation screenshots.
        public Bounds MainFall { get; private set; }
        public Vector3 MainFallFacing { get; private set; }
        private float mainFallLength;
        public int Foams { get; private set; }
        public bool CurtainsVisible => curtains.Count > 0 && curtains.TrueForAll(item => item.activeSelf);
        public XenaVisualMode AppliedMode { get; private set; } = XenaVisualMode.Original;
        public int RemasterSurfaces { get; private set; }
        public int MistEmitters => mists.Count;
        public bool MistActive => mists.Count > 0 && mists.TrueForAll(item => item.gameObject.activeSelf);
        public bool MistInactive => mists.TrueForAll(item => !item.gameObject.activeSelf);

        private void Awake()
        {
            // Pool/river surface heights from the original @_stream mesh.
            var water = new List<Vector3>();
            foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (filter.sharedMesh != null && filter.sharedMesh.name == Stream) foreach (Vector3 p in filter.sharedMesh.vertices) water.Add(filter.transform.TransformPoint(p));
            float WaterHeight(Vector3 at)
            {
                float best = 2.5f * 2.5f, height = float.NaN;
                foreach (Vector3 p in water) { float d = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z); if (d < best) { best = d; height = p.y; } }
                return height;
            }
            foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.sharedMesh == null) continue;
                string name = filter.sharedMesh.name; var renderer = filter.GetComponent<MeshRenderer>();
                if (name == Terrain)
                {
                    Material[] slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length && i < filter.sharedMesh.subMeshCount; i++)
                        if (slots[i] != null && slots[i].name.StartsWith(TerrainFallPage))
                        {
                            Material fall = Create("Remaster/Waterfall", filter.name);
                            int[] triangles = filter.sharedMesh.GetTriangles(i);
                            ConfigureStrip(filter.sharedMesh, triangles, filter.transform, fall, true);
                            targets.Add(new Target { renderer = renderer, material = fall, slot = i });
                            // Boxy falling faces are replaced by smooth curtains;
                            // the river faces above the lip keep this material.
                            int built = AddCurtains(filter, triangles, fall, WaterHeight); terrainFall = fall;
                            if (built == 0) foreach (Bounds fallBounds in FallClusters(filter.sharedMesh, triangles, filter.transform)) mists.Add(CreateMist(fallBounds));
                        }
                    continue;
                }
                string resource = name == Stream ? "Remaster/Water" : name == Waterfall ? "Remaster/Waterfall" : name == Rainbow ? "Remaster/Rainbow" : null;
                if (resource == null) continue;
                Material material = Create(resource, filter.name);
                ConfigureStrip(filter.sharedMesh, filter.sharedMesh.triangles, filter.transform, material, name == Waterfall);
                targets.Add(new Target { renderer = renderer, material = material });
                if (name == Waterfall && AddCurtains(filter, filter.sharedMesh.triangles, material, WaterHeight, true) == 0) mists.Add(CreateMist(renderer.bounds));
            }
        }
        private int AddCurtains(MeshFilter filter, int[] triangles, Material fall, System.Func<Vector3, float> waterHeight, bool freeStanding = false)
        {
            var built = PrototypeRemasterFalls.Build(filter.sharedMesh, triangles, filter.transform, fall.GetVector("_UvRect"), fall.GetFloat("_FlowSign"), waterHeight);
            fall.SetFloat("_HideFalling", built.Count > 0 ? (freeStanding ? 2 : 1) : 0);
            foreach (var curtain in built)
            {
                if (curtain.length > mainFallLength) { mainFallLength = curtain.length; MainFall = curtain.mesh.bounds; MainFallFacing = curtain.outward; }
                var sheet = new GameObject("RemasterWaterCurtain"); sheet.AddComponent<MeshFilter>().sharedMesh = curtain.mesh;
                var sheetMaterial = new Material(fall) { name = "RemasterWaterCurtain" }; sheetMaterial.SetFloat("_HideFalling", 0); sheetMaterial.SetFloat("_Across", curtain.length); sheetMaterial.SetFloat("_BottomFade", 1); sheetMaterial.renderQueue = 3100;
                sheet.AddComponent<MeshRenderer>().sharedMaterial = sheetMaterial; sheet.SetActive(false); curtains.Add(sheet); sheetSources[sheet] = fall;
                mists.Add(CreateFallStreams(curtain)); FallParticleSystems++;
                if (curtain.foam == null) continue;
                mists.Add(CreateMist(curtain.plunge));
                var foam = new GameObject("RemasterPlungeFoam"); foam.AddComponent<MeshFilter>().sharedMesh = curtain.foam;
                foam.AddComponent<MeshRenderer>().sharedMaterial = Create("Remaster/Foam", "plunge"); foam.SetActive(false); curtains.Add(foam); Foams++;
            }
            return built.Count;
        }
        private readonly Dictionary<GameObject, Material> sheetSources = new Dictionary<GameObject, Material>();
        private static Material Create(string resource, string owner)
        {
            Material template = Resources.Load<Material>(resource);
            if (template == null) throw new System.InvalidOperationException("Remaster material unavailable: " + resource);
            return new Material(template) { name = template.name + " (" + owner + ")" };
        }

        // Normalize the used texture-page sub-rect and find which V direction
        // runs downward, so procedural patterns follow the actual surface.
        private static void ConfigureStrip(Mesh mesh, int[] triangles, Transform space, Material material, bool flow)
        {
            Vector2[] uv = mesh.uv; Vector3[] vertices = mesh.vertices;
            if (uv.Length == 0 || triangles.Length == 0) return;
            Vector2 min = uv[triangles[0]], max = min;
            foreach (int i in triangles) { min = Vector2.Min(min, uv[i]); max = Vector2.Max(max, uv[i]); }
            material.SetVector("_UvRect", new Vector4(min.x, min.y, max.x - min.x, max.y - min.y));
            if (!flow) return;
            float meanV = 0, meanY = 0;
            foreach (int i in triangles) { meanV += uv[i].y / triangles.Length; meanY += space.TransformPoint(vertices[i]).y / triangles.Length; }
            float covariance = 0;
            foreach (int i in triangles) covariance += (uv[i].y - meanV) * (space.TransformPoint(vertices[i]).y - meanY);
            material.SetFloat("_FlowSign", covariance < 0 ? 1 : -1);
        }

        // Connected falling-water surfaces (shared vertex positions); tall
        // ones get mist at their base.
        private static List<Bounds> FallClusters(Mesh mesh, int[] triangles, Transform space)
        {
            Vector3[] vertices = mesh.vertices; int count = triangles.Length / 3;
            var parent = new int[count]; for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var owner = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < count; t++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = space.TransformPoint(vertices[triangles[t * 3 + k]]);
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 50), Mathf.RoundToInt(p.y * 50), Mathf.RoundToInt(p.z * 50));
                    if (owner.TryGetValue(key, out int other)) parent[Find(t)] = Find(other); else owner[key] = t;
                }
            var groups = new Dictionary<int, Bounds>();
            for (int t = 0; t < count; t++)
            {
                int root = Find(t);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = space.TransformPoint(vertices[triangles[t * 3 + k]]);
                    if (groups.TryGetValue(root, out Bounds b)) { b.Encapsulate(p); groups[root] = b; } else groups[root] = new Bounds(p, Vector3.zero);
                }
            }
            var tall = new List<Bounds>();
            foreach (Bounds b in groups.Values) if (b.size.y > 1.5f) tall.Add(b);
            return tall;
        }

        public int FallParticleSystems { get; private set; }
        // Falling water as particles: velocity-stretched streams launched from
        // the lip along the curtain's ballistic path, plus fine droplets.
        private ParticleSystem CreateFallStreams(PrototypeRemasterFalls.Curtain curtain)
        {
            Material template = Resources.Load<Material>("Remaster/FallStreak");
            if (template == null) throw new System.InvalidOperationException("Remaster fall streak material unavailable");
            var root = new GameObject("RemasterFallStreams");
            var system = root.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.loop = true; main.playOnAwake = false; main.prewarm = true; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.duration = 3;
            main.startLifetime = new ParticleSystem.MinMaxCurve(curtain.fallTime * 0.95f, curtain.fallTime * 1.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.4f); main.gravityModifier = 1;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f); main.maxParticles = 12000;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.94f, 1f, 0.28f), new Color(1f, 1f, 1f, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission; emission.rateOverTime = Mathf.Clamp(curtain.length * 420, 160, 5200);
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Mesh; shape.meshShapeType = ParticleSystemMeshShapeType.Edge;
            shape.mesh = curtain.lipEmitter; shape.useMeshColors = false; shape.normalOffset = 0;
            var noise = system.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f;
            var fade = system.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.85f, 0.85f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var grow = system.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.8f, 1, 1.3f));
            var render = root.GetComponent<ParticleSystemRenderer>(); render.sharedMaterial = template;
            render.renderMode = ParticleSystemRenderMode.Stretch; render.velocityScale = 0.16f; render.lengthScale = 2.2f;
            // Keep streams visible at distance: thin streaks would fall below one pixel.
            render.minParticleSize = 0.0022f;
            render.sortingFudge = -10;
            root.SetActive(false);
            return system;
        }
        private ParticleSystem CreateMist(Bounds fall)
        {
            Material template = Resources.Load<Material>("Remaster/Mist");
            if (template == null) throw new System.InvalidOperationException("Remaster mist material unavailable");
            var root = new GameObject("RemasterWaterfallMist"); root.transform.position = new Vector3(fall.center.x, fall.min.y + 0.25f, fall.center.z);
            var system = root.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.loop = true; main.playOnAwake = false; main.prewarm = true; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.duration = 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.94f, 0.97f, 1f, 0.2f); main.maxParticles = 220; main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission; emission.rateOverTime = Mathf.Clamp(fall.size.x * fall.size.z * 4, 30, 70);
            var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(0.6f, fall.size.x), 0.2f, Mathf.Max(0.6f, fall.size.z));
            var fade = system.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var grow = system.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
            var render = root.GetComponent<ParticleSystemRenderer>(); render.sharedMaterial = template; render.renderMode = ParticleSystemRenderMode.Billboard;
            // Spray: small bright droplets thrown out of the plunge and falling back.
            var sprayObject = new GameObject("Spray"); sprayObject.transform.SetParent(root.transform, false);
            var spray = sprayObject.AddComponent<ParticleSystem>(); spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sprayMain = spray.main; sprayMain.loop = true; sprayMain.playOnAwake = false; sprayMain.prewarm = true; sprayMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; sprayMain.duration = 2;
            sprayMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); sprayMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            sprayMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f); sprayMain.gravityModifier = 0.55f; sprayMain.maxParticles = 300;
            sprayMain.startColor = new Color(0.95f, 0.98f, 1f, 0.55f); sprayMain.simulationSpace = ParticleSystemSimulationSpace.World;
            var sprayEmission = spray.emission; sprayEmission.rateOverTime = 90;
            var sprayShape = spray.shape; sprayShape.shapeType = ParticleSystemShapeType.Cone; sprayShape.angle = 38; sprayShape.radius = Mathf.Max(0.3f, Mathf.Min(fall.size.x, fall.size.z) * 0.4f);
            sprayObject.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var sprayFade = spray.colorOverLifetime; sprayFade.enabled = true; sprayFade.color = gradient;
            sprayObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = template;
            root.SetActive(false);
            return system;
        }

        // Mist freezes with the pause menu like the other presentation effects.
        private void Update()
        {
            if (!MistActive) return;
            if (input == null) input = GetComponent<PrototypeInput>();
            bool paused = input != null && input.Paused;
            foreach (ParticleSystem mist in mists) { if (paused && !mist.isPaused) mist.Pause(); else if (!paused && mist.isPaused) mist.Play(); }
        }
        private PrototypeInput input;

        public void ApplyVisualMode(XenaVisualMode mode)
        {
            AppliedMode = mode; RemasterSurfaces = 0;
            if (mode == XenaVisualMode.Remaster)
                foreach (Target target in targets)
                {
                    if (target.renderer == null) continue;
                    var slots = target.renderer.sharedMaterials;
                    // Keep the bound (upscaled) original page as surface detail.
                    Material bound = slots[target.slot < 0 ? 0 : target.slot];
                    Texture page = bound != null ? bound.mainTexture : null;
                    if (page != null && target.material.HasProperty("_MainTex")) target.material.SetTexture("_MainTex", page);
                    for (int i = 0; i < slots.Length; i++) if (target.slot < 0 || target.slot == i) slots[i] = target.material;
                    target.renderer.sharedMaterials = slots; RemasterSurfaces++;
                }
            bool active = mode == XenaVisualMode.Remaster;
            foreach (GameObject sheet in curtains)
            {
                Material sheetMaterial = sheet.GetComponent<MeshRenderer>().sharedMaterial;
                if (active && sheetSources.TryGetValue(sheet, out Material source) && sheetMaterial.HasProperty("_MainTex")) sheetMaterial.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                sheet.SetActive(active);
            }
            foreach (ParticleSystem mist in mists)
            {
                if (active && !mist.gameObject.activeSelf) { mist.gameObject.SetActive(true); mist.Simulate(3, true, true); mist.Play(); }
                else if (!active && mist.gameObject.activeSelf) { mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); mist.gameObject.SetActive(false); }
            }
        }
    }
}
