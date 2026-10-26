using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // The original text renderer (docs/xena-message-window.md, docs/xena-main-menu.md; font atlas and
    // glyph table from tools/export_message_window.mjs -> Resources/MessageWindow): 16x16 cells of
    // XENAFONT.TIM, per-glyph width / advance / drawDx / yOffset, lines 17 px apart; alignment 0/3 left,
    // 1 right (x - extent, trailing spaces count), 2 centre (each line shifted by trunc(extent/2));
    // style 8 = half-dark shadow copies at (+1,+1) and (+2,+2); glyphs modulated by the call colour
    // (128 = full) and added. Russian text maps to the game's glyph set (digits use their own cells).
    public static class PrototypePs1Text
    {
        [Serializable] private sealed class Glyph { public int @byte; public string @char; public int glyph; public int[] cell; public int width, advance, drawDx, yOffset; }
        [Serializable] private sealed class Font { public Glyph[] glyphs; public int cell, lineStep; }
        [Serializable] private sealed class File { public Font font; }
        private static Dictionary<char, Glyph> map; private static Texture2D atlas; private static int lineStep = 17;
        private static readonly Dictionary<char, char> Fallback = new Dictionary<char, char> { { 'й', 'и' }, { 'Й', 'И' }, { 'ё', 'е' }, { 'Ё', 'Е' }, { 'щ', 'ш' }, { 'Щ', 'Ш' }, { 'ъ', 'ь' }, { 'Ъ', 'Ь' }, { 'X', 'Х' }, { 'x', 'х' }, { 'O', 'О' } };
        public static bool Available { get { Load(); return atlas != null; } }
        private static void Load()
        {
            if (map != null) return;
            map = new Dictionary<char, Glyph>();
            var json = Resources.Load<TextAsset>("MessageWindow/message_window"); atlas = Resources.Load<Texture2D>("MessageWindow/xenafont_glyphs");
            if (json == null || atlas == null) { atlas = null; return; }
            atlas.filterMode = FilterMode.Point;
            var font = JsonUtility.FromJson<File>(json.text).font; if (font.lineStep > 0) lineStep = font.lineStep;
            foreach (var g in font.glyphs)
            {
                if (g.@byte >= 0x30 && g.@byte <= 0x39)
                {
                    // Digits keep their own cells; a digit cell that draws a Cyrillic letter (0x30 = 'О', ...)
                    // also serves that letter when no letter cell exists.
                    map[(char)g.@byte] = g;
                    if (!string.IsNullOrEmpty(g.@char) && g.@char.Length == 1 && g.@char[0] > 0x400 && !map.ContainsKey(g.@char[0])) map[g.@char[0]] = g;
                    continue;
                }
                if (g.@byte == 3) { map['○'] = g; continue; }
                if (g.@byte == 4) { map['ⓧ'] = g; continue; }
                if (!string.IsNullOrEmpty(g.@char) && g.@char.Length == 1)
                {
                    char c = g.@char[0];
                    // Letters (byte >= 0x41) win over punctuation cells that reuse a Cyrillic look.
                    if (!map.ContainsKey(c) || g.@byte >= 0x41) map[c] = g;
                }
            }
        }
        private static Glyph Find(char c)
        {
            if (map.TryGetValue(c, out var g)) return g;
            if (Fallback.TryGetValue(c, out var f) && map.TryGetValue(f, out g)) return g;
            return map.TryGetValue(' ', out g) ? g : null;
        }
        public static int Extent(string line) { Load(); int e = 0; foreach (char c in line) { var g = Find(c); if (g != null) e += g.advance; } return e; }

        // Draws text in 320x240 screen pixels mapped by `toScreen` (x, y, w, h -> GUI rect).
        public static void Draw(Func<float, float, float, float, Rect> toScreen, string text, float x, float y, int align, Color32 colour, bool shadow = true, float alpha = 1)
        {
            Load(); if (atlas == null) return;
            var lines = text.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l]; int extent = Extent(line);
                float pen = align == 1 ? x - extent : align == 2 ? x - extent / 2 : x, py = y + l * lineStep;
                foreach (char c in line)
                {
                    var g = Find(c); if (g == null) continue;
                    if (c != ' ')
                    {
                        var uv = new Rect(g.cell[0] / (float)atlas.width, 1 - (g.cell[1] + 16) / (float)atlas.height, 16f / atlas.width, 16f / atlas.height);
                        var keep = GUI.color;
                        if (shadow)
                        {
                            GUI.color = new Color(0, 0, 0, 0.5f * alpha);
                            GUI.DrawTextureWithTexCoords(toScreen(pen + g.drawDx + 1, py + g.yOffset + 1, 16, 16), atlas, uv);
                            GUI.DrawTextureWithTexCoords(toScreen(pen + g.drawDx + 2, py + g.yOffset + 2, 16, 16), atlas, uv);
                        }
                        GUI.color = new Color(colour.r / 128f, colour.g / 128f, colour.b / 128f, alpha);
                        GUI.DrawTextureWithTexCoords(toScreen(pen + g.drawDx, py + g.yOffset, 16, 16), atlas, uv);
                        GUI.color = keep;
                    }
                    pen += g.advance;
                }
            }
        }
    }
}
