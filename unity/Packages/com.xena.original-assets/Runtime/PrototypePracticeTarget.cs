using UnityEngine;

namespace Xena.Assets
{
    public sealed class PrototypePracticeTarget : MonoBehaviour
    {
        private PrototypeCombat player;
        private Material material;
        private Renderer[] renderers;
        private float flash, timer;
        public int Health { get; private set; } = 100;
        public int Hits { get; private set; }
        public bool Retaliates { get; set; }
        public static PrototypePracticeTarget Create(Vector3 ground, PrototypeCombat owner)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "PracticeDummy";
            body.transform.position = ground + Vector3.up * 0.85f; body.transform.localScale = new Vector3(0.6f, 0.85f, 0.6f);
            body.GetComponent<Collider>().isTrigger = true;
            var target = body.AddComponent<PrototypePracticeTarget>(); target.player = owner;
            target.material = new Material(Shader.Find("Xena/OriginalUnlit")); target.material.color = new Color(0.65f, 0.38f, 0.13f);
            var arms = GameObject.CreatePrimitive(PrimitiveType.Cube); arms.name = "PracticeArms";
            arms.transform.SetParent(body.transform, false); arms.transform.localPosition = Vector3.up * 0.3f;
            arms.transform.localScale = new Vector3(2.2f, 0.15f, 0.25f); Destroy(arms.GetComponent<Collider>());
            target.renderers = body.GetComponentsInChildren<Renderer>();
            foreach (Renderer item in target.renderers) item.sharedMaterial = target.material;
            return target;
        }
        public void ReceiveHit(int damage) { if (Health <= 0) return; Health = Mathf.Max(0, Health - damage); Hits++; flash = 0.18f; }
        public void ResetTarget() { Health = 100; Hits = 0; flash = timer = 0; Retaliates = false; }
        public void Pulse()
        {
            if (Health > 0 && Vector3.Distance(transform.position, player.transform.position + Vector3.up * 0.85f) < 1.8f)
                player.ReceivePracticeHit(10, transform.position);
        }
        private void FixedUpdate()
        {
            if (player.GetComponent<PrototypeInput>().Paused) return;
            flash = Mathf.Max(0, flash - Time.fixedDeltaTime);
            if (Retaliates && Health > 0) { timer += Time.fixedDeltaTime; if (timer >= 2) { timer = 0; Pulse(); } }
            material.color = Health <= 0 ? Color.gray : flash > 0 ? Color.red : Retaliates && timer > 1.6f ? Color.yellow : new Color(0.65f, 0.38f, 0.13f);
        }
        private void OnGUI()
        {
            if (Camera.main == null) return;
            Vector3 point = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (point.z > 0) GUI.Box(new Rect(point.x - 90, Screen.height - point.y, 180, 25), "Practice dummy: " + Health + "/100");
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
