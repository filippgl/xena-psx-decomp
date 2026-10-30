using System;
using UnityEngine;

namespace Xena.Assets
{
    // Test runs are silent (user, 2026-10-30): the tools/ runners start the player with --mute. Nothing else in the
    // game touches the listener's master volume, so sources keep playing and logging exactly as in a normal run.
    public static class PrototypeAudioMute
    {
        public static bool Active { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Active = Array.IndexOf(Environment.GetCommandLineArgs(), "--mute") >= 0;
            if (Active) AudioListener.volume = 0;
        }
    }
}
