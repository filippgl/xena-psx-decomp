using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace Xena.Assets
{
    // Boot videos before the main menu (docs/xena-fmv.md, func_8006C1B4): EA_LOGO, UNILOGO, then
    // INTRO. On the first boot the two logos cannot be skipped; INTRO is skipped by START only
    // (here Esc / Enter / pad Start). Then the main menu (PrototypeMainMenu) appears; it ignores
    // input while a video plays. Remaster plays the Real-ESRGAN 4x versions (<name>-hd,
    // tools/export_fmv.py), Original the 320x240 originals, both 4:3 on black.
    public sealed class PrototypeBootVideos : MonoBehaviour
    {
        public static readonly string[] Sequence = { "ea_logo", "unilogo", "intro" };
        public static bool Played { get; private set; }
        public static bool Playing { get; private set; }
        // Frame of the last video change: the press that skipped a video must not reach the menu.
        public static int ChangedFrame { get; private set; } = -1;
        public int Index { get; private set; } = -1;
        public string ClipName { get; private set; }
        private VideoPlayer player; private RenderTexture frame; private float started; private AudioSource source;

        private void Start()
        {
            bool smoke = false;
            foreach (string arg in System.Environment.GetCommandLineArgs()) if (arg.EndsWith("-smoke")) smoke = true;
            if (!Played && !smoke && !Application.isEditor) Begin();
        }
        public void Begin() { Played = true; Index = -1; Next(); }
        private void Next()
        {
            Index++; ChangedFrame = Time.frameCount;
            if (player != null) { player.Stop(); Destroy(player); player = null; }
            if (Index >= Sequence.Length) { Playing = false; ClipName = null; return; }
            bool remaster = PrototypeGraphics.Active == null || PrototypeGraphics.Active.Mode == XenaVisualMode.Remaster;
            var clip = remaster ? Resources.Load<VideoClip>("Video/" + Sequence[Index] + "-hd") : null;
            if (clip == null) clip = Resources.Load<VideoClip>("Video/" + Sequence[Index]);
            if (clip == null) { Next(); return; }
            Playing = true; ClipName = clip.name; started = Time.unscaledTime;
            if (frame == null || frame.width != (int)clip.width) { if (frame != null) frame.Release(); frame = new RenderTexture((int)clip.width, (int)clip.height, 0) { filterMode = remaster ? FilterMode.Bilinear : FilterMode.Point }; }
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false; player.clip = clip; player.renderMode = VideoRenderMode.RenderTexture; player.targetTexture = frame;
            // The main menu pauses the AudioListener behind the videos: this source ignores that.
            if (source == null) { var holder = new GameObject("BootVideoAudio"); holder.transform.SetParent(transform, false); source = holder.AddComponent<AudioSource>(); source.ignoreListenerPause = true; source.playOnAwake = false; source.spatialBlend = 0; }
            player.audioOutputMode = VideoAudioOutputMode.AudioSource; player.EnableAudioTrack(0, true); player.SetTargetAudioSource(0, source);
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.Play();
        }
        // Test hook: jump to the last half second so the natural end -> next video path runs quickly.
        public void SeekNearEndForTest() { if (player != null && player.clip != null) player.time = player.clip.length - 0.5; }
        // START skips INTRO only (the logos are not skippable on the first boot).
        public bool Skip() { if (!Playing || Sequence[Index] != "intro") return false; Next(); return true; }
        private void Update()
        {
            if (!Playing) return;
            bool start = Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame)
                || Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (start) Skip();
            // isPlaying is false before the first frame is prepared.
            if (Playing && player != null && Time.unscaledTime - started > 1 && !player.isPlaying) Next();
        }
        private void OnGUI()
        {
            GUI.depth = -1200;
            if (!Playing || Event.current.type != EventType.Repaint) return;
            var tint = GUI.color; GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = tint;
            float h = Screen.height, w = h * 4 / 3;
            if (frame != null) GUI.DrawTexture(new Rect((Screen.width - w) / 2, 0, w, h), frame, ScaleMode.StretchToFill);
        }
        private void OnDestroy() { if (frame != null) frame.Release(); if (Playing) Playing = false; }
    }
}
