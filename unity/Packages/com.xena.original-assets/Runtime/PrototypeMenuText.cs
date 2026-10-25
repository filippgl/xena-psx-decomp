using UnityEngine;

namespace Xena.Assets
{
    // Menu text in Tenor Sans (Google Fonts, SIL OFL 1.1, Resources/Fonts; Cyrillic,
    // classical humanist capitals close to the menu art). Shadow and glow are drawn as
    // offset copies; used by the main menu (Remaster) and the death/exit menu.
    public static class PrototypeMenuText
    {
        private static Font font; private static GUIStyle style;
        public static Font Font { get { if (font == null) font = Resources.Load<Font>("Fonts/TenorSans-Regular"); return font; } }
        public static bool Available => Font != null;

        private static GUIStyle Style(int size, TextAnchor anchor)
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, richText = false };
            style.font = Font; style.fontSize = size; style.alignment = anchor;
            return style;
        }
        // shadow: soft dark drop shadow; glow (alpha > 0): coloured halo around the letters.
        public static void Draw(Rect rect, string text, int size, Color color, TextAnchor anchor, Color glow = default, float shadow = 0.75f)
        {
            var s = Style(size, anchor); var keep = GUI.color; GUI.color = Color.white;
            float unit = Mathf.Max(1, size / 18f);
            if (glow.a > 0)
                for (int ring = 3; ring >= 1; ring--)
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * Mathf.PI / 6, r = ring * unit * 1.2f;
                        s.normal.textColor = new Color(glow.r, glow.g, glow.b, glow.a * 0.16f / ring);
                        GUI.Label(new Rect(rect.x + Mathf.Cos(a) * r, rect.y + Mathf.Sin(a) * r, rect.width, rect.height), text, s);
                    }
            if (shadow > 0)
                for (int k = 1; k <= 2; k++)
                {
                    s.normal.textColor = new Color(0, 0, 0, shadow / k);
                    GUI.Label(new Rect(rect.x + unit * k * 0.8f, rect.y + unit * k * 0.8f, rect.width, rect.height), text, s);
                }
            s.normal.textColor = color; GUI.Label(rect, text, s);
            GUI.color = keep;
        }
    }
}
