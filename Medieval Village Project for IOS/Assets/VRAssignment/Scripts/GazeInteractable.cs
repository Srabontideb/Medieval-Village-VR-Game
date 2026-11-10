using System.Collections;
using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// A village prop (barrel, crate, shield...) you can interact with:
    ///  • Cardboard: look at it (it pulses) and tap the thumb hole, or keep looking for 2 s.
    ///  • Mono: tap it on the screen.
    /// It hops and spins, then lands back where it was.
    /// </summary>
    public class GazeInteractable : MonoBehaviour
    {
        public float hopHeight = 0.8f;
        public float hopDuration = 0.8f;
        public float hoverScale = 1.12f;
        [Tooltip("Off = don't hop; call onInteract instead (used by villagers).")]
        public bool hop = true;
        [Tooltip("For captions, e.g. \"a prop\" / \"a villager\".")]
        public string subject = "a prop";
        [Tooltip("For captions, e.g. \"it hops\".")]
        public string effect = "it hops";
        public System.Action onInteract;

        public string TapCaption => $"Tap {subject} → {effect}";
        public string LookTapCaption => $"Look at {subject} + tap → {effect}";
        public string StareCaption => $"Stare at {subject} for 2 s → {effect}";

        Vector3 baseScale, basePos;
        Quaternion baseRot;
        bool gazed, busy;

        public static int InteractionCount { get; private set; }

        void Awake()
        {
            baseScale = transform.localScale;
        }

        public void SetGazed(bool on) => gazed = on;

        public void Interact()
        {
            if (!hop) { onInteract?.Invoke(); return; }
            if (!busy) StartCoroutine(Hop());
        }

        void Update()
        {
            float pulse = hop ? 0.04f : 0.01f;
            float target = gazed && !busy ? hoverScale + pulse * Mathf.Sin(Time.time * 8f) : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target, Time.deltaTime * 12f);
        }

        IEnumerator Hop()
        {
            busy = true;
            InteractionCount++;
            if (SoundFX.Instance != null) SoundFX.Instance.Boing();
            basePos = transform.position;
            baseRot = transform.rotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / hopDuration)
            {
                float y = 4f * t * (1f - t); // parabola 0 → 1 → 0
                transform.position = basePos + Vector3.up * (y * hopHeight);
                transform.rotation = Quaternion.AngleAxis(360f * Mathf.SmoothStep(0f, 1f, t), Vector3.up) * baseRot;
                yield return null;
            }
            transform.position = basePos;
            transform.rotation = baseRot;
            busy = false;
        }
    }
}
