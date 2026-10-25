using UnityEngine;

namespace Xena.Assets
{
    // Screen-facing sprite: the original draws pickups with the identity camera
    // rotation (func_80071F9C/func_80010000), i.e. parallel to the screen.
    public sealed class PrototypeScreenSprite : MonoBehaviour
    {
        private void LateUpdate() { if (Camera.main != null) transform.rotation = Camera.main.transform.rotation; }
    }
}
