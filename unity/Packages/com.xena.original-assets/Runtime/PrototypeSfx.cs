using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Original sound effects (docs/xena-sfx.md; renders from tools/export_sfx.mjs, installed by
    // tools/install_sfx.py into Resources/Sfx). Playback follows func_800847C0(id, note, vol, pan, pos):
    // renders are note 60 / vol 127 / pan 64, other notes use the exported note file or pitch
    // 2^((note-60)/12); vol scales linearly (SPU vol*129). Positional voices are re-evaluated every frame
    // by func_800845C8 against the CAMERA: 3D distance d (units), vol 127 up to 2500, then
    // 127 - (d-2500)*64/10000, floor 10; behind the camera vol -= vol>>2; pan = 64 + rel*64/3969
    // (rel = azimuth from the view direction, 65536/turn, rear mirrored to the front). 24 voices; ids with
    // 0x1000 are CD-XA clips on one stream (a new clip replaces the current one). Loops (0x13, 0x3D, ...)
    // use the raw loop body (gain APPROXIMATED). Effects volume = PrototypePauseMenu.EffectsVolume.
    public sealed class PrototypeSfx : MonoBehaviour
    {
        [Serializable] private sealed class Entry { public int id; public string file; public bool loop; public int[] notes; public string[] noteFiles; public string loopFile; public float loopRate60, loopGain; }
        [Serializable] private sealed class Index { public Entry[] sounds; }
        private sealed class Voice { public AudioSource source; public int id = -1; public bool positional, ui; public Vector3 position; public Transform follow; public int vol; public float gain; public bool xa; }
        public const int Voices = 24, HalfView = 3969;
        private static PrototypeSfx instance;
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Voice[] voices = new Voice[Voices];
        private readonly System.Random random = new System.Random(1);
        // Test log: (id, note, vol) of every started sound.
        public static readonly List<(int id, int note, int vol)> Log = new List<(int, int, int)>();
        public static int Available => Instance.entries.Count;

        public static PrototypeSfx Instance
        {
            get
            {
                if (instance == null) { var go = new GameObject("PrototypeSfx"); DontDestroyOnLoad(go); instance = go.AddComponent<PrototypeSfx>(); instance.Load(); }
                return instance;
            }
        }
        private void Load()
        {
            var json = Resources.Load<TextAsset>("Sfx/index");
            if (json != null) foreach (var e in JsonUtility.FromJson<Index>(json.text).sounds) entries[e.id] = e;
            for (int i = 0; i < Voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0; s.dopplerLevel = 0;
                voices[i] = new Voice { source = s };
            }
        }
        private AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!clips.TryGetValue(name, out var c)) clips[name] = c = Resources.Load<AudioClip>("Sfx/" + name);
            return c;
        }
        public int Rand(int a, int b) => random.Next(a, b + 1); // func_800B4350 inclusive

        // 2D (func_80084A34); ui: also plays while the menus pause the listener.
        public static void Play2D(int id, int note = 60, int vol = 127, bool ui = false) => Instance.Start(id, note, vol, false, Vector3.zero, null, ui);
        public static void PlayAt(int id, Vector3 position, int note = 60, int vol = 127) => Instance.Start(id, note, vol, true, position, null, false);
        public static void PlayOn(int id, Transform follow, int note = 60, int vol = 127) => Instance.Start(id, note, vol, true, follow.position, follow, false);
        public static void PlayRandAt(int a, int b, Vector3 position, int note = 60, int vol = 127) => PlayAt(Instance.Rand(a, b), position, note, vol);
        public static bool IsPlaying(int first, int last)
        {
            foreach (var v in Instance.voices) if (v.id >= first && v.id <= last && v.source.isPlaying) return true;
            return false;
        }
        public static bool XaPlaying => IsPlaying(0x1000, 0x1FFF);
        // func_80084E80(list, n, pos, chance): if rand(0,4095) < chance play a random entry, re-rolled while it is
        // the XA clip already playing. Returns the id played or -1.
        public static int PlayListAt(int[] list, Vector3 position, int chance)
        {
            var s = Instance; if (s.Rand(0, 4095) >= chance) return -1;
            int id = list[s.Rand(0, list.Length - 1)];
            for (int guard = 0; guard < 8 && list.Length > 1 && IsPlaying(id, id); guard++) id = list[s.Rand(0, list.Length - 1)];
            PlayAt(id, position); return id;
        }
        public static void Stop(int id) { foreach (var v in Instance.voices) if (v.id == id) { v.source.Stop(); v.id = -1; } }
        public static void StopAll() { foreach (var v in Instance.voices) { v.source.Stop(); v.id = -1; } }

        private void Start(int id, int note, int vol, bool positional, Vector3 position, Transform follow, bool ui)
        {
            if (!entries.TryGetValue(id, out var e)) return;
            bool xa = (id & 0x1000) != 0;
            AudioClip clip; float pitch = 1, gain = 1; bool loop = false;
            if (e.loop && !string.IsNullOrEmpty(e.loopFile))
            {
                clip = Clip(e.loopFile); loop = true; gain = e.loopGain;
                pitch = e.loopRate60 * Mathf.Pow(2, (note - 60) / 12f);
            }
            else
            {
                int k = e.notes != null ? Array.IndexOf(e.notes, note) : -1;
                clip = k >= 0 ? Clip(e.noteFiles[k]) : Clip(e.file);
                if (k < 0 && !xa) pitch = Mathf.Pow(2, (note - 60) / 12f);
            }
            if (clip == null) return;
            Voice voice = null;
            if (xa) foreach (var v in voices) if (v.xa && v.source.isPlaying) { voice = v; break; } // one XA stream
            if (voice == null) foreach (var v in voices) if (!v.source.isPlaying) { voice = v; break; }
            if (voice == null) foreach (var v in voices) if (!v.source.loop) { voice = v; break; } // steal a one-shot
            if (voice == null) return;
            voice.source.Stop();
            voice.id = id; voice.positional = positional; voice.position = position; voice.follow = follow; voice.vol = vol; voice.gain = gain; voice.ui = ui; voice.xa = xa;
            voice.source.clip = clip; voice.source.loop = loop; voice.source.pitch = pitch; voice.source.ignoreListenerPause = ui;
            Apply(voice); voice.source.Play();
            Log.Add((id, note, vol)); if (Log.Count > 512) Log.RemoveAt(0);
        }

        // func_800845C8: distance volume and pan from the camera.
        public static void Attenuate(Vector3 position, Transform camera, out int vol, out int pan)
        {
            vol = 127; pan = 64;
            if (camera == null) return;
            Vector3 d = (position - camera.position) * 400;
            int distance = Mathf.RoundToInt(d.magnitude);
            if (distance > 2500) vol = Mathf.Max(10, 127 - (distance - 2500) * 64 / 10000);
            Vector3 flat = new Vector3(d.x, 0, d.z), forward = camera.forward; forward.y = 0;
            if (flat.sqrMagnitude < 1e-6f || forward.sqrMagnitude < 1e-6f) return;
            int rel = Mathf.RoundToInt(Vector3.SignedAngle(forward, flat, Vector3.up) / 360f * 65536); // + = right
            if (Mathf.Abs(rel) > 0x4000) { vol -= vol >> 2; rel = rel > 0 ? 0x8000 - rel : -0x8000 - rel; }
            pan = Mathf.Clamp(64 + rel * 64 / HalfView, 0, 127);
        }
        private void Apply(Voice v)
        {
            int dist = 127, pan = 64;
            if (v.positional)
            {
                if (v.follow != null) v.position = v.follow.position;
                var cam = Camera.main; Attenuate(v.position, cam != null ? cam.transform : null, out dist, out pan);
            }
            v.source.volume = Mathf.Clamp01(dist / 127f * v.vol / 127f * v.gain * PrototypePauseMenu.EffectsVolume);
            v.source.panStereo = PrototypePauseMenu.Stereo ? (pan - 64) / 64f : 0; // options Аудио: Моно centres every voice
        }
        private void LateUpdate()
        {
            foreach (var v in voices)
            {
                if (v.id < 0) continue;
                if (!v.source.isPlaying && !AudioListener.pause) { v.id = -1; continue; }
                Apply(v);
            }
        }
    }
}
