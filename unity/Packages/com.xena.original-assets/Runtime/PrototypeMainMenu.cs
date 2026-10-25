using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets
{
    // Original main menu (docs/xena-main-menu.md, tools/export_main_menu.mjs +
    // tools/install_main_menu.py; the exporter composites 298/298 native frames exactly).
    // Original mode composites each tick on the CPU in the original order: main.tim, active
    // lightning bolts, hair channels 0..2, the cursor highlight, then the prompt added with
    // 5-bit saturation (abr 1). Texel 0 is transparent. Menu tick = 30 Hz here (INFERRED:
    // the native port ran ~34.7 Hz, the hardware rate is UNKNOWN); frame flips, no interpolation.
    // Remaster: Codex HD background (labels removed), Codex HD labels, Real-ESRGAN lightning and
    // prompt, and a wind warp of Xena's hair driven by the same tick (presentation only).
    // Ported actions: New game (starts level 01). Load game, Training arena, Options and the
    // 900-tick attract demo are not ported (a note is shown / the idle timer only restarts).
    // Menu sounds 0x39/0 are not ported.
    public sealed class PrototypeMainMenu : MonoBehaviour
    {
        public static bool PendingShow;                // set by the GAME OVER flow before reloading
        public static int Cursor { get; private set; } // D_800ED534 keeps its value between visits
        public bool Visible { get; private set; }
        public int Ticks { get; private set; }
        public int ConfirmDelay { get; private set; }
        public int Idle { get; private set; }
        public int[] HairSeq => new[] { hair[0].seq, hair[1].seq, hair[2].seq };
        public int[] HairFrame => new[] { hair[0].frame, hair[1].frame, hair[2].frame };
        public int BoltsDrawn { get; private set; }
        public bool HdReady => hdBackground != null;
        public string Note { get; private set; }
        public static int NewGames { get; private set; }

        // D_800C2880 sequence starts, D_800C2868/D_800C2874 centres.
        private static readonly int[] SeqStart = { 0, 12, 72, 91, 112, 134 };
        private static readonly Vector2Int[] SeqCentre = { new Vector2Int(260, 49), new Vector2Int(260, 49), new Vector2Int(260, 49), new Vector2Int(246, 34), new Vector2Int(232, 33) };
        private static readonly Vector2Int[] BoltCentre = { new Vector2Int(33, 41), new Vector2Int(152, 33), new Vector2Int(145, 102), new Vector2Int(287, 94) };
        private static readonly Vector2Int[] ItemCentre = { new Vector2Int(76, 133), new Vector2Int(80, 156), new Vector2Int(95, 179), new Vector2Int(69, 203) };
        private static readonly string[] ItemNames = { "ng", "lg", "ta", "op" };
        private static readonly Vector2Int PromptTopLeft = new Vector2Int(131, 201);
        private struct Hair { public int seq, frame; }
        private struct Bolt { public int timer, state; }
        private readonly Hair[] hair = new Hair[3];
        private readonly Bolt[] bolts = new Bolt[4];
        private readonly bool[] boltVisible = new bool[4];
        private uint seed;

        private Color32[] background, prompt; private int promptW, promptH;
        private Color32[][] hairTiles, boltTiles, highlights; private Vector2Int[] hairSize, boltSize, highlightSize;
        private Color32[] frame = new Color32[320 * 240]; private Texture2D screen;
        private Texture2D hdBackground, hdPrompt, hairPatch; private Texture2D[] hdBolts = new Texture2D[4], hdLabels = new Texture2D[4], hdLabelsSel = new Texture2D[4];
        private Color32[] hdSource, hairMask, patchPixels; private RectInt patchRect;
        private float clock, noteUntil;
        private readonly RectInt[] labelBounds = new RectInt[4];
        // Alpha bounding box in top-down texture pixels.
        private static RectInt AlphaBounds(Texture2D t)
        {
            var p = t.GetPixels32(); int x0 = t.width, x1 = -1, y0 = t.height, y1 = -1;
            for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++)
                if (p[y * t.width + x].a > 40) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            return x1 < x0 ? new RectInt(0, 0, t.width, t.height) : new RectInt(x0, t.height - 1 - y1, x1 - x0 + 1, y1 - y0 + 1);
        }

        private void Awake()
        {
            bool smoke = false;
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg.EndsWith("-smoke")) smoke = true;
            if (!Load()) { enabled = false; PendingShow = false; return; }
            if (PendingShow || (!smoke && !Application.isEditor)) Show();
            PendingShow = false;
        }
        private bool Load()
        {
            var bg = Resources.Load<Texture2D>("Menu/background");
            if (bg == null || !bg.isReadable) return false;
            background = Flip(bg.GetPixels32(), bg.width, bg.height);
            hairTiles = LoadTiles("Menu/hair", out hairSize); boltTiles = LoadTiles("Menu/lightning", out boltSize);
            highlights = new Color32[4][]; highlightSize = new Vector2Int[4];
            for (int i = 0; i < 4; i++)
            {
                var t = Resources.Load<Texture2D>("Menu/highlights/" + ItemNames[i] + "_sel"); if (t == null) return false;
                highlights[i] = Flip(t.GetPixels32(), t.width, t.height); highlightSize[i] = new Vector2Int(t.width, t.height);
            }
            var p = Resources.Load<Texture2D>("Menu/font/prompt"); if (p == null) return false;
            prompt = Flip(p.GetPixels32(), p.width, p.height); promptW = p.width; promptH = p.height;
            if (hairTiles.Length != 134 || boltTiles.Length != 4) return false;
            screen = new Texture2D(320, 240, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            hdBackground = Resources.Load<Texture2D>("Menu/HD/background");
            if (hdBackground != null)
            {
                var mask = Resources.Load<Texture2D>("Menu/HD/hair-mask");
                if (mask != null && hdBackground.isReadable && mask.isReadable)
                {
                    hdSource = hdBackground.GetPixels32(); hairMask = mask.GetPixels32();
                    // Bounding box of the mask (texture rows are bottom-up).
                    int x0 = mask.width, x1 = -1, y0 = mask.height, y1 = -1;
                    for (int y = 0; y < mask.height; y++) for (int x = 0; x < mask.width; x++)
                        if (hairMask[y * mask.width + x].r > 0) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
                    if (x1 > x0) { patchRect = new RectInt(x0 - 6, y0 - 6, x1 - x0 + 13, y1 - y0 + 13); hairPatch = new Texture2D(patchRect.width, patchRect.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; patchPixels = new Color32[patchRect.width * patchRect.height]; }
                }
                hdPrompt = Resources.Load<Texture2D>("Menu/HD/prompt");
                var hdBoltTiles = Resources.LoadAll<Texture2D>("Menu/HD/lightning"); Array.Sort(hdBoltTiles, (a, b) => string.CompareOrdinal(a.name, b.name));
                for (int i = 0; i < 4 && i < hdBoltTiles.Length; i++) hdBolts[i] = hdBoltTiles[i];
                for (int i = 0; i < 4; i++)
                {
                    hdLabels[i] = Resources.Load<Texture2D>("Menu/HD/label-" + ItemNames[i]); hdLabelsSel[i] = Resources.Load<Texture2D>("Menu/HD/label-" + ItemNames[i] + "-sel");
                    labelBounds[i] = hdLabels[i] != null && hdLabels[i].isReadable ? AlphaBounds(hdLabels[i]) : new RectInt(0, 0, 1, 1);
                }
            }
            return true;
        }
        private static Color32[][] LoadTiles(string folder, out Vector2Int[] sizes)
        {
            var tiles = Resources.LoadAll<Texture2D>(folder);
            Array.Sort(tiles, (a, b) => string.CompareOrdinal(a.name, b.name)); // NNN_ prefix = tile index
            var pixels = new Color32[tiles.Length][]; sizes = new Vector2Int[tiles.Length];
            for (int i = 0; i < tiles.Length; i++) { pixels[i] = Flip(tiles[i].GetPixels32(), tiles[i].width, tiles[i].height); sizes[i] = new Vector2Int(tiles[i].width, tiles[i].height); }
            return pixels;
        }
        // Texture rows are bottom-up; the PS1 framebuffer is top-down.
        private static Color32[] Flip(Color32[] p, int w, int h)
        {
            var o = new Color32[p.Length];
            for (int y = 0; y < h; y++) Array.Copy(p, (h - 1 - y) * w, o, y * w, w);
            return o;
        }

        public void Show()
        {
            Visible = true; Ticks = 0; ConfirmDelay = 5; Idle = 900; clock = 0; Note = null;
            // func_8006AA04: hair channels start at the first tile of seq 2/3/4; bolts rand(30,200), state 2.
            if (seed == 0) seed = (uint)Environment.TickCount | 1;
            hair[0] = new Hair { seq = 2, frame = SeqStart[2] }; hair[1] = new Hair { seq = 3, frame = SeqStart[3] }; hair[2] = new Hair { seq = 4, frame = SeqStart[4] };
            for (int i = 0; i < 4; i++) bolts[i] = new Bolt { timer = Range(30, 200), state = 2 };
            Time.timeScale = 0; AudioListener.pause = true;
            Compose();
        }
        public void SeedForTest(uint value) { seed = value; }
        private void Hide() { Visible = false; Time.timeScale = 1; AudioListener.pause = false; }

        // func_800B42D8 / func_800B4350.
        private int Rand()
        {
            for (int i = 0; i < 16; i++) { uint bit = (seed >> 30) & 1; if ((seed & 4) != 0) bit ^= 1; seed = (seed << 1) | bit; }
            return (short)(seed & 0xFFFF);
        }
        private int Range(int lo, int hi) => lo + (int)(((uint)(hi - lo + 1) * (uint)(Rand() & 0xFFFF)) >> 16);

        private void Update()
        {
            if (!Visible) return;
            int move = 0; bool confirm = false;
            if (Pressed(Key.UpArrow) || Pressed(Key.W) || Gamepad.current?.dpad.up.wasPressedThisFrame == true) move = -1;
            if (Pressed(Key.DownArrow) || Pressed(Key.S) || Gamepad.current?.dpad.down.wasPressedThisFrame == true) move = 1;
            if (Pressed(Key.Enter) || Pressed(Key.Space) || Gamepad.current?.buttonSouth.wasPressedThisFrame == true) confirm = true;
            if (move != 0 || confirm) Idle = 900;
            if (move != 0) Cursor = Mathf.Clamp(Cursor + move, 0, 3);
            if (confirm) Select();
            if (!Visible) return;
            clock += Time.unscaledDeltaTime;
            bool stepped = false;
            for (int guard = 0; clock >= 1f / 30 && guard < 4; guard++) { clock -= 1f / 30; Tick(); stepped = true; }
            if (clock > 1f / 30) clock = 0;
            if (stepped) Compose();
        }
        private static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        public void MoveCursor(int delta) { if (Visible) { Cursor = Mathf.Clamp(Cursor + delta, 0, 3); Idle = 900; Compose(); } }
        public void Select()
        {
            if (!Visible || ConfirmDelay > 0) return;
            if (Cursor == 0) { NewGames++; Hide(); return; }
            Note = "Пока недоступно в прототипе"; noteUntil = Time.unscaledTime + 2;
        }
        // One menu tick (func_8006AC88 + main's counters), RNG call order: bolts 0..3, then hair.
        public void Tick()
        {
            Ticks++;
            if (ConfirmDelay > 0) ConfirmDelay--;
            if (--Idle <= 0) Idle = 900; // attract demo not ported
            BoltsDrawn = 0;
            for (int i = 0; i < 4; i++)
            {
                var b = bolts[i]; boltVisible[i] = false;
                b.timer = b.timer != 0 ? b.timer - 1 : Range(60, 600);
                if (b.timer < 7)
                {
                    if (b.state >= 2) { boltVisible[i] = true; BoltsDrawn++; }
                    b.state = (sbyte)(b.state - 1);
                    if (b.state <= 0) b.state = Range(0, 3);
                }
                bolts[i] = b;
            }
            for (int c = 0; c < 3; c++)
            {
                var h = hair[c];
                h.frame += h.seq == 1 ? 2 : 1;
                if (h.frame >= SeqStart[h.seq + 1])
                {
                    h.seq = h.seq == 0 ? 1 : h.seq == 1 ? ((Rand() & 4) != 0 ? 2 : 1) : h.seq == 2 ? 0 : h.seq;
                    h.frame = SeqStart[h.seq];
                }
                hair[c] = h;
            }
            if (hairPatch != null) WarpHair();
        }

        private void Compose()
        {
            Array.Copy(background, frame, frame.Length);
            for (int i = 0; i < 4; i++) if (boltVisible[i]) Blit(boltTiles[i], boltSize[i], BoltCentre[i] - boltSize[i] / 2);
            for (int c = 0; c < 3; c++) Blit(hairTiles[hair[c].frame], hairSize[hair[c].frame], SeqCentre[hair[c].seq] - hairSize[hair[c].frame] / 2);
            Blit(highlights[Cursor], highlightSize[Cursor], ItemCentre[Cursor] - highlightSize[Cursor] / 2);
            // Prompt: semi-transparent abr 1 (B + F), clamped per 5-bit channel.
            for (int y = 0; y < promptH; y++) for (int x = 0; x < promptW; x++)
            {
                var t = prompt[y * promptW + x]; if (t.a == 0) continue;
                int at = (PromptTopLeft.y + y) * 320 + PromptTopLeft.x + x; var b = frame[at];
                frame[at] = new Color32(Add(b.r, t.r), Add(b.g, t.g), Add(b.b, t.b), 255);
            }
            screen.SetPixels32(Flip(frame, 320, 240)); screen.Apply(false);
        }
        private static byte Add(byte a, byte b) { int s = Mathf.Min(31, (a * 31 + 127) / 255 + (b * 31 + 127) / 255); return (byte)(s * 255 / 31); }
        private void Blit(Color32[] tile, Vector2Int size, Vector2Int topLeft)
        {
            for (int y = 0; y < size.y; y++)
            {
                int sy = topLeft.y + y; if (sy < 0 || sy >= 240) continue;
                for (int x = 0; x < size.x; x++)
                {
                    int sx = topLeft.x + x; var t = tile[y * size.x + x];
                    if (sx < 0 || sx >= 320 || t.a == 0) continue;
                    frame[sy * 320 + sx] = t;
                }
            }
        }

        // Remaster hair: wind displacement of the masked hair pixels, stronger toward the
        // back/ends of the hair (away from the face), resampled from the still HD image.
        private void WarpHair()
        {
            int w = hdBackground.width, pw = patchRect.width, ph = patchRect.height; float t = Ticks;
            for (int py = 0; py < ph; py++)
            {
                int y = patchRect.y + py;
                for (int px = 0; px < pw; px++)
                {
                    int x = patchRect.x + px; int i = y * w + x;
                    float m = hairMask[i].r / 255f;
                    if (m <= 0) { patchPixels[py * pw + px] = hdSource[i]; continue; }
                    float reach = Mathf.Clamp01((float)px / pw * 1.3f) * Mathf.Clamp01(1.2f - (float)py / ph * 0.4f);
                    float amp = 4.5f * m * (0.35f + reach);
                    float dx = amp * (Mathf.Sin(t * 0.19f + y * 0.045f) + 0.45f * Mathf.Sin(t * 0.43f + y * 0.13f + x * 0.031f));
                    float dy = amp * 0.35f * Mathf.Sin(t * 0.27f + x * 0.052f);
                    patchPixels[py * pw + px] = Sample(x - dx, y - dy, w, hdBackground.height);
                }
            }
            hairPatch.SetPixels32(patchPixels); hairPatch.Apply(false);
        }
        private Color32 Sample(float x, float y, int w, int h)
        {
            int x0 = Mathf.Clamp((int)x, 0, w - 2), y0 = Mathf.Clamp((int)y, 0, h - 2); float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0);
            var a = hdSource[y0 * w + x0]; var b = hdSource[y0 * w + x0 + 1]; var c = hdSource[(y0 + 1) * w + x0]; var d = hdSource[(y0 + 1) * w + x0 + 1];
            return Color32.Lerp(Color32.Lerp(a, b, fx), Color32.Lerp(c, d, fx), fy);
        }

        private void OnGUI()
        {
            GUI.depth = -1100;
            if (!Visible || Event.current.type != EventType.Repaint) return;
            var tint = GUI.color; GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = tint;
            bool remaster = PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
            if (remaster && hdBackground != null) DrawRemaster();
            else { float h = Screen.height, w = h * 4 / 3; GUI.DrawTexture(new Rect((Screen.width - w) / 2, 0, w, h), screen, ScaleMode.StretchToFill); }
            if (Note != null && Time.unscaledTime < noteUntil)
            {
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(14, Screen.height / 36) };
                GUI.Label(new Rect(0, Screen.height * 0.93f, Screen.width, Screen.height * 0.05f), Note, style);
            }
        }
        // HD image: the 4:3 picture spans x 85..1450 of 1536 at full height (1024).
        private void DrawRemaster()
        {
            float bw = hdBackground.width, bh = hdBackground.height;
            float scale = Mathf.Max(Screen.width / bw, Screen.height / bh);
            Vector2 origin = new Vector2((Screen.width - bw * scale) / 2, (Screen.height - bh * scale) / 2);
            Rect Hd(float x, float y, float w, float h) => new Rect(origin.x + x * scale, origin.y + y * scale, w * scale, h * scale);
            float k = bh / 240f; Rect Ps(float x, float y, float w, float h) => Hd(85 + x * k, y * k, w * k, h * k);
            GUI.DrawTexture(Hd(0, 0, bw, bh), hdBackground);
            for (int i = 0; i < 4; i++) if (boltVisible[i] && hdBolts[i] != null) { var tl = BoltCentre[i] - boltSize[i] / 2; GUI.DrawTexture(Ps(tl.x, tl.y, boltSize[i].x, boltSize[i].y), hdBolts[i]); }
            // Texture rows are bottom-up: the patch rect's GUI y is measured from the top.
            if (hairPatch != null) GUI.DrawTexture(Hd(patchRect.x, bh - patchRect.y - patchRect.height, patchRect.width, patchRect.height), hairPatch);
            for (int i = 0; i < 4; i++)
            {
                var label = i == Cursor && hdLabelsSel[i] != null ? hdLabelsSel[i] : hdLabels[i];
                if (label == null) continue;
                // One scale for all labels (Codex drew them at one letter height): the New game
                // letters span 13 px of 240; left edges aligned at x 30 like the baked labels.
                float unit = 13f / Mathf.Max(1, labelBounds[0].height); var r = labelBounds[i];
                float x = 30 - r.x * unit, y = ItemCentre[i].y - (r.y + r.height / 2f) * unit;
                GUI.DrawTexture(Ps(x, y, label.width * unit, label.height * unit), label);
            }
            if (hdPrompt != null) GUI.DrawTexture(Ps(PromptTopLeft.x, PromptTopLeft.y, promptW, promptH), hdPrompt);
        }
    }
}
