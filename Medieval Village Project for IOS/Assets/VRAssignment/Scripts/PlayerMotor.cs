using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// Moves the player through the village with a CharacterController (so you
    /// collide with houses and follow the hills), applies gravity, and handles
    /// "walk to a point". All input scripts (touch, Cardboard) talk to this.
    ///
    /// Hierarchy:  Player (this + CharacterController)
    ///               └─ Head  (eye height; rotated by look / gyro)
    ///                    └─ Main Camera (+ the two eye cameras in VR)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("References")]
        public Transform head;

        [Header("Movement")]
        [Tooltip("Normal walking speed in metres per second.")]
        public float walkSpeed = 3f;
        public float gravity = -15f;
        public float eyeHeight = 1.6f;
        [Tooltip("Highest step (metres) the player walks up without getting stuck.")]
        public float stepHeight = 0.6f;
        [Tooltip("If you fall below this height you are put back at the start.")]
        public float killHeight = -30f;

        [Header("Jumping / hopping")]
        [Tooltip("How high a jump goes (metres) - enough to hop over stones and logs.")]
        public float jumpHeight = 1.0f;
        [Tooltip("Automatically hop when you walk into a low obstacle (stone, log, crate).")]
        public bool autoHop = true;
        [Tooltip("Obstacles lower than this are hopped over automatically.")]
        public float maxAutoHopHeight = 1.1f;

        [Header("Look limits (mono mode)")]
        public float minPitch = -80f;
        public float maxPitch = 80f;

        /// <summary>Horizontal look angle in degrees (mono mode).</summary>
        public float Yaw { get; private set; }
        /// <summary>Vertical look angle in degrees, positive = looking down (Unity convention).</summary>
        public float Pitch { get; private set; }

        /// <summary>True while something else (the gyroscope in VR) rotates the head.</summary>
        public bool HeadControlledExternally { get; set; }

        /// <summary>Multiplies walking speed (Cardboard swipe up/down changes it).</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        public bool IsWalkingToTarget => hasTarget;
        public bool IsGrounded => cc != null && cc.isGrounded;
        /// <summary>Horizontal speed actually moved last frame (m/s) - used for footstep sounds.</summary>
        public float HorizontalSpeed { get; private set; }

        /// <summary>Fired when the player jumps / lands / teleports (used for sounds).</summary>
        public event System.Action Jumped, Landed, Teleported;
        public Vector3 Target => target;

        CharacterController cc;
        Vector3 pendingDisplacement;
        float verticalVelocity;
        bool hasTarget;
        Vector3 target;
        float stuckTimer;
        Vector3 spawnPosition;
        float spawnYaw;
        bool wasGrounded = true;
        float hopCooldown;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cc.stepOffset < stepHeight) cc.stepOffset = stepHeight; // climb low stones, kerbs, steps
            if (cc.slopeLimit < 60f) cc.slopeLimit = 60f;
            gameObject.layer = 2; // "Ignore Raycast": taps and gaze rays pass through the player's own capsule
            if (head == null) head = transform.Find("Head");
            if (head == null)
            {
                Debug.LogError("[PlayerMotor] No 'Head' child found. Run VR Assignment > Set Up Village Scene.");
                enabled = false;
                return;
            }

            // Take the initial look direction from wherever the head is pointing,
            // then keep the root un-rotated (the head does all the rotating).
            Vector3 e = head.rotation.eulerAngles;
            Yaw = e.y;
            Pitch = Mathf.Clamp(NormalizeAngle(e.x), minPitch, maxPitch);
            transform.rotation = Quaternion.identity;
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            ApplyHeadRotation();

            spawnPosition = transform.position;
            spawnYaw = Yaw;
        }

        // ---------- API used by the input scripts ----------

        /// <summary>Rotate the view (degrees). Positive dPitch looks down.</summary>
        public void Look(float dYaw, float dPitch)
        {
            Yaw = Mathf.Repeat(Yaw + dYaw, 360f);
            Pitch = Mathf.Clamp(Pitch + dPitch, minPitch, maxPitch);
        }

        public void SetLook(float yaw, float pitch)
        {
            Yaw = Mathf.Repeat(yaw, 360f);
            Pitch = Mathf.Clamp(NormalizeAngle(pitch), minPitch, maxPitch);
        }

        /// <summary>Move by a world-space displacement this frame (y is ignored).</summary>
        public void Displace(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            pendingDisplacement += worldDelta;
        }

        /// <summary>Walk at the current speed in the direction the head is facing (horizontal).
        /// amount: 1 = forward, -1 = backward. Call every frame while walking.</summary>
        public void WalkForward(float amount)
        {
            Displace(FlatForward * (amount * CurrentSpeed * Time.deltaTime));
        }

        /// <summary>Strafe sideways. amount: 1 = right, -1 = left. Call every frame.</summary>
        public void Strafe(float amount)
        {
            Displace(FlatRight * (amount * CurrentSpeed * Time.deltaTime));
        }

        public void WalkTo(Vector3 point)
        {
            target = point;
            hasTarget = true;
            stuckTimer = 0f;
        }

        public void CancelWalkTo() => hasTarget = false;

        /// <summary>Jump instantly to a point on the ground.</summary>
        public void Teleport(Vector3 groundPoint)
        {
            hasTarget = false;
            cc.enabled = false;
            transform.position = groundPoint + Vector3.up * 0.05f;
            cc.enabled = true;
            verticalVelocity = 0f;
            Teleported?.Invoke();
        }

        /// <summary>True if the last jump was an automatic hop over an obstacle.</summary>
        public bool LastJumpWasAuto { get; private set; }

        /// <summary>Jump (only when standing on something).</summary>
        public void Jump() => Jump(false);

        void Jump(bool auto)
        {
            if (!cc.isGrounded || hopCooldown > 0f) return;
            LastJumpWasAuto = auto;
            verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight);
            hopCooldown = 0.35f;
            Jumped?.Invoke();
        }

        public void ResetToSpawn()
        {
            Teleport(spawnPosition);
            SetLook(spawnYaw, 0f);
        }

        public float CurrentSpeed => walkSpeed * SpeedMultiplier;

        /// <summary>Head forward direction flattened onto the ground plane.</summary>
        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = head.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = head.up; // looking straight down/up
                f.y = 0f;
                return f.normalized;
            }
        }

        public Vector3 FlatRight
        {
            get
            {
                Vector3 f = FlatForward;
                return new Vector3(f.z, 0f, -f.x);
            }
        }

        // ---------- per-frame ----------

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 move = pendingDisplacement;
            pendingDisplacement = Vector3.zero;

            // Manual movement cancels an automatic walk.
            if (move.sqrMagnitude > 1e-6f) hasTarget = false;

            if (hasTarget)
            {
                Vector3 to = target - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.35f)
                {
                    hasTarget = false;
                }
                else
                {
                    float step = Mathf.Min(dist, CurrentSpeed * dt);
                    move = to / dist * step;
                    // Give up if we're blocked by a wall for a while.
                    Vector3 before = transform.position;
                    MoveCharacter(move, dt);
                    float moved = new Vector2(transform.position.x - before.x, transform.position.z - before.z).magnitude;
                    if (moved < step * 0.2f) stuckTimer += dt; else stuckTimer = 0f;
                    if (stuckTimer > 0.6f) hasTarget = false;
                    FinishFrame();
                    return;
                }
            }

            MoveCharacter(move, dt);
            FinishFrame();
        }

        void MoveCharacter(Vector3 horizontal, float dt)
        {
            if (hopCooldown > 0f) hopCooldown -= dt;
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;
            Vector3 motion = horizontal;
            motion.y = verticalVelocity * dt;

            Vector3 before = transform.position;
            cc.Move(motion);
            Vector3 after = transform.position;
            float moved = new Vector2(after.x - before.x, after.z - before.z).magnitude;
            float wanted = new Vector2(horizontal.x, horizontal.z).magnitude;
            HorizontalSpeed = dt > 0f ? moved / dt : 0f;

            // Auto-hop: we tried to walk but something low blocked us -> jump over it.
            if (autoHop && cc.isGrounded && wanted > 1e-4f && moved < wanted * 0.35f &&
                (cc.collisionFlags & CollisionFlags.Sides) != 0 && IsLowObstacleAhead(horizontal / wanted))
                Jump(true);

            // Landing
            if (cc.isGrounded && !wasGrounded) Landed?.Invoke();
            wasGrounded = cc.isGrounded;
        }

        /// <summary>Something blocks us at knee height, but there is free space at head height (= low enough to hop over).</summary>
        bool IsLowObstacleAhead(Vector3 dir)
        {
            Vector3 feet = transform.position;
            bool blockedLow = Physics.Raycast(feet + Vector3.up * 0.35f, dir, cc.radius + 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool clearHigh = !Physics.Raycast(feet + Vector3.up * (maxAutoHopHeight + 0.15f), dir, cc.radius + 1.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return blockedLow && clearHigh;
        }

        void FinishFrame()
        {
            if (transform.position.y < killHeight) ResetToSpawn();
            if (!HeadControlledExternally) ApplyHeadRotation();
        }

        void ApplyHeadRotation()
        {
            head.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
        }

        public static float NormalizeAngle(float a)
        {
            a = Mathf.Repeat(a + 180f, 360f) - 180f;
            return a;
        }
    }
}
