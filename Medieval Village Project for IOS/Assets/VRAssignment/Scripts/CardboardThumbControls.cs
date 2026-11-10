using System;
using UnityEngine;

namespace VRAssignment
{
    /// <summary>
    /// PART II – navigation in CARDBOARD (stereo) mode, using the thumb hole and head movement.
    ///
    ///  Head movement ............. look around (gyroscope)
    ///  Tap ....................... start / stop walking where you look
    ///  Tap while looking at a prop  make it hop (rapid taps = repeat)
    ///  Double-tap ................ teleport to the spot you are looking at
    ///  Press and hold ............ walk while holding
    ///  Swipe left / right ........ snap-turn 30°
    ///  Swipe up / down ........... faster / slower (slowest + swipe down = stop)
    ///  Tilt head left / right .... strafe sideways
    ///  Look straight down ........ pause walking
    ///  Look at a prop for 2 s .... interact without touching
    /// </summary>
    public class CardboardThumbControls : MonoBehaviour
    {
        public PlayerMotor motor;
        public StereoCameraRig rig;

        [Header("Thumb-hole gestures")]
        public float tapMaxTime = 0.3f;
        public float swipeMinInches = 0.2f;
        public float swipeMaxTime = 0.7f;
        public float holdTime = 0.5f;
        public float doubleTapTime = 0.35f;
        public float snapTurnDegrees = 30f;
        public float[] speedLevels = { 0.5f, 1f, 2f, 3f };
        public int startSpeedLevel = 1;

        [Header("Head gestures")]
        public bool headTiltStrafe = true;
        [Tooltip("Head roll (degrees) before strafing starts.")]
        public float tiltDeadZone = 12f;
        [Tooltip("Head roll (degrees) for full strafe speed.")]
        public float tiltFullAngle = 30f;
        public bool lookDownToPause = true;
        public float lookDownAngle = 65f;

        [Header("Gaze")]
        public float gazeDistance = 80f;
        public float dwellTime = 2f;
        public float maxTeleportDistance = 40f;

        // ---- read by the UI (reticle, messages) ----
        public bool AutoWalk { get; private set; }
        public bool IsWalking { get; private set; }
        public bool HasGazeHit { get; private set; }
        public Vector3 GazePoint { get; private set; }
        public GazeInteractable GazeTarget { get; private set; }
        public float DwellProgress { get; private set; }
        public int SpeedLevel => speedIndex;
        public event Action<string> Message;

        int speedIndex;
        bool wasStrafing, wasPaused;
        int fingerId = -1;
        Vector2 startPos;
        float startTime;
        bool holdWalking, fingerOnUI, dwellFired;
        float lastTapTime = -10f;

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (rig == null) rig = FindFirstObjectByType<StereoCameraRig>();
            speedIndex = Mathf.Clamp(startSpeedLevel, 0, speedLevels.Length - 1);
        }

        void OnEnable()
        {
            if (rig != null) rig.ModeChanged += OnModeChanged;
        }

        void OnDisable()
        {
            if (rig != null) rig.ModeChanged -= OnModeChanged;
        }

        void OnModeChanged(bool vr)
        {
            AutoWalk = false; holdWalking = false; fingerId = -1;
            speedIndex = Mathf.Clamp(startSpeedLevel, 0, speedLevels.Length - 1);
            motor.SpeedMultiplier = vr ? speedLevels[speedIndex] : 1f;
            SetGazeTarget(null);
        }

        void Update()
        {
            if (rig == null || !rig.IsVR) return;

            UpdateGaze();
            HandleThumb();
            HandleEditorLook();

            // ---- movement ----
            Transform head = motor.head;
            float pitch = PlayerMotor.NormalizeAngle(head.eulerAngles.x); // + = looking down
            // + = head tilted left (read from the gyro, since the view itself is kept level)
            float roll = rig.GyroActive ? rig.HeadRoll : PlayerMotor.NormalizeAngle(head.eulerAngles.z);
            bool pausedByLookingDown = lookDownToPause && pitch > lookDownAngle;

            if (pausedByLookingDown && !wasPaused && (AutoWalk || holdWalking)) Caption.Show("Look down → walking paused");
            wasPaused = pausedByLookingDown;
            IsWalking = (AutoWalk || holdWalking) && !pausedByLookingDown;
            if (IsWalking) motor.WalkForward(1f);

            bool strafing = false;
            if (headTiltStrafe && rig.GyroActive)
            {
                float a = Mathf.Abs(roll);
                if (a > tiltDeadZone && a < 60f) // beyond 60° the phone is simply held the wrong way: ignore
                {
                    strafing = true;
                    if (!wasStrafing) Caption.Show(roll < 0f ? "Tilt head right → strafe right" : "Tilt head left → strafe left");
                    float amount = Mathf.Clamp01((a - tiltDeadZone) / Mathf.Max(1f, tiltFullAngle - tiltDeadZone));
                    motor.Strafe(-Mathf.Sign(roll) * amount); // tilt right -> strafe right
                }
            }
            wasStrafing = strafing;
        }

        // ---------------- gaze ----------------

