using System;
using UnityEngine;

namespace Xena.Assets
{
    [Serializable] public sealed class XenaMaterialBinding
    {
        public Renderer renderer;
        public Material[] original;
        public Material[] upscaled;
    }

    public sealed class XenaAssetPreview : MonoBehaviour
    {
        [SerializeField] private AnimationClip[] clips;
        [SerializeField] private int[] slots;
        [SerializeField] private int[] frameCounts;
        [SerializeField] private float[] rates;
        [SerializeField] private XenaMaterialBinding[] materials;
        [SerializeField] private bool showControls = true;
        [SerializeField] private bool useUpscale = true;
        [SerializeField] private bool playing = true;
        private int selected;
        private float time;
        private float yaw;

        public int ClipCount => clips == null ? 0 : clips.Length;
        public int CurrentSlot => ClipCount == 0 ? -1 : slots[selected];
        public int CurrentFrame => ClipCount == 0 ? -1 : Mathf.Clamp(Mathf.RoundToInt(time * rates[selected]), 0, frameCounts[selected] - 1);

        public void SetUpscale(bool enabled)
        {
            useUpscale = enabled;
            ApplyMaterials();
        }

        public void Configure(AnimationClip[] animations, int[] identifiers, int[] counts,
            float[] stepsPerSecond, XenaMaterialBinding[] bindings)
        {
            clips = animations; slots = identifiers; frameCounts = counts; rates = stepsPerSecond; materials = bindings;
        }

        private void OnEnable()
        {
            ApplyMaterials();
            Sample();
        }

        private void Update()
        {
            if (ClipCount == 0) return;
            float duration = frameCounts[selected] / rates[selected];
            if (playing) time = (time + Time.deltaTime) % duration;
            Sample();
        }

        public void SampleFrame(int clipIndex, int frame)
        {
            if (clipIndex < 0 || clipIndex >= ClipCount) throw new ArgumentOutOfRangeException(nameof(clipIndex));
            selected = clipIndex;
            time = Mathf.Clamp(frame, 0, frameCounts[selected] - 1) / rates[selected];
            Sample();
        }

        public void SampleSlot(int slot, float seconds)
        {
            int index = Array.IndexOf(slots, slot);
            if (index < 0) index = 0;
            selected = index;
            time = seconds % (frameCounts[index] / rates[index]);
            Sample();
        }

        public void SampleSlotFrame(int slot, int frame)
        {
            int index = Array.IndexOf(slots, slot);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(slot), "Original animation slot unavailable");
            SampleFrame(index, frame);
        }

        private void Sample()
        {
            if (ClipCount > 0 && clips[selected] != null) clips[selected].SampleAnimation(gameObject, time);
        }

        private void ApplyMaterials()
        {
            if (materials == null) return;
            foreach (XenaMaterialBinding binding in materials)
                if (binding.renderer != null) binding.renderer.sharedMaterials = useUpscale ? binding.upscaled : binding.original;
        }

        private void OnGUI()
        {
            if (!showControls || ClipCount == 0) return;
            GUILayout.BeginArea(new Rect(16, 16, 310, 300), GUI.skin.box);
            GUILayout.Label("Xena: original animation preview");
            GUILayout.Label("Slot " + slots[selected].ToString("X2") + " | " + clips[selected].name);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous")) Select(-1);
            if (GUILayout.Button(playing ? "Pause" : "Play")) playing = !playing;
            if (GUILayout.Button("Next")) Select(1);
            GUILayout.EndHorizontal();
            int frame = Mathf.Min(frameCounts[selected] - 1, Mathf.FloorToInt(time * rates[selected]));
            GUILayout.Label("Frame " + (frame + 1) + " / " + frameCounts[selected]);
            int scrub = Mathf.RoundToInt(GUILayout.HorizontalSlider(frame, 0, frameCounts[selected] - 1));
            if (scrub != frame) { playing = false; SampleFrame(selected, scrub); }
            bool nextUpscale = GUILayout.Toggle(useUpscale, "Upscale 4x");
            if (nextUpscale != useUpscale) SetUpscale(nextUpscale);
            GUILayout.Label("Rotate model");
            yaw = GUILayout.HorizontalSlider(yaw, -180, 180);
            transform.localRotation = Quaternion.Euler(0, yaw, 0);
            GUILayout.Label("Preview timing is configurable; gameplay is not implemented.");
            GUILayout.EndArea();
        }

        private void Select(int delta)
        {
            selected = (selected + delta + ClipCount) % ClipCount;
            time = 0;
            Sample();
        }
    }
}
