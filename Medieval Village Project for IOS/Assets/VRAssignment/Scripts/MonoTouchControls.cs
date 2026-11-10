using System.Collections.Generic;
using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// PART I – navigation in MONO mode (phone held in the hand, no Cardboard).
    ///
    ///  One finger drag ............ look around
    ///  Tap ........................ walk to the tapped spot (or poke a prop: it hops)
    ///  Double-tap ................. teleport to the tapped spot
    ///  Press and hold ............. walk forward while holding (drag to steer)
    ///  Two-finger drag ............ move forward/back (up/down) and strafe (left/right)
    ///  Pinch / spread ............. zoom = change the field of view (FOV)
    ///  Two-finger twist ........... turn left/right
    ///  Editor only: mouse = finger, WASD = move, Q/E = turn, scroll = zoom
    /// </summary>
    public class MonoTouchControls : MonoBehaviour
    {
        public PlayerMotor motor;
        public StereoCameraRig rig;

        [Header("Look (one finger)")]
        [Tooltip("Degrees turned when dragging across the full screen height.")]
        public float lookDegreesPerScreen = 100f;
        [Tooltip("Off = drag the world (like Street View). On = the view follows your finger.")]
        public bool viewFollowsFinger = false;

        [Header("Tap / hold")]
        public float tapMaxTime = 0.3f;
        public float tapSlopInches = 0.12f;
        public float doubleTapTime = 0.35f;
        public float holdTime = 0.45f;
        public float maxTapDistance = 150f;

        [Header("Two fingers")]
        [Tooltip("Metres moved when two fingers drag across the full screen height.")]
        public float panMetresPerScreen = 8f;
        [Tooltip("FOV degrees changed by a pinch across the full screen height.")]
        public float pinchFovPerScreen = 70f;
        public float gestureLockInches = 0.12f;

        // one-finger state
        int primaryId = -1;
        Vector2 startPos;
        float startTime;
        bool movedBeyondSlop, holdWalking, multiTouchUsed;
        float lastTapTime = -10f;
        Vector2 lastTapPos;

        // two-finger state
        enum TwoMode { Undecided, Pan, Pinch, Twist }
        TwoMode twoMode;
        float accPan, accPinch, accTwist;

        readonly HashSet<int> uiFingers = new HashSet<int>();
        readonly List<TouchPoint> active = new List<TouchPoint>();

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (rig == null) rig = FindFirstObjectByType<StereoCameraRig>();
        }

        void Update()
        {
            if (rig != null && rig.IsVR) { ResetState(); return; }

            var touches = InputUtil.Touches;
            active.Clear();
            foreach (var t in touches)
            {
                if (t.phase == TouchPhase.Began && InputUtil.IsOverUI(t.position)) uiFingers.Add(t.id);
                bool ui = uiFingers.Contains(t.id);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) uiFingers.Remove(t.id);
                if (!ui) active.Add(t);
            }

            if (active.Count >= 2) TwoFingers(active[0], active[1]);
            else if (active.Count == 1) OneFinger(active[0]);

            if (touches.Count == 0)
            {
                // all fingers lifted
                primaryId = -1; holdWalking = false; multiTouchUsed = false; twoMode = TwoMode.Undecided;
            }

            KeyboardAndMouseExtras();
        }

        void ResetState()
        {
            primaryId = -1; holdWalking = false; multiTouchUsed = false; twoMode = TwoMode.Undecided;
            uiFingers.Clear();
        }

        // ---------------- one finger ----------------

        void OneFinger(TouchPoint t)
        {
            if (t.phase == TouchPhase.Began || primaryId != t.id)
            {
                primaryId = t.id;
                startPos = t.position;
                startTime = Time.time;
                movedBeyondSlop = false;
                holdWalking = false;
                if (t.phase == TouchPhase.Began) return;
            }

            if (!movedBeyondSlop && !holdWalking && (t.position - startPos).magnitude > InputUtil.Inches(tapSlopInches))
                Caption.Show("1-finger drag → look around");
            if ((t.position - startPos).magnitude > InputUtil.Inches(tapSlopInches)) movedBeyondSlop = true;

            // Look around by dragging.
            if (t.phase == TouchPhase.Moved && (movedBeyondSlop || holdWalking))
            {
                float k = lookDegreesPerScreen / Mathf.Min(Screen.width, Screen.height); // same feel in portrait and landscape
                // Drag-the-world: finger right -> view turns left; finger down -> view tilts up.
                float sign = viewFollowsFinger ? 1f : -1f;
                motor.Look(sign * t.delta.x * k, -sign * t.delta.y * k);
            }

            // Press and hold = walk forward.
            if (!holdWalking && !movedBeyondSlop && !multiTouchUsed && Time.time - startTime > holdTime)
            {
                holdWalking = true;
                Caption.Show("Press & hold → walk forward");
            }
            if (holdWalking && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)) motor.WalkForward(1f);

            if (t.phase == TouchPhase.Ended)
            {
                bool isTap = !movedBeyondSlop && !holdWalking && !multiTouchUsed && Time.time - startTime < tapMaxTime;
                if (isTap) Tap(t.position);
                holdWalking = false;
                primaryId = -1;
            }
        }

        void Tap(Vector2 screenPos)
        {
            bool isDouble = Time.time - lastTapTime < doubleTapTime &&
                            (screenPos - lastTapPos).magnitude < InputUtil.Inches(0.4f);
            lastTapTime = isDouble ? -10f : Time.time;
            lastTapPos = screenPos;

            Camera cam = rig != null ? rig.monoCamera : Camera.main;
            Ray ray = cam.ScreenPointToRay(screenPos);
            // Collide: interactive props use walk-through trigger boxes, but taps must still hit them.
            if (!Physics.Raycast(ray, out RaycastHit hit, maxTapDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                return;

            var prop = hit.collider.GetComponentInParent<GazeInteractable>();
            if (prop != null) { Caption.Show(prop.TapCaption); prop.Interact(); return; }

            if (isDouble) { motor.Teleport(GroundBelow(hit.point)); Caption.Show("Double-tap → teleport"); }
            else { motor.WalkTo(hit.point); Caption.Show("Tap → walk to that spot"); }
        }

        static Vector3 GroundBelow(Vector3 p)
        {
            // If you tapped a wall, drop to the ground in front of it.
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit g, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return g.point;
            return p;
        }

        // ---------------- two fingers ----------------

        void TwoFingers(TouchPoint a, TouchPoint b)
        {
            multiTouchUsed = true;
            holdWalking = false;

            Vector2 prevA = a.position - a.delta, prevB = b.position - b.delta;
            Vector2 pan = (a.delta + b.delta) * 0.5f;
            float pinch = (a.position - b.position).magnitude - (prevA - prevB).magnitude;
            float twist = Vector2.SignedAngle(prevB - prevA, b.position - a.position); // + = counter-clockwise
            float radius = (a.position - b.position).magnitude * 0.5f;

            if (a.phase == TouchPhase.Began || b.phase == TouchPhase.Began)
            {
                twoMode = TwoMode.Undecided; accPan = accPinch = accTwist = 0f;
                return;
            }

            // Decide which gesture this is from the first few millimetres of movement, then lock it.
            if (twoMode == TwoMode.Undecided)
            {
                accPan += pan.magnitude;
                accPinch += Mathf.Abs(pinch);
                accTwist += Mathf.Abs(twist) * Mathf.Deg2Rad * radius;
                float lockPx = InputUtil.Inches(gestureLockInches);
                if (Mathf.Max(accPan, accPinch, accTwist) < lockPx) return;
                if (accPan >= accPinch && accPan >= accTwist) { twoMode = TwoMode.Pan; Caption.Show("2-finger drag → move forward/back & strafe"); }
                else if (accPinch >= accTwist) twoMode = TwoMode.Pinch;
                else { twoMode = TwoMode.Twist; Caption.Show("2-finger twist → turn"); }
            }

            float perScreen = 1f / Mathf.Min(Screen.width, Screen.height);
            switch (twoMode)
            {
                case TwoMode.Pan:
                    // Fingers up = forward, fingers right = strafe right.
                    float m = panMetresPerScreen * perScreen * motor.SpeedMultiplier;
                    motor.Displace(motor.FlatForward * (pan.y * m) + motor.FlatRight * (pan.x * m));
                    break;
                case TwoMode.Pinch:
                    // Spread = zoom in (smaller FOV), pinch = zoom out.
                    if (rig != null)
                    {
                        rig.MonoFieldOfView -= pinch * perScreen * pinchFovPerScreen;
                        Caption.Show($"Pinch → zoom (FOV {rig.MonoFieldOfView:0}°)");
                    }
                    break;
                case TwoMode.Twist:
                    motor.Look(-twist, 0f); // clockwise twist turns right
                    break;
            }
        }

        // ---------------- Editor / desktop testing ----------------

        void KeyboardAndMouseExtras()
        {
            if (Application.isMobilePlatform) return;
            float fwd = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            float side = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            if (fwd != 0f) motor.WalkForward(fwd);
            if (side != 0f) motor.Strafe(side);
            if (Input.GetKeyDown(KeyCode.Space)) motor.Jump();
            if (Input.GetKey(KeyCode.Q)) motor.Look(-90f * Time.deltaTime, 0f);
            if (Input.GetKey(KeyCode.E)) motor.Look(90f * Time.deltaTime, 0f);
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && rig != null) rig.MonoFieldOfView -= scroll * 3f;
        }
    }
}
