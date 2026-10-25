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
    // - Karma ring (250,191), always 50/50 blend; the kill meter D_800F1135 is not
    //   ported yet, so it stays unlit (n = 0).
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
            Loaded = true;
        }
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
            Draw("karma-ring", 250, 191, 55, 32, 0, 1, 0.5f);
            if (scripts != null && scripts.Potions > 0) Draw("1ichlth", 295, 20, 12, 24);
        }
    }
}
