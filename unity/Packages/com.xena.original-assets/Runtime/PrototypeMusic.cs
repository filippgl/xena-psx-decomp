using System;
using UnityEngine;

namespace Xena.Assets
{
    // Level01 music m_vill (sound set 1, table 0x800EE550; docs/xena-ambient-audit.md,
    // rendered by tools/export_music.mjs from the original SEP/VAB: an approximate
    // render without reverb). Sequence 0 = exploration; while Xena has a combat
    // target (+0xF8, INFERRED: here any engaged bandit) sequences 1 and 2 alternate,
    // fading back to 0 about 100 frames after combat ends. No loop markers exist:
    // each sequence repeats whole (INFERRED from libsnd). Muted in smoke runs.
    public sealed class PrototypeMusic : MonoBehaviour
    {
        public const float FadeSeconds = 100f / 30;
        private AudioClip[] tracks;
        private AudioSource[] sources;
        private int playing = -1, combatTrack = 1;
        private float calm;
        public int Track => playing;
        public bool Muted { get; private set; }

        private void Awake()
        {
            tracks = new[] { Resources.Load<AudioClip>("Audio/m_vill_seq0"), Resources.Load<AudioClip>("Audio/m_vill_seq1"), Resources.Load<AudioClip>("Audio/m_vill_seq2") };
            if (Array.Exists(tracks, clip => clip == null)) { enabled = false; return; }
            foreach (string arg in Environment.GetCommandLineArgs()) if (arg.EndsWith("-smoke")) Muted = true;
            sources = new[] { gameObject.AddComponent<AudioSource>(), gameObject.AddComponent<AudioSource>() };
            foreach (var source in sources) { source.playOnAwake = false; source.spatialBlend = 0; source.volume = 0; }
            Play(0);
        }
        private void Play(int track)
        {
            if (track == playing) return;
            playing = track;
            // Crossfade: the free source takes the new track.
            var next = sources[0].isPlaying && sources[0].volume > 0.01f ? sources[1] : sources[0];
            next.clip = tracks[track]; next.loop = track == 0; next.time = 0; next.Play();
        }
        private bool InCombat()
        {
            var bandits = GetComponent<PrototypeBandits>(); if (bandits == null) return false;
            foreach (var brain in bandits.Brains) if (brain != null && !brain.Removed && !brain.Dying && brain.State != PrototypeBanditBrain.Mode.Idle) return true;
            return false;
        }
        private void Update()
        {
            bool combat = InCombat();
            if (combat) { calm = 0; if (playing == 0) { Play(combatTrack); } }
            else if (playing != 0 && (calm += Time.unscaledDeltaTime) > FadeSeconds) Play(0);
            // Alternate the two combat sequences when one ends.
            if (playing > 0)
            {
                var current = Array.Find(sources, s => s.clip == tracks[playing]);
                if (current != null && !current.isPlaying) { combatTrack = playing == 1 ? 2 : 1; playing = -1; Play(combat ? combatTrack : 0); }
            }
            float target = Muted ? 0 : 0.8f * PrototypePauseMenu.MusicVolume;
            foreach (var source in sources)
            {
                bool active = playing >= 0 && source.clip == tracks[playing] && source.isPlaying;
                source.volume = Mathf.MoveTowards(source.volume, active ? target : 0, Time.unscaledDeltaTime / 1.5f);
                if (!active && source.volume <= 0.001f && source.isPlaying) source.Stop();
            }
        }
    }
}
