using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xena.Assets
{
    // Level01 frogs (update 0x8001B12C, docs/xena-ambient-audit.md): static
    // two-pose FROG.TMD (mesh 0 = jump pose hidden at start, mesh 1 = sitting).
    // Mode 0: on frames with frame&15 != 0, Xena within 1000 -> mode 1, v = 6500,
    // swap meshes 0/1. Mode 1 per frame: y up by v/100, v -= 500, forward 40
    // along the root yaw; when below the original ground: despawn (landing
    // splash/sound not ported). 30 frames/s; drawn with render interpolation.
    public sealed class PrototypeFrogs : MonoBehaviour
    {
        [Serializable] private sealed class ScriptFile { public Actor[] actors; }
        [Serializable] private sealed class Actor { public int index; public string behaviour; public int[] position; public int yaw; }
        public sealed class Frog
        {
            public Transform root, body; public GameObject jumpPose, sitPose; public int yaw, velocity; public bool jumping, gone;
            public Vector3 previous, current;
        }
        public readonly List<Frog> Frogs = new List<Frog>();
        private PrototypeMotor motor; private int frame, ticks, ticksSinceStep;
        private float clock => ticksSinceStep * Time.fixedDeltaTime;
        public int Jumped { get; private set; }

        private void Awake()
        {
            motor = GetComponent<PrototypeMotor>();
            var source = Resources.Load<TextAsset>("Level01Scripts"); var prefab = Resources.Load<GameObject>("Props/Frog/Frog");
            if (source == null || prefab == null) return;
            var group = new GameObject("Level01Frogs").transform;
            foreach (var actor in JsonUtility.FromJson<ScriptFile>(source.text).actors)
            {
                if (actor.behaviour != "ambientFrog") continue;
                var root = new GameObject("frog #" + actor.index).transform; root.SetParent(group, false);
                root.SetPositionAndRotation(new Vector3(actor.position[0], -actor.position[1], actor.position[2]) / 400, Quaternion.Euler(0, (actor.yaw & 4095) * 360f / 4096, 0));
                var body = Instantiate(prefab, root, false); body.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var pose = body.GetComponent<XenaAssetPreview>(); if (pose != null) { pose.ShowControls = false; pose.SampleSlot(0, 0); pose.enabled = false; }
                var frog = new Frog { root = root, body = body.transform, yaw = actor.yaw & 4095, jumpPose = Find(body.transform, "Geometry_0"), sitPose = Find(body.transform, "Geometry_1") };
                if (frog.jumpPose != null) frog.jumpPose.SetActive(false);
                frog.previous = frog.current = root.position; Frogs.Add(frog);
            }
        }
        private static GameObject Find(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t.gameObject;
            return null;
        }
        private void FixedUpdate()
        {
            var input = GetComponent<PrototypeInput>(); if (input != null && input.Paused) return;
            int perStep = Mathf.Max(1, Mathf.RoundToInt(1f / 30 / Time.fixedDeltaTime)); ticksSinceStep++;
            if (++ticks % perStep == 0) { ticksSinceStep = 0; frame++; Step(); }
        }
        private void Step()
        {
            foreach (var frog in Frogs)
            {
                if (frog.gone) continue;
                frog.previous = frog.current;
                if (!frog.jumping)
                {
                    if ((frame & 15) == 0) continue;
                    Vector3 d = (motor.transform.position - frog.root.position) * 400;
                    int dx = Mathf.Abs(Mathf.RoundToInt(d.x)), dz = Mathf.Abs(Mathf.RoundToInt(d.z));
                    if (Mathf.Max(dx, dz) + 3 * Mathf.Min(dx, dz) / 8 >= 1000) continue;
                    frog.jumping = true; frog.velocity = 6500; Jumped++;
                    if (frog.jumpPose != null) frog.jumpPose.SetActive(true); if (frog.sitPose != null) frog.sitPose.SetActive(false);
                }
                float angle = frog.yaw / 4096f * 2 * Mathf.PI;
                Vector3 p = frog.root.position + new Vector3(Mathf.Sin(angle) * 40, frog.velocity / 100f, Mathf.Cos(angle) * 40) / 400;
                frog.velocity -= 500;
                float ground = motor.GroundHeight(p + Vector3.up * 0.5f);
                frog.root.position = p; frog.current = p;
                if (frog.velocity < 0 && (float.IsNegativeInfinity(ground) || p.y < ground)) { frog.gone = true; frog.root.gameObject.SetActive(false); }
            }
        }
        private void LateUpdate()
        {
            float alpha = Mathf.Clamp((clock + Mathf.Clamp(Time.time - Time.fixedTime, 0, Time.fixedDeltaTime)) * 30, 0, 1.5f);
            foreach (var frog in Frogs)
            {
                if (frog.gone || !frog.jumping) { frog.body.localPosition = Vector3.zero; continue; }
                frog.body.position = Vector3.LerpUnclamped(frog.previous, frog.current, alpha);
            }
        }
    }
}
