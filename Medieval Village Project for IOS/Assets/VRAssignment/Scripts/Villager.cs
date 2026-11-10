using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// A simple low-poly villager (built from primitives by VR Assignment → Add Villagers).
    ///  • Wanders between random spots near its home on the village paths, pausing now and then.
    ///  • Swings arms and legs while walking.
    ///  • Stops and turns to face you when you come close.
    ///  • Tap it (mono) or look at it + tap / stare 2 s (Cardboard): it waves and says hello.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class Villager : MonoBehaviour
    {
        [Header("Body parts (set by the builder)")]
        public Transform leftLeg, rightLeg, leftArm, rightArm, head;

        [Header("Behaviour")]
        public float walkSpeed = 1.1f;
        public float wanderRadius = 12f;
        public float noticeDistance = 1.8f;
        public string[] greetings =
        {
            "Welcome to our village!",
            "Good day, traveller!",
            "Have you tried the fish market?",
            "The church bell rings at noon.",
            "Mind the river, it's cold!",
            "Fresh bread at the bakery today!",
        };

        Vector3 home, dest;
        bool walking;
        float waitTimer, greetTimer, phase;
        Transform player;
        CapsuleCollider capsule;
        static readonly RaycastHit[] hits = new RaycastHit[12];

        void Awake()
        {
            capsule = GetComponent<CapsuleCollider>();
            home = transform.position;
            var gi = GetComponent<GazeInteractable>();
            if (gi != null)
            {
                gi.hop = false;
                gi.hoverScale = 1.03f;
                gi.subject = "a villager";
                gi.effect = "they wave and say hello";
                gi.onInteract = Greet;
            }
        }

        void Start()
        {
            var m = FindFirstObjectByType<PlayerMotor>();
            if (m != null) player = m.transform;
            waitTimer = Random.Range(0.5f, 3f);
        }

        public void Greet()
        {
            greetTimer = 2.5f;
            walking = false;
            Caption.Show($"Villager: \"{greetings[Random.Range(0, greetings.Length)]}\"");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused

            if (greetTimer > 0f)
            {
                greetTimer -= dt;
                FacePlayer(dt);
                Animate(false, true, dt);
                return;
            }

            if (player != null && (player.position - transform.position).sqrMagnitude < noticeDistance * noticeDistance)
            {
                FacePlayer(dt);
                Animate(false, false, dt);
                return;
            }

            if (!walking)
            {
                waitTimer -= dt;
                Animate(false, false, dt);
                if (waitTimer <= 0f) PickDestination();
                return;
            }

            Vector3 to = dest - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.3f) { Stop(Random.Range(2f, 5f)); return; }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), 200f * dt);
            Vector3 fwd = transform.forward;
            if (Blocked(fwd)) { Stop(Random.Range(0.5f, 1.5f)); return; }

            Vector3 next = transform.position + fwd * (walkSpeed * dt);
            if (!GroundAt(next, out float gy) || Mathf.Abs(gy - transform.position.y) > 0.5f) { Stop(1f); return; }
            next.y = Mathf.Lerp(transform.position.y, gy, 0.5f);
            transform.position = next;
            Animate(true, false, dt);
        }

        void Stop(float wait)
        {
            walking = false;
            waitTimer = wait;
        }

        void PickDestination()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                Vector3 p = home + new Vector3(r.x, 0f, r.y);
                if (GroundAt(p, out float gy) && Mathf.Abs(gy - home.y) < 1.5f)
                {
                    dest = new Vector3(p.x, gy, p.z);
                    walking = true;
                    return;
                }
            }
            waitTimer = 1f;
        }

        void FacePlayer(float dt)
        {
            if (player == null) return;
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 240f * dt);
        }

        bool IsMine(Collider c) => c == capsule || c.transform.IsChildOf(transform);

        /// <summary>Something solid in front at body height (house, fence, tree, another villager...).</summary>
        bool Blocked(Vector3 fwd)
        {
            int n = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 0.9f, 0.25f, fwd, hits, 0.6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsMine(hits[i].collider)) return true;
            return false;
        }

        /// <summary>Ground height at a point (not water, not ourselves).</summary>
        bool GroundAt(Vector3 p, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 1.5f, p.z), Vector3.down, hits, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (IsMine(c) || c.GetComponentInParent<Villager>() != null) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    found = !c.name.StartsWith("Plane"); // the river's water planes
                    y = hits[i].point.y;
                }
            }
            return found;
        }

        void Animate(bool moving, bool waving, float dt)
        {
            phase += dt * (moving ? 7.5f : 0f);
            float swing = moving ? Mathf.Sin(phase) * 32f : 0f;
            float k = 1f - Mathf.Exp(-12f * dt);
            Turn(leftLeg, Quaternion.Euler(swing, 0f, 0f), k);
            Turn(rightLeg, Quaternion.Euler(-swing, 0f, 0f), k);
            Turn(leftArm, Quaternion.Euler(-swing * 0.8f, 0f, 0f), k);
            Turn(rightArm, waving ? Quaternion.Euler(0f, 0f, 150f + 20f * Mathf.Sin(Time.time * 10f)) : Quaternion.Euler(swing * 0.8f, 0f, 0f), k);
            if (head != null)
                head.localRotation = Quaternion.Euler(moving ? 0f : 4f * Mathf.Sin(Time.time * 0.7f + home.x), moving ? 0f : 15f * Mathf.Sin(Time.time * 0.4f + home.z), 0f);
        }

        static void Turn(Transform t, Quaternion target, float k)
        {
            if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, target, k);
        }
    }
}
