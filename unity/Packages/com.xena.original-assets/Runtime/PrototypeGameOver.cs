using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Xena.Assets
{
    // Death -> game over flow (docs/xena-hit-reactions.md section 5; native capture
    // build/gameover, 600 damage poked into Xena +0x12 on level 01):
    // - clip 0x91, then game state 0x11 about 230 native frames after the hit (3.8 s);
    //   here MenuStep = 65 clip frames + 45 steps (the 90-tick wait, 60 Hz ticks INFERRED).
    // - Continues D_800F277C are cleared on a new game and level 01 has no continue
    //   pickup, so func_8006D3EC always opens the menu: "Повтор уровня" (default) /
    //   "Вых" (sic: the Russian translation truncates "Выход"; drawn in full here) over
    //   the death view. Retry: the level restarts at its start, 500 hp (native: state 0xC,
    //   level start view). Exit: "Хочешь выйти?" Да / Не(т), default Нет; Да -> state 0x13
    //   plays MEDIA/CHAKDETH.STR (GAME OVER, 5.8 s) and returns to the main menu (state 2).
    //   After the video the level reloads behind the main menu (PrototypeMainMenu).
    // Remaster plays the Real-ESRGAN 4x video chakdeth-hd when present.
    // Menu text uses the GUI font (xenafont.tim is not exported yet).
    public sealed class PrototypeGameOver : MonoBehaviour
    {
        public enum Phase { None, Menu, Confirm, Video }
        public const int MenuStep = 65 + 45;
        public Phase State { get; private set; }
        public int Selected { get; private set; }
        public bool VideoStarted { get; private set; }
        public string VideoName { get; private set; }
        public static int Restarts { get; private set; }
        private PrototypeOriginalDamage damage;
        private VideoPlayer player; private RenderTexture frame;
        private GUIStyle style; private float videoStart;
        public static readonly string[] MenuItems = { "Повтор уровня", "Выход" };
        public static readonly string[] ConfirmItems = { "Да", "Нет" };

        private void Awake() { damage = GetComponent<PrototypeOriginalDamage>(); }
        private void Update()
        {
            if (State == Phase.None)
            {
                if (damage != null && damage.Dead && damage.DeathFrame >= MenuStep) { State = Phase.Menu; Selected = 0; }
                return;
            }
            if (State == Phase.Video)
            {
                // VideoPlayer.isPlaying is false before the first frame is prepared.
                if (player == null || (Time.unscaledTime - videoStart > 1 && !player.isPlaying) || Pressed(Key.Escape)) { PrototypeMainMenu.PendingShow = true; Retry(); }
                return;
            }
            if (Pressed(Key.UpArrow) || Pressed(Key.W) || Gamepad.current?.dpad.up.wasPressedThisFrame == true) Move(-1);
            if (Pressed(Key.DownArrow) || Pressed(Key.S) || Gamepad.current?.dpad.down.wasPressedThisFrame == true) Move(1);
            if (Pressed(Key.Enter) || Pressed(Key.Space) || Gamepad.current?.buttonSouth.wasPressedThisFrame == true) Confirm();
        }
        private static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        public void Move(int delta) { if (State == Phase.Menu || State == Phase.Confirm) Selected = Mathf.Clamp(Selected + delta, 0, 1); }
        public void Confirm()
        {
            if (State == Phase.Menu) { if (Selected == 0) Retry(); else { State = Phase.Confirm; Selected = 1; } }
            else if (State == Phase.Confirm) { if (Selected == 0) PlayGameOver(); else { State = Phase.Menu; Selected = 1; } }
        }
        private void PlayGameOver()
        {
            State = Phase.Video; videoStart = Time.unscaledTime;
            // Remaster: Real-ESRGAN 4x version (tools/export_game_over_fmv.py) when it exists.
            bool remaster = PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
            var clip = remaster ? Resources.Load<VideoClip>("Video/chakdeth-hd") : null;
            if (clip == null) clip = Resources.Load<VideoClip>("Video/chakdeth");
            if (clip == null) return;
            VideoName = clip.name;
            frame = new RenderTexture((int)clip.width, (int)clip.height, 0) { filterMode = remaster ? FilterMode.Bilinear : FilterMode.Point };
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false; player.clip = clip; player.renderMode = VideoRenderMode.RenderTexture; player.targetTexture = frame;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            var music = GetComponent<PrototypeMusic>();
            if (music != null && music.Muted) player.audioOutputMode = VideoAudioOutputMode.None;
            player.Play(); VideoStarted = true;
        }
        public void Retry()
        {
            Restarts++;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnGUI()
        {
            GUI.depth = -1000; // above the HUD, messages and the prototype overlay (set on every event)
            if (State == Phase.None || Event.current.type != EventType.Repaint) return;
            if (State == Phase.Video)
            {
                // Texture2D.blackTexture is transparent: fill the bars with opaque black.
                var tint = GUI.color; GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = tint;
                float h = Screen.height, w = h * 4 / 3;
                if (frame != null) GUI.DrawTexture(new Rect((Screen.width - w) / 2, 0, w, h), frame, ScaleMode.StretchToFill);
                return;
            }
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.Max(16, Screen.height / 22);
            // Native layout (320x240): title/first line centred at y~139, next lines 20 px below.
            string[] lines = State == Phase.Menu ? MenuItems : new[] { "Хочешь выйти?", ConfirmItems[0], ConfirmItems[1] };
            int first = State == Phase.Menu ? 0 : 1;
            float scale = Screen.height / 240f, y = 139 * scale;
            for (int i = 0; i < lines.Length; i++)
            {
                bool selected = i - first == Selected && i >= first;
                var rect = new Rect(0, y + i * 20 * scale - 10 * scale, Screen.width, 20 * scale);
                style.normal.textColor = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), lines[i], style);
                style.normal.textColor = selected ? new Color(1f, 0.93f, 0.35f) : Color.white; GUI.Label(rect, lines[i], style);
            }
        }
        private void OnDestroy() { if (frame != null) frame.Release(); }
    }
}
