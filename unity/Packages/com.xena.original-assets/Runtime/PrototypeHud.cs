using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original in-game HUD (docs/xena-hud-audit.md, tools/export_hud.mjs; 821/821
    // opaque texels match the native framebuffer). 320x240 layout:
    // - Xena health: hilt (16,206), blade SWORD (41,209) width len, empty BLOOD
    //   from 41+len (u=len); len = hp<=0 ? 0 : clamp(trunc(109*hp/max), 3, 109),
    //   shown length grows +1 px/frame, drops at once (D_800F1330).
    // - Enemy bars, 4 slots DAGGER/DAGGERED at (40+65i, 25): enemies within 4000
    //   (checked when (actor>>5)&7 == frame&7; here: every 8 frames); len =
    //   clamp(trunc(26*hp/max)+18, 18, 44); visible 60 frames after the last
    //   update; slot freed at hp<=0.
    // - Karma ring (250,191): 29 segments, 50/50 blend, n = trunc(meter*29/96) lit from
    //   segment 15, chase animation while the power-up runs (meter in PrototypeOriginalDamage).
    // - Potion icon 1ICHLTH (295,20) while potions > 0 (drawn opaque here; the
    //   original blends its STP texels when not selected).
    // Original: point-sampled, 4:3 frame centred. Remaster: anchored to the
    // screen corners and uses <name>-4x HD textures when they exist.
    public sealed class PrototypeHud : MonoBehaviour
    {
        private sealed class EnemySlot { public PrototypeBanditBrain brain; public int health = -1; public int visible; }
        private readonly Dictionary<string, Texture2D> original = new Dictionary<string, Texture2D>(), hd = new Dictionary<string, Texture2D>();
        private readonly EnemySlot[] slots = { new EnemySlot(), new EnemySlot(), new EnemySlot(), new EnemySlot() };
        private PrototypeOriginalDamage vitality; private PrototypeLevelScripts scripts; private PrototypeBandits bandits;
        private float clock; private int frame, shownLength;
        public int ShownLength => shownLength;
        public int TargetLength { get; private set; }
        public int VisibleEnemyBars { get { int n = 0; foreach (var s in slots) if (s.brain != null && s.visible > 0) n++; return n; } }
        public bool Loaded { get; private set; }
        public int HdTextures => hd.Count;

        private void Awake()
        {
            vitality = GetComponent<PrototypeOriginalDamage>(); scripts = GetComponent<PrototypeLevelScripts>(); bandits = GetComponent<PrototypeBandits>();
            foreach (string name in new[] { "hilt", "sword", "blood", "dagger", "daggered", "karma-ring", "karma-ring-lit", "1ichlth" })
            {
                var texture = Resources.Load<Texture2D>("HUD/" + name);
                if (texture == null) return;
                texture.filterMode = FilterMode.Point; original[name] = texture;
                var upscaled = Resources.Load<Texture2D>("HUD/" + name + "-4x");
                if (upscaled != null) { upscaled.filterMode = FilterMode.Bilinear; hd[name] = upscaled; }
            }
            for (int i = 0; i < 29; i++)
            {
                var normal = Resources.Load<Texture2D>("HUD/Karma/karma-" + i.ToString("00")); var lit = Resources.Load<Texture2D>("HUD/Karma/karma-" + i.ToString("00") + "-lit");
                if (normal == null || lit == null) return;
                normal.filterMode = lit.filterMode = FilterMode.Point; karmaNormal[i] = normal; karmaLit[i] = lit;
            }
            Loaded = true;
        }
        // Karma ring segment offsets D_800C76C0 relative to (250,191).
        private static readonly Vector2Int[] KarmaOffsets = { new Vector2Int(24, 0), new Vector2Int(27, 0), new Vector2Int(30, 1), new Vector2Int(33, 2), new Vector2Int(37, 4), new Vector2Int(41, 6), new Vector2Int(43, 9), new Vector2Int(45, 12), new Vector2Int(45, 15), new Vector2Int(44, 17), new Vector2Int(40, 20), new Vector2Int(36, 22), new Vector2Int(33, 24), new Vector2Int(29, 24), new Vector2Int(26, 24), new Vector2Int(21, 24), new Vector2Int(17, 23), new Vector2Int(11, 22), new Vector2Int(7, 20), new Vector2Int(3, 18), new Vector2Int(1, 16), new Vector2Int(0, 13), new Vector2Int(0, 9), new Vector2Int(2, 6), new Vector2Int(5, 4), new Vector2Int(8, 2), new Vector2Int(12, 1), new Vector2Int(16, 1), new Vector2Int(19, 0) };
        private readonly Texture2D[] karmaNormal = new Texture2D[29], karmaLit = new Texture2D[29];
        public int LitSegments { get; private set; }
        public static int HealthLength(int hp, int max) => hp <= 0 ? 0 : Mathf.Clamp(109 * hp / max, 3, 109);
        public static int EnemyLength(int hp, int max) => Mathf.Clamp(26 * hp / max + 18, 18, 44);

        private void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            while (clock >= 1f / 30) { clock -= 1f / 30; Tick(); }
        }
        private void Tick()
        {
            frame++;
            if (vitality != null)
            {
                TargetLength = HealthLength(vitality.Health, PrototypeOriginalDamage.MaxHealth);
                shownLength = shownLength > TargetLength ? TargetLength : Mathf.Min(TargetLength, shownLength + 1);
            }
            foreach (var slot in slots)
            {
                if (slot.visible > 0) slot.visible--;
                if (slot.brain != null && (slot.brain.Removed || slot.brain.Health <= 0 || !slot.brain.gameObject.activeInHierarchy)) { slot.brain = null; slot.visible = 0; }
                else if (slot.brain != null && slot.brain.Health != slot.health) { slot.health = slot.brain.Health; slot.visible = 60; }
            }
            if (bandits == null || (frame & 7) != 0) return;
            foreach (var brain in bandits.Brains)
            {
                if (brain == null || brain.Removed || brain.Health <= 0 || !brain.gameObject.activeInHierarchy) continue;
                Vector3 d = (brain.transform.position - transform.position) * 400;
                int dx = Mathf.Abs(Mathf.RoundToInt(d.x)), dz = Mathf.Abs(Mathf.RoundToInt(d.z));
                if (Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8 >= 4000 || System.Array.Exists(slots, s => s.brain == brain)) continue;
                var free = System.Array.Find(slots, s => s.brain == null);
                if (free != null) { free.brain = brain; free.health = brain.Health; free.visible = 60; }
            }
        }

        private void OnGUI()
        {
            if (!Loaded || Event.current.type != EventType.Repaint) return;
            bool remaster = PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
            float scale = Screen.height / 240f, frameLeft = (Screen.width - 320 * scale) / 2;
            // Remaster anchors left/right halves to the screen edges; Original keeps 4:3.
            float X(float x) => remaster ? (x < 160 ? x * scale : Screen.width - (320 - x) * scale) : frameLeft + x * scale;
            void Draw(string name, float x, float y, float w, float h, float u0 = 0, float uw = 1, float alpha = 1)
            {
                Texture2D texture = remaster && hd.TryGetValue(name, out var big) ? big : original[name];
                var color = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
                GUI.DrawTextureWithTexCoords(new Rect(X(x), y * scale, w * scale, h * scale), texture, new Rect(u0, 0, uw, 1));
                GUI.color = color;
            }
            int len = shownLength;
            if (len > 0) Draw("sword", 41, 209, len, 6, 0, len / 112f);
            Draw("blood", 41 + len, 209, 112 - len, 6, len / 112f, (112 - len) / 112f);
            Draw("hilt", 16, 206, 28, 14);
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i]; if (slot.brain == null || slot.visible <= 0) continue;
                int e = EnemyLength(slot.brain.Health, slot.brain.MaxHealth);
                Draw("dagger", 40 + 65 * i, 25, e, 11, 0, e / 48f);
                Draw("daggered", 40 + 65 * i + e, 25, 48 - e, 11, e / 48f, (48 - e) / 48f);
            }
            // Ring: n = trunc(meter*29/96) segments lit from segment 15; while the power-up
            // runs, a chase: phase=(frame>>1)&3, phase+4k semi-transparent, phase+1+4k lit opaque.
            int karma = vitality != null ? vitality.Karma : 0, n = karma * 29 / 96; bool power = vitality != null && vitality.PowerUp > 0;
            LitSegments = 0;
            for (int i = 0; i < 29; i++)
            {
                int segment = (i + 15) % 29; bool lit = i < n; float alpha = 0.5f; int phase = (frame >> 1) & 3;
                if (power) { if ((segment - phase) % 4 == 0) alpha = 0.5f; if ((segment - phase - 1 + 29) % 4 == 0) { lit = true; alpha = 1; } }
                if (lit) LitSegments++;
                var texture = lit ? karmaLit[segment] : karmaNormal[segment];
                float sx = 250 + KarmaOffsets[segment].x, sy = 191 + KarmaOffsets[segment].y;
                if (remaster && hd.TryGetValue(lit ? "karma-ring-lit" : "karma-ring", out var ring))
                {
                    // HD ring: the same segment rectangle cut from the Codex-remastered composite.
                    var c = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
                    GUI.DrawTextureWithTexCoords(new Rect(X(sx), sy * scale, texture.width * scale, texture.height * scale), ring,
                        new Rect(KarmaOffsets[segment].x / 55f, 1 - (KarmaOffsets[segment].y + texture.height) / 32f, texture.width / 55f, texture.height / 32f));
                    GUI.color = c;
                }
                else { var c = GUI.color; GUI.color = new Color(1, 1, 1, alpha); GUI.DrawTexture(new Rect(X(sx), sy * scale, texture.width * scale, texture.height * scale), texture); GUI.color = c; }
            }
            if (scripts != null && scripts.Potions > 0) Draw("1ichlth", 295, 20, 12, 24);
        }
    }
}