        void UpdateGaze()
        {
            Transform head = motor.head;
            // Collide: interactive props use walk-through trigger boxes, but the gaze must still hit them.
            if (Physics.Raycast(head.position, head.forward, out RaycastHit hit, gazeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                HasGazeHit = true;
                GazePoint = hit.point;
                SetGazeTarget(hit.collider.GetComponentInParent<GazeInteractable>());
            }
            else
            {
                HasGazeHit = false;
                GazePoint = head.position + head.forward * 20f;
                SetGazeTarget(null);
            }

            if (GazeTarget != null && !dwellFired)
            {
                DwellProgress += Time.deltaTime / dwellTime;
                if (DwellProgress >= 1f)
                {
                    Caption.Show(GazeTarget.StareCaption);
                    GazeTarget.Interact();
                    dwellFired = true;
                    DwellProgress = 0f;
                }
            }
        }

        void SetGazeTarget(GazeInteractable t)
        {
            if (t == GazeTarget) return;
            if (GazeTarget != null) GazeTarget.SetGazed(false);
            GazeTarget = t;
            if (GazeTarget != null) GazeTarget.SetGazed(true);
            DwellProgress = 0f;
            dwellFired = false;
        }

        // ---------------- thumb hole ----------------

        void HandleThumb()
        {
            foreach (var t in InputUtil.Touches)
            {
                if (t.phase == TouchPhase.Began && fingerId == -1)
                {
                    fingerId = t.id;
                    startPos = t.position;
                    startTime = Time.time;
                    holdWalking = false;
                    fingerOnUI = InputUtil.IsOverUI(t.position);
                    continue;
                }
                if (t.id != fingerId) continue;
                if (fingerOnUI)
                {
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) fingerId = -1;
                    continue;
                }

                float dist = (t.position - startPos).magnitude;
                float dur = Time.time - startTime;
                bool still = dist < InputUtil.Inches(swipeMinInches * 0.5f);

                if ((t.phase == TouchPhase.Stationary || t.phase == TouchPhase.Moved) && still && dur > holdTime && !holdWalking)
                {
                    holdWalking = true;
                    Say("Hold → walk where you look");
                }

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (holdWalking) holdWalking = false;
                    else if (t.phase == TouchPhase.Ended)
                    {
                        if (dist >= InputUtil.Inches(swipeMinInches) && dur < swipeMaxTime) Swipe(t.position - startPos);
                        else if (still && dur < tapMaxTime) Tap();
                    }
                    fingerId = -1;
                }
            }
        }

        void Tap()
        {
            bool isDouble = Time.time - lastTapTime < doubleTapTime;
            lastTapTime = isDouble ? -10f : Time.time;

            if (GazeTarget != null)
            {
                Say(GazeTarget.LookTapCaption);
                GazeTarget.Interact();
                return;
            }

            AutoWalk = !AutoWalk;

            if (isDouble)
            {
                // The first tap already toggled walking; undo that and teleport instead.
                AutoWalk = !AutoWalk;
                if (HasGazeHit && Vector3.Distance(motor.transform.position, GazePoint) <= maxTeleportDistance)
                {
                    motor.Teleport(GazePoint);
                    Say("Double-tap → teleport to where you look");
                }
                return;
            }
            Say(AutoWalk ? "Tap → walk where you look" : "Tap → stop");
        }

        void Swipe(Vector2 d)
        {
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
            {
                rig.Turn(Mathf.Sign(d.x) * snapTurnDegrees);
                Say(d.x > 0 ? "Swipe right → turn right 30°" : "Swipe left → turn left 30°");
                return;
            }

            if (d.y > 0f)
            {
                if (!AutoWalk) AutoWalk = true;
                else speedIndex = Mathf.Min(speedIndex + 1, speedLevels.Length - 1);
            }
            else
            {
                if (speedIndex == 0) AutoWalk = false;
                else speedIndex--;
            }
            motor.SpeedMultiplier = speedLevels[speedIndex];
            Say(AutoWalk ? $"Swipe {(d.y > 0f ? "up" : "down")} → speed ×{speedLevels[speedIndex]:0.#}" : "Swipe down → stop");
        }

        void Say(string msg) => Message?.Invoke(msg);

        // Editor / Mac testing (no gyroscope):
        //   right-drag or arrow keys = move your "head", WASD = walk/strafe, Space = tap (start/stop walking)
        void HandleEditorLook()
        {
            if (rig.GyroActive || Application.isMobilePlatform || StereoCameraRig.UnityRemoteConnected) return;
            if (Input.GetMouseButton(1))
                motor.Look(Input.GetAxis("Mouse X") * 3f, -Input.GetAxis("Mouse Y") * 3f);

            float lookSpeed = 90f * Time.deltaTime;
            float yaw = (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.Q) ? 1f : 0f);
            float pitch = (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f);
            if (yaw != 0f || pitch != 0f) motor.Look(yaw * lookSpeed, pitch * lookSpeed);

            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float side = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            if (fwd != 0f) motor.WalkForward(fwd);
            if (side != 0f) motor.Strafe(side);

            if (Input.GetKeyDown(KeyCode.Space)) Tap();
        }
    }
}
