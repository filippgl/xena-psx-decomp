using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Xena.Assets
{
    // Pause menu (docs/xena-pause-menu.md, tools/export_pause_menu.mjs -> Resources/PauseMenu).
    // Original: the scene stays drawn and is darkened (full-screen subtractive grey 22); "ПАУЗА"
    // right-aligned at (160,64) with an underline, a vertical separator at x 180, items right-aligned at
    // x 160, y 90/110/130/150 (Продолжить, Повтор уровня, Опции, Вых), right column shield / sword /
    // scroll with "%dx" counts at x 216; selected item glows (64-vblank pulse); cursor resets on open,
    // no wrap; confirm dialogs start on "Нет". Text: PrototypeMenuText (Original Underdog, Remaster Tenor Sans).
    // Remaster: the same layout in Tenor Sans with a soft dark gradient.
    // The prototype's settings live here now: Опции holds music/effects volume, graphics
    // (Original/Remaster), mouse sensitivity / invert Y and the key bindings (PrototypeInput).
    public sealed class PrototypePauseMenu : MonoBehaviour
    {
        public enum Page { Main, ConfirmRestart, ConfirmExit, Options, Controls }
        public Page Current { get; private set; } = Page.Main;
        public int Cursor { get; private set; }
        public static float MusicVolume = 1, EffectsVolume = 1;
        // Original options screen items (docs/xena-pause-menu.md "Options screen"): Аудио Моно/Стерео (mono = centred
        // pan) and Движения Ходьба/Бег (D_800F278C; read here as the default gait that the run key inverts:
        // INFERRED). The screen is shared with the main menu (OpenFromMainMenu).
        public static bool Stereo = true, AlwaysRun;
        public bool Standalone { get; private set; }
        public static PrototypePauseMenu Active { get; private set; }
        public void OpenFromMainMenu() { Standalone = true; Current = Page.Options; Cursor = 0; Opened++; }
        private bool Shown => input != null && (input.Paused || Standalone);
        public static readonly string[] Items = { "Продолжить", "Повтор уровня", "Опции", "Выход" };
        private static readonly string[] OptionItems = { "Музыка", "Эффекты", "Аудио", "Движения", "Графика", "Чувствительность мыши", "Инверсия камеры", "Разрешение", "Экран", "Управление", "Назад" };
        private const int OptResolution = 7, OptScreen = 8, OptControls = 9, OptBack = 10;
        // Display options (user 2026-10-26): resolution from the monitor's list, screen mode full screen / borderless
        // window / window; saved in PlayerPrefs and applied at start (not with --no-settings: tests keep their size).
        private static readonly FullScreenMode[] Modes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        private static readonly string[] ModeNames = { "Полный экран", "Окно без рамки", "В окне" };
        private static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions) { var v = new Vector2Int(r.width, r.height); if (!list.Contains(v)) list.Add(v); }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y)); return list;
        }
        private static int ModeIndex(FullScreenMode m) { int i = System.Array.IndexOf(Modes, m == FullScreenMode.MaximizedWindow ? FullScreenMode.Windowed : m); return i < 0 ? 2 : i; }
        public static void ApplyDisplay(int width, int height, FullScreenMode mode)
        {
            Screen.SetResolution(width, height, mode);
            PlayerPrefs.SetInt("xena.resW", width); PlayerPrefs.SetInt("xena.resH", height); PlayerPrefs.SetInt("xena.screenMode", ModeIndex(mode));
        }
        private void AdjustResolution(int delta)
        {
            var list = Resolutions(); int at = list.FindIndex(v => v.x == Screen.width && v.y == Screen.height); if (at < 0) at = list.Count - 1;
            var next = list[Mathf.Clamp(at + delta, 0, list.Count - 1)]; ApplyDisplay(next.x, next.y, Screen.fullScreenMode); PrototypeSfx.Play2D(0x39, ui: true);
        }
        private void AdjustScreen(int delta)
        {
            int at = Mathf.Clamp(ModeIndex(Screen.fullScreenMode) + delta, 0, Modes.Length - 1);
            ApplyDisplay(Screen.width, Screen.height, Modes[at]); PrototypeSfx.Play2D(0x39, ui: true);
        }
        private PrototypeInput input; private PrototypeLevelScripts scripts; private bool wasPaused;
        private readonly Dictionary<string, Texture2D> labels = new Dictionary<string, Texture2D>(); private Texture2D shield, sword, scroll, hdShield, hdSword, hdScroll;
        [Serializable] private sealed class Label { public string name; public int[] originOffset; public int extent; }
        [Serializable] private sealed class LabelFile { public Label[] labels; }
        private readonly Dictionary<string, Label> layout = new Dictionary<string, Label>();
        private static readonly Dictionary<string, string> KeyNames = new Dictionary<string, string> {
            { "Forward", "Вперёд" }, { "Back", "Назад" }, { "Left", "Влево" }, { "Right", "Вправо" }, { "Sprint", "Бег" }, { "Jump", "Прыжок" },
            { "Sword", "Меч" }, { "Kick", "Удар ногой" }, { "Block", "Блок" }, { "Reset", "Сброс позиции" }, { "CenterCamera", "Камера за спину" }, { "Chakram", "Чакрам" } };
        public int Opened { get; private set; }

        private void Awake()
        {
            input = GetComponent<PrototypeInput>(); scripts = GetComponent<PrototypeLevelScripts>();
            shield = Resources.Load<Texture2D>("PauseMenu/icons/shield"); sword = Resources.Load<Texture2D>("PauseMenu/icons/sword"); scroll = Resources.Load<Texture2D>("PauseMenu/icons/scroll");
            foreach (var t in new[] { shield, sword, scroll }) if (t != null) t.filterMode = FilterMode.Point;
            // Remaster icons: Codex shield (PauseMenu/icons/shield-hd); the sword and scroll are the same
            // sprites as the pickups (UP_SWORD / SCROLL tile 0), whose Codex HD versions already exist.
            hdShield = Resources.Load<Texture2D>("PauseMenu/icons/shield-hd"); hdSword = Resources.Load<Texture2D>("Props/Pickups/up_sword-hd"); hdScroll = Resources.Load<Texture2D>("Props/Pickups/scroll-hd");
            var json = Resources.Load<TextAsset>("PauseMenu/pause_menu");
            if (json != null) foreach (var l in JsonUtility.FromJson<LabelFile>(json.text).labels) if (l != null && l.name != null) layout[l.name] = l;
            MusicVolume = PlayerPrefs.GetFloat("xena.music", 1); EffectsVolume = PlayerPrefs.GetFloat("xena.effects", 1);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--no-settings") < 0 && PlayerPrefs.HasKey("xena.resW"))
                Screen.SetResolution(PlayerPrefs.GetInt("xena.resW"), PlayerPrefs.GetInt("xena.resH"), Modes[Mathf.Clamp(PlayerPrefs.GetInt("xena.screenMode", 2), 0, Modes.Length - 1)]);
            Stereo = PlayerPrefs.GetInt("xena.stereo", 1) != 0; AlwaysRun = PlayerPrefs.GetInt("xena.alwaysRun", 0) != 0; Active = this;
        }

        private void Update()
        {
            if (input == null) return;
            if (input.Paused && !wasPaused) { Current = Page.Main; Cursor = 0; Opened++; }
            wasPaused = input.Paused;
            // Esc inside a sub-page goes back instead of resuming (the input only toggles on the main page).
            input.PauseLocked = input.Paused && Current != Page.Main;
            if (!Shown || input.IsRebinding) return;
            var k = Keyboard.current; var pad = Gamepad.current;
            bool up = k != null && (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) || pad != null && pad.dpad.up.wasPressedThisFrame;
            bool down = k != null && (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) || pad != null && pad.dpad.down.wasPressedThisFrame;
            bool left = k != null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) || pad != null && pad.dpad.left.wasPressedThisFrame;
            bool right = k != null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) || pad != null && pad.dpad.right.wasPressedThisFrame;
            bool confirm = k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) || pad != null && pad.buttonSouth.wasPressedThisFrame;
            bool back = k != null && k.escapeKey.wasPressedThisFrame || pad != null && pad.buttonNorth.wasPressedThisFrame;
            if (up) Move(-1); if (down) Move(1);
            if (left) Adjust(-1); if (right) Adjust(1);
            if (confirm) Select();
            if (back && Current != Page.Main) Back();
        }
        private int Count => Current == Page.Main ? Items.Length : Current == Page.Options ? OptionItems.Length : Current == Page.Controls ? PrototypeInput.BindingKeys.Length + 2 : 2;
        public void Move(int delta) { int was = Cursor; Cursor = Mathf.Clamp(Cursor + delta, 0, Count - 1); if (Cursor != was) PrototypeSfx.Play2D(0x39, ui: true); } // no wrap; menu move 0x39
        public void Back()
        {
            if (Current == Page.Controls) { Current = Page.Options; Cursor = OptControls; }
            else if (Standalone) { Standalone = false; input.SaveSettings(); Current = Page.Main; Cursor = 0; }
            else { Current = Page.Main; Cursor = 0; }
        }
        private void Adjust(int delta)
        {
            if (Current != Page.Options) return;
            if (Cursor == 0) { MusicVolume = Mathf.Clamp01(MusicVolume + delta * 0.05f); PlayerPrefs.SetFloat("xena.music", MusicVolume); }
            if (Cursor == 1) { EffectsVolume = Mathf.Clamp01(EffectsVolume + delta * 0.05f); PlayerPrefs.SetFloat("xena.effects", EffectsVolume); PrototypeSfx.Play2D(0x26, ui: true); } // volume test sound 0x26
            if (Cursor == 2) { Stereo = delta > 0; PlayerPrefs.SetInt("xena.stereo", Stereo ? 1 : 0); PrototypeSfx.Play2D(0x39, ui: true); } // LEFT mono, RIGHT stereo
            if (Cursor == 3) { AlwaysRun = delta > 0; PlayerPrefs.SetInt("xena.alwaysRun", AlwaysRun ? 1 : 0); PrototypeSfx.Play2D(0x39, ui: true); }
            if (Cursor == 4) ToggleGraphics();
            if (Cursor == 5) input.Preferences.Data.mouseSensitivity = Mathf.Clamp(input.Preferences.Data.mouseSensitivity + delta * 0.01f, 0.03f, 0.3f);
            if (Cursor == 6) input.Preferences.Data.invertY = !input.Preferences.Data.invertY;
            if (Cursor == OptResolution) AdjustResolution(delta);
            if (Cursor == OptScreen) AdjustScreen(delta);
        }
        private static void ToggleGraphics() { if (PrototypeGraphics.Active != null) PrototypeGraphics.Active.SetMode(PrototypeGraphics.Active.Mode == XenaVisualMode.Original ? XenaVisualMode.Remaster : XenaVisualMode.Original); }
        public void Select()
        {
            PrototypeSfx.Play2D(0, ui: true); // confirm 0x00
            switch (Current)
            {
                case Page.Main:
                    if (Cursor == 0) input.SetPaused(false);
                    else if (Cursor == 1) { Current = Page.ConfirmRestart; Cursor = 1; }
                    else if (Cursor == 2) { Current = Page.Options; Cursor = 0; }
                    else { Current = Page.ConfirmExit; Cursor = 1; }
                    break;
                case Page.ConfirmRestart:
                    if (Cursor == 1) { Back(); break; }
                    input.SetPaused(false); UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                    break;
                case Page.ConfirmExit:
                    if (Cursor == 1) { Back(); break; }
                    // Original: Вых -> GAME OVER video -> main menu.
                    input.SetPaused(false); var flow = GetComponent<PrototypeGameOver>(); if (flow != null) flow.PlayGameOverFromMenu();
                    break;
                case Page.Options:
                    if (Cursor == 4 || Cursor == 6) Adjust(1);
                    else if (Cursor == 2) Adjust(Stereo ? -1 : 1);
                    else if (Cursor == 3) Adjust(AlwaysRun ? -1 : 1);
                    else if (Cursor == OptScreen) AdjustScreen(ModeIndex(Screen.fullScreenMode) == Modes.Length - 1 ? -(Modes.Length - 1) : 1);
                    else if (Cursor == OptResolution) AdjustResolution(1);
                    else if (Cursor == OptControls) { Current = Page.Controls; Cursor = 0; }
                    else if (Cursor == OptBack) { input.SaveSettings(); Back(); }
                    break;
                case Page.Controls:
                    int keys = PrototypeInput.BindingKeys.Length;
                    if (Cursor < keys) input.BeginRebind(PrototypeInput.BindingKeys[Cursor]);
                    else if (Cursor == keys) input.ResetPreferences();
                    else { input.SaveSettings(); Back(); }
                    break;
            }
        }

        private static bool Remaster => PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
        private void OnGUI()
        {
            GUI.depth = Standalone ? -1150 : -900; // over the main menu (-1100) when opened from it
            if (!Shown || Event.current.type != EventType.Repaint) return;
            float scale = Screen.height / 240f, left = (Screen.width - 320 * scale) / 2;
            Rect R(float x, float y, float w, float h) => new Rect(left + x * scale, y * scale, w * scale, h * scale);
            // Darken: Original subtracts grey 22 (about 9 %); Remaster a deeper soft vignette.
            var c = GUI.color; GUI.color = new Color(0, 0, 0, Standalone ? 0.88f : Remaster ? 0.55f : 0.09f * 3.2f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = c;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 * 60f / 64f);
            if (Current == Page.Options || Current == Page.Controls) { DrawOptions(R, scale, pulse); return; }
            // Title, underline, separator.
            Text(R, 160, 64, "ПАУЗА", "title", Color.white, false, scale, pulse);
            Line(R(105, 79, 52, 1)); Line(R(180, 49, 1, 130));
            if (Current == Page.Main)
                for (int i = 0; i < Items.Length; i++) Text(R, 160, 90 + 20 * i, Items[i], "item" + i, new Color(0.49f, 0.49f, 0.49f), i == Cursor, scale, pulse);
            else
            {
                string question = Current == Page.ConfirmRestart ? "Повтор уровня?" : "Ты уверен?";
                Text(R, 160, 90, question, null, new Color(0.49f, 0.49f, 0.49f), false, scale, pulse);
                Text(R, 160, 120, "Да", null, new Color(0.49f, 0.49f, 0.49f), Cursor == 0, scale, pulse);
                Text(R, 160, 140, "Нет", null, new Color(0.49f, 0.49f, 0.49f), Cursor == 1, scale, pulse);
            }
            // Right column: armour (none on level 01), sword level, scrolls read.
            Icon(R, shield, 224, 66, Remaster ? hdShield : null); Icon(R, sword, 224, 110, Remaster ? hdSword : null); Icon(R, scroll, 231, 155, Remaster ? hdScroll : null);
            int[] counts = { 0, scripts != null ? scripts.SwordLevel : 0, scripts != null ? scripts.ScrollsRead : 0 };
            for (int i = 0; i < 3; i++) Text(R, 216, new[] { 59, 103, 148 }[i], counts[i] + "x", null, new Color(0.43f, 0.43f, 0.43f), false, scale, pulse);
        }
        private void DrawOptions(Func<float, float, float, float, Rect> R, float scale, float pulse)
        {
            var grey = new Color(0.49f, 0.49f, 0.49f);
            if (Current == Page.Options)
            {
                Text(R, 160, 40, "ОПЦИИ", null, Color.white, false, scale, pulse);
                string[] values = { Mathf.RoundToInt(MusicVolume * 100) == 0 ? "Вык" : Mathf.RoundToInt(MusicVolume * 100) + "%", Mathf.RoundToInt(EffectsVolume * 100) == 0 ? "Вык" : Mathf.RoundToInt(EffectsVolume * 100) + "%",
                    Stereo ? "Стерео" : "Моно", AlwaysRun ? "Бег" : "Ходьба",
                    Remaster ? "Ремастер" : "Оригинал", input.Preferences.Data.mouseSensitivity.ToString("0.00"), input.Preferences.Data.invertY ? "Вкл" : "Вык", Screen.width + "×" + Screen.height, ModeNames[ModeIndex(Screen.fullScreenMode)], "", "" };
                for (int i = 0; i < OptionItems.Length; i++)
                {
                    Text(R, 160, 56 + 16 * i, OptionItems[i], null, grey, i == Cursor, scale, pulse);
                    if (values[i] != "") Text(R, 260, 56 + 16 * i, values[i], null, grey, i == Cursor, scale, pulse);
                }
                return;
            }
            Text(R, 160, 30, "УПРАВЛЕНИЕ", null, Color.white, false, scale, pulse);
            int keys = PrototypeInput.BindingKeys.Length, first = Mathf.Clamp(Cursor - 6, 0, Mathf.Max(0, keys + 2 - 9));
            for (int row = 0; row < 9 && first + row < keys + 2; row++)
            {
                int i = first + row; string label = i < keys ? (KeyNames.TryGetValue(PrototypeInput.BindingKeys[i], out var ru) ? ru : PrototypeInput.BindingKeys[i]) : i == keys ? "По умолчанию" : "Назад";
                Text(R, 160, 56 + 18 * row, label, null, grey, i == Cursor, scale, pulse);
                if (i < keys) Text(R, 290, 56 + 18 * row, input.IsRebinding && i == Cursor ? "..." : input.BindingLabel(PrototypeInput.BindingKeys[i]), null, grey, i == Cursor, scale, pulse);
            }
        }
        private static void Line(Rect r) { var c = GUI.color; GUI.color = new Color(0, 0, 0, 0.9f); GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width, Mathf.Max(1, r.height)), Texture2D.whiteTexture); GUI.color = Color.white; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = c; }
        // Original sprite rect; the HD texture is fitted into it keeping its own aspect.
        private static void Icon(Func<float, float, float, float, Rect> R, Texture2D t, float cx, float cy, Texture2D hd)
        {
            if (t == null) return;
            if (hd == null) { GUI.DrawTexture(R(cx - t.width / 2, cy - t.height / 2, t.width, t.height), t); return; }
            float h = t.height * 1.15f, w = h * hd.width / hd.height; if (w > t.width * 1.6f) { w = t.width * 1.6f; h = w * hd.height / hd.width; }
            GUI.DrawTexture(R(cx - w / 2, cy - h / 2, w, h), hd, ScaleMode.StretchToFill);
        }
        // Right-aligned text at the anchor: Original draws the exported label when there is one.
        private void Text(Func<float, float, float, float, Rect> R, float x, float y, string text, string label, Color colour, bool selected, float scale, float pulse)
        {
            var area = R(x - 300, y - 2, 300, 18);
            if (!Remaster && selected) colour = new Color(1, 1, 0.3f); // original: yellow pulse on the selected line
            PrototypeMenuText.Draw(area, text, Mathf.RoundToInt(11 * scale), selected && Remaster ? Color.Lerp(colour, new Color(1, 0.9f, 0.4f), 0.5f + 0.5f * pulse) : colour * 1.6f, TextAnchor.MiddleRight,
                selected ? new Color(1, 0.8f, 0.25f, pulse) : default);
        }
    }
}
