using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Level01 hard-coded "scripts" (no interpreter exists; docs/xena-level-scripts.md,
    // data tools/export_level_scripts.mjs). VERIFIED rules implemented:
    // - sign_pt (0x800406C8): Xena contact (sign radius 100 + Xena 150, height 500)
    //   shows message param+0x46 once, cooldown 60 frames, re-armed only after
    //   Xena leaves 400 units.
    // - pickups (0x80038F28..0x80039030): contact radius 100+150 / height 300 while
    //   Xena is not attacking/airborne; scroll shows message level*2+p0, health2
    //   and up_sword show their first-pickup message once (0x36+bit table).
    //   Potions: +1, used at once when health+150 < 500 (+30% of max, see
    //   PrototypeOriginalDamage), else kept for auto-use below 350; sword level +1 (max 4).
    // - siras (0x80074FD0): Xena within 1450 (max+3/8min) ends the level.
    // Messages are modal (gameplay paused) and close on Enter/Space/pad A after
    // a short delay. The original closes on Start (pad 0x800, unverified) after
    // D_800F10F0 (UNKNOWN); Start/Esc stay the pause menu here.
    // NOT implemented: pickup models
    // (STM-only sprites / static TMD export pending), music m_vill, Xena pickup
    // animation 0x37 (collection happens at contact instead of frame 11).
    public sealed class PrototypeLevelScripts : MonoBehaviour
    {
        [Serializable] private sealed class ScriptFile { public Actor[] actors; public Message[] messages; public Cutscene cutscene; }
        [Serializable] private sealed class Cutscene { public Key[] keys; public Segment[] segments; public Placement xenaPlacement; }
        [Serializable] private sealed class Key { public int frame; public int[] offset; public Angles angles; }
        [Serializable] private sealed class Angles { public int cam2A_neg, cam28, cam2C; }
        [Serializable] private sealed class Segment { public int from, to, frames; }
        [Serializable] private sealed class Placement { public int offsetX, offsetZ, yaw; }
        [Serializable] private sealed class Actor { public int index; public string name, behaviour; public int[] position; public int[] @params; public int yaw; public Sign sign; }
        [Serializable] private sealed class Sign { public int messageParam; }
        [Serializable] private sealed class Message { public int index; public string[] transliterationInferred; }
        private sealed class Trigger { public Actor actor; public bool armed = true, done; public int cooldown; public GameObject visual; }
        public const int Level = 1, XenaRadius = 150, SignRadius = 100, SignHeight = 500, Rearm = 400, PickupRadius = 100, PickupHeight = 300, SirasRange = 1450;
        private readonly List<Trigger> triggers = new List<Trigger>();
        private readonly Dictionary<int, string[]> messages = new Dictionary<int, string[]>();
        private readonly HashSet<string> firstPickupShown = new HashSet<string>();
        private PrototypeMotor motor;
        private PrototypeInput input;
        private float clock, shownAt;
        public string[] Shown { get; private set; }
        public int ShownIndex { get; private set; } = -1;
        public readonly List<int> MessageLog = new List<int>();
        public int Potions { get; private set; }
        public int SwordLevel { get; private set; }
        public bool LevelComplete { get; private set; }
        // Siras ending (func_8007E410, table 0x800C804C): camera keys relative to
        // Siras, linear per segment (a + f*(b-a)/frames); yaw = +0x2A term, pitch
        // = +0x28 (4096 = 360 deg; checked: key 0 looks straight at Siras).
        public bool CutsceneActive { get; private set; }
        public int CutsceneFrame { get; private set; }
        public int CutsceneFrames { get; private set; }
        private Cutscene cutscene;
        private Vector3 sirasPosition;
        public bool Modal => Shown != null;
        public bool UsePotion() { if (Potions <= 0) return false; Potions--; return true; }

        private void Awake()
        {
            motor = GetComponent<PrototypeMotor>(); input = GetComponent<PrototypeInput>();
            var source = Resources.Load<TextAsset>("Level01Scripts");
            if (source == null) throw new InvalidOperationException("Level01 script data unavailable");
            var file = JsonUtility.FromJson<ScriptFile>(source.text);
            cutscene = file.cutscene;
            foreach (var message in file.messages) messages[message.index] = message.transliterationInferred;
            foreach (var actor in file.actors)
                if (actor.behaviour == "sign" || actor.behaviour == "pickup" || actor.behaviour == "levelEndNpc")
                {
                    var trigger = new Trigger { actor = actor }; triggers.Add(trigger);
                    if (actor.behaviour == "pickup") trigger.visual = PlaceSprite(actor);
                    // Original models: SIGN_PT (static TMD, SMI rest pose) and SIRAS (own SMA, slot 0).
                    if (actor.behaviour == "sign") Place("Sign_pt", actor, false);
                    if (actor.behaviour == "levelEndNpc") Place("Siras", actor, true);
                }
        }

        public int PlacedModels { get; private set; }
        private Transform props;
        // Pickups are screen-facing sprites (descriptor +0x40 = 2, func_80087A44):
        // size table D_800C1D1C (units), quad x -w/2..w/2, y 0..h (bottom-centre
        // pivot), identity camera rotation; health2 50% blend, scroll/up_sword opaque.
        private static readonly Dictionary<string, Vector2Int> SpriteSize = new Dictionary<string, Vector2Int>
        { { "health2", new Vector2Int(120, 240) }, { "scroll", new Vector2Int(256, 256) }, { "up_sword", new Vector2Int(80, 256) } };
        public int PlacedSprites { get; private set; }
        private GameObject PlaceSprite(Actor actor)
        {
            if (!SpriteSize.TryGetValue(actor.name, out var size)) return null;
            var texture = Resources.Load<Texture2D>("Props/Pickups/" + actor.name);
            if (texture == null) throw new InvalidOperationException("Pickup sprite unavailable: " + actor.name);
            if (props == null) props = new GameObject("Level01Props").transform;
            var root = new GameObject(actor.name + " #" + actor.index); root.transform.SetParent(props, false); root.transform.position = Unity(actor.position);
            float w = size.x / 400f, h = size.y / 400f;
            var mesh = new Mesh { name = actor.name + "Sprite" };
            mesh.vertices = new[] { new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            bool half = actor.name == "health2";
            var material = new Material(Shader.Find(half ? "Xena/OriginalSemiTransparent" : "Xena/OriginalUnlit")) { mainTexture = texture };
            material.SetFloat("_UseVertexLighting", 0); material.SetFloat("_Cull", 0); material.SetFloat("_Cutoff", 0.5f);
            if (half) { material.SetTexture("_StpMask", Texture2D.whiteTexture); material.SetFloat("_Ps1BlendMode", 0); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10); }
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            texture.filterMode = FilterMode.Point;
            var sprite = root.AddComponent<PrototypeScreenSprite>(); sprite.Original = texture;
            sprite.Remaster = Resources.Load<Texture2D>("Props/Pickups/" + actor.name + "-hd"); PlacedSprites++;
            return root;
        }
        private void Place(string model, Actor actor, bool animate)
        {
            var prefab = Resources.Load<GameObject>("Props/" + model + "/" + model);
            if (prefab == null) throw new InvalidOperationException("Level prop model unavailable: " + model);
            if (props == null) props = new GameObject("Level01Props").transform;
            var root = new GameObject(actor.name + " #" + actor.index); root.transform.SetParent(props, false);
            root.transform.SetPositionAndRotation(Unity(actor.position), Quaternion.Euler(0, (actor.yaw & 4095) * 360f / 4096, 0));
            // Character models face -Z after the Z reflection and turn 180; the signpost board does not:
            // native capture of sign #0 (build/signdir/native_sign.png) shows the arrow pointing along the
            // path, which the 180 turn reversed. Siras keeps the character convention (not re-verified).
            var body = Instantiate(prefab, root.transform, false); body.transform.localRotation = Quaternion.Euler(0, model == "Sign_pt" ? 0 : 180, 0);
            var pose = body.GetComponent<XenaAssetPreview>(); pose.ShowControls = false; pose.SampleSlot(0, 0); pose.enabled = animate;
            PlacedModels++;
        }
        private static Vector3 Unity(int[] p) => new Vector3(p[0], -p[1], p[2]) / 400;
        private static int Metric(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.RoundToInt((a.x - b.x) * 400)), dz = Mathf.Abs(Mathf.RoundToInt((a.z - b.z) * 400));
            return Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8;
        }
        private bool Contact(Vector3 at, int radius, int height)
        {
            Vector3 xena = motor.transform.position;
            float dy = (xena.y - at.y) * 400;
            return Metric(xena, at) < radius + XenaRadius && dy > -height && dy < 750 + height;
        }

        private void Update()
        {
            if (!Modal) return;
            // Modal message: close after a short delay on confirm/start.
            if (Time.unscaledTime - shownAt > 0.4f && (UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true
                || UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame == true
                || UnityEngine.InputSystem.Gamepad.current?.buttonSouth.wasPressedThisFrame == true)) Close();
        }
        public void Close() { Shown = null; ShownIndex = -1; Time.timeScale = 1; }
        private void Show(int index)
        {
            if (!messages.TryGetValue(index, out var lines)) return;
            Shown = lines; ShownIndex = index; MessageLog.Add(index); shownAt = Time.unscaledTime; Time.timeScale = 0;
        }

        private void FixedUpdate()
        {
            if (CutsceneActive) { clock += Time.fixedDeltaTime; while (clock >= 1f / 30 && CutsceneActive) { clock -= 1f / 30; EndingStep(); } return; }
            if (!PrototypeBandits.AiAllowed || Modal || LevelComplete || (input != null && input.Paused)) return;
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30) { clock -= 1f / 30; Step(); if (Modal) break; }
        }
        private void Step()
        {
            var combat = GetComponent<PrototypeCombat>();
            foreach (var trigger in triggers)
            {
                if (trigger.done) continue;
                Vector3 at = Unity(trigger.actor.position);
                if (trigger.cooldown > 0) trigger.cooldown--;
                switch (trigger.actor.behaviour)
                {
                    case "sign":
                        if (!trigger.armed && Metric(motor.transform.position, at) >= Rearm) trigger.armed = true;
                        if (trigger.armed && trigger.cooldown == 0 && Contact(at, SignRadius, SignHeight))
                        { Show(trigger.actor.sign.messageParam + 0x46); trigger.cooldown = 60; trigger.armed = false; }
                        break;
                    case "pickup":
                        if (!motor.Grounded || (combat != null && combat.Busy) || !Contact(at, PickupRadius, PickupHeight)) break;
                        trigger.done = true; if (trigger.visual != null) Destroy(trigger.visual);
                        if (trigger.actor.name == "scroll") Show(Level * 2 + trigger.actor.@params[0]);
                        else
                        {
                            if (trigger.actor.name == "health2") { Potions++; GetComponent<PrototypeOriginalDamage>()?.OnPotionPicked(); }
                            if (trigger.actor.name == "up_sword") SwordLevel = Mathf.Min(4, SwordLevel + 1);
                            if (firstPickupShown.Add(trigger.actor.name)) Show(trigger.actor.name == "health2" ? 54 : 69);
                        }
                        break;
                    case "levelEndNpc":
                        if (Metric(motor.transform.position, at) < SirasRange) { trigger.done = true; StartEnding(at); }
                        break;
                }
                if (Modal) return;
            }
        }

        private void StartEnding(Vector3 siras)
        {
            sirasPosition = siras; CutsceneFrame = 0; CutsceneFrames = 0;
            if (cutscene?.segments == null || cutscene.keys == null || cutscene.keys.Length < 2) { LevelComplete = true; return; }
            foreach (var segment in cutscene.segments) CutsceneFrames += segment.frames;
            // Xena placement (0x8007EAB0..0x8007EB94): Siras + (130, -330), on the ground, yaw 2787.
            var p = cutscene.xenaPlacement; Vector3 xena = siras + new Vector3(p.offsetX, 0, p.offsetZ) / 400;
            float ground = motor.GroundHeight(xena + Vector3.up); if (!float.IsNegativeInfinity(ground)) xena.y = ground;
            var capsule = motor.GetComponent<CharacterController>(); capsule.enabled = false; motor.transform.SetPositionAndRotation(xena, Quaternion.Euler(0, (p.yaw & 4095) * 360f / 4096, 0)); capsule.enabled = true;
            motor.enabled = false; var orbit = Camera.main.GetComponent<PrototypeCamera>(); if (orbit != null) orbit.enabled = false;
            CutsceneActive = true; EndingStep();
        }
        private void EndingStep()
        {
            int remaining = CutsceneFrame; Key a = cutscene.keys[0], b = cutscene.keys[1]; int frames = 1, within = 0;
            foreach (var segment in cutscene.segments)
            {
                if (remaining < segment.frames || segment == cutscene.segments[cutscene.segments.Length - 1]) { a = cutscene.keys[segment.from]; b = cutscene.keys[segment.to]; frames = segment.frames; within = Mathf.Min(remaining, segment.frames); break; }
                remaining -= segment.frames;
            }
            float t = (float)within / frames;
            Vector3 offset = Vector3.Lerp(new Vector3(a.offset[0], -a.offset[1], a.offset[2]), new Vector3(b.offset[0], -b.offset[1], b.offset[2]), t) / 400;
            float yaw = Mathf.Lerp(a.angles.cam2A_neg, b.angles.cam2A_neg, t) * 360f / 4096, pitch = Mathf.Lerp(a.angles.cam28, b.angles.cam28, t) * 360f / 4096, roll = Mathf.Lerp(a.angles.cam2C, b.angles.cam2C, t) * 360f / 4096;
            Camera.main.transform.SetPositionAndRotation(sirasPosition + offset, Quaternion.Euler(pitch, yaw, roll));
            if (++CutsceneFrame >= CutsceneFrames) { CutsceneActive = false; LevelComplete = true; }
        }
        public IEnumerable<Vector3> TriggerPositions(string behaviour)
        { foreach (var t in triggers) if (t.actor.behaviour == behaviour) yield return Unity(t.actor.position); }

        private GUIStyle style;
        private void OnGUI()
        {
            if (!Modal && !LevelComplete) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(18, Screen.height / 30), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            string text = Modal ? string.Join("\n", Array.ConvertAll(Shown, line => line.TrimEnd())) : "Уровень пройден";
            float w = Screen.width * 0.6f, h = Screen.height * 0.3f;
            GUI.Box(new Rect((Screen.width - w) / 2, Screen.height * 0.6f, w, h), text, style);
        }
    }
}
