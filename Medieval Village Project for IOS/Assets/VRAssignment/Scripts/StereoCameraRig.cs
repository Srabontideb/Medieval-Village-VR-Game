using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VRAssignment
{
    /// <summary>
    /// Switches between MONO (one full-screen camera) and CARDBOARD STEREO
    /// (two side-by-side eye cameras). In stereo the head is driven by the
    /// phone's gyroscope.
    ///
    /// Additional features for grading:
    ///   FOV  – field of view, separately adjustable for mono and stereo.
    ///   IOC  – inter-ocular distance (eye separation) between the two eye cameras.
    /// Both can be changed live from the Settings panel, and are saved.
    /// </summary>
    public class StereoCameraRig : MonoBehaviour
    {
        public static StereoCameraRig Instance { get; private set; }

        [Header("References")]
        public PlayerMotor motor;
        public Camera monoCamera;

        [Header("IOC – inter-ocular distance (metres)")]
        [Range(0f, 0.2f)] public float eyeSeparation = 0.064f;

        [Header("FOV (vertical, degrees)")]
        [Range(20f, 110f)] public float monoFieldOfView = 60f;
        [Range(40f, 120f)] public float stereoFieldOfView = 85f;

        [Header("Orientation")]
        [Tooltip("Off = the app is always landscape. On = mono mode may also rotate to portrait.")]
        public bool allowPortraitInMono = false;

        [Header("Head tracking")]
        public bool useGyroscope = true;
        [Tooltip("0 = raw gyro (lowest latency). Higher = smoother but laggier.")]
        [Range(0f, 0.9f)] public float gyroSmoothing = 0.1f;
        [Tooltip("Keep the horizon straight: the view turns left/right and up/down with your head, but never tilts sideways.")]
        public bool keepHorizonLevel = true;

        /// <summary>Sideways head tilt in degrees (+ = tilted left), even when the horizon is kept level.</summary>
        public float HeadRoll { get; private set; }

        public const float MinMonoFov = 20f, MaxMonoFov = 110f;
        public const float MinStereoFov = 40f, MaxStereoFov = 120f;
        public const float MinSeparation = 0f, MaxSeparation = 0.2f;

        public bool IsVR { get; private set; }
        public bool GyroActive { get; private set; }
        public Camera LeftEye => leftEye;
        public Camera RightEye => rightEye;
        /// <summary>Each eye renders into its own square texture; the UI draws it lens-sized with barrel distortion.</summary>
        public RenderTexture LeftTexture { get; private set; }
        public RenderTexture RightTexture { get; private set; }

        /// <summary>Fired when VR mode is switched on/off.</summary>
        public event Action<bool> ModeChanged;
        /// <summary>Fired when FOV / IOC change (from UI, pinch, etc.).</summary>
        public event Action SettingsChanged;

        Camera leftEye, rightEye, blackBackground;
        Transform head;
        float yawOffset;
        Quaternion smoothedGyro = Quaternion.identity;
        bool gyroInitialised;

        const string KeySep = "vr_eyeSeparation", KeyMonoFov = "vr_monoFov", KeyStereoFov = "vr_stereoFov";

        void Awake()
        {
            Instance = this;
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (monoCamera == null) monoCamera = Camera.main;
            head = motor != null ? motor.head : monoCamera.transform.parent;

            eyeSeparation = PlayerPrefs.GetFloat(KeySep, eyeSeparation);
            monoFieldOfView = PlayerPrefs.GetFloat(KeyMonoFov, monoFieldOfView);
            stereoFieldOfView = PlayerPrefs.GetFloat(KeyStereoFov, stereoFieldOfView);

            CreateEyeCameras();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            if (head == null && motor != null) head = motor.head;
            SetVR(false);
        }

        void CreateEyeCameras()
        {
            LeftTexture = NewEyeTexture("Left Eye RT");
            RightTexture = NewEyeTexture("Right Eye RT");
            leftEye = CreateEye("Left Eye", LeftTexture);
            rightEye = CreateEye("Right Eye", RightTexture);

            // Clears the real screen to black in VR (the eye images are drawn on top by the UI).
            var bgGo = new GameObject("VR Black Background");
            bgGo.transform.SetParent(transform, false);
            blackBackground = bgGo.AddComponent<Camera>();
            blackBackground.clearFlags = CameraClearFlags.SolidColor;
            blackBackground.backgroundColor = Color.black;
            blackBackground.cullingMask = 0; // draws nothing
            blackBackground.depth = -100;
            var bgData = blackBackground.GetUniversalAdditionalCameraData();
            bgData.renderPostProcessing = false;
            bgData.renderShadows = false;
            blackBackground.enabled = false;

            ApplyCameraSettings();
        }

        static RenderTexture NewEyeTexture(string name)
        {
            var rt = new RenderTexture(1024, 1024, 24) { name = name, antiAliasing = 2 };
            rt.Create();
            return rt;
        }

        Camera CreateEye(string name, RenderTexture target)
        {
            var go = new GameObject(name);
            go.transform.SetParent(monoCamera.transform, false);
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(monoCamera); // copies clear flags, clip planes, culling mask... and the transform
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.targetTexture = target;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.depth = monoCamera.depth + 1;

            // Match the URP settings of the original camera (post-processing etc.).
            var src = monoCamera.GetUniversalAdditionalCameraData();
            var dst = cam.GetUniversalAdditionalCameraData();
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.renderShadows = src.renderShadows;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = src.volumeTrigger;

            cam.enabled = false;
            return cam;
        }

        // ---------- public API ----------

        public void ToggleVR() => SetVR(!IsVR);

        public void SetVR(bool on)
        {
            IsVR = on;
            ApplyOrientation(on);
            leftEye.enabled = on;
            rightEye.enabled = on;
            blackBackground.enabled = on;
            // Keep the mono camera's GameObject active so its AudioListener keeps working.
            monoCamera.enabled = !on;

            bool gyroAvailable = SystemInfo.supportsGyroscope || UnityRemoteConnected;
            GyroActive = on && useGyroscope && gyroAvailable;
            if (gyroAvailable) Input.gyro.enabled = GyroActive;
            gyroInitialised = false;

            if (motor != null)
            {
                motor.HeadControlledExternally = GyroActive;
                if (!on)
                {
                    // Leaving VR: carry on looking the same way.
                    motor.SetLook(head.eulerAngles.y, 0f);
                }
            }
            ApplyCameraSettings();
            ModeChanged?.Invoke(on);
        }

        /// <summary>Level the view (mono) / re-sync the gyroscope (VR).</summary>
        public void Recenter()
        {
            if (GyroActive) gyroInitialised = false; // re-aligns to the current heading next frame
            else if (motor != null) motor.SetLook(motor.Yaw, 0f);
        }

        /// <summary>Snap-turn the body (degrees, + = right). Used by Cardboard swipes.</summary>
        public void Turn(float degrees)
        {
            if (GyroActive) yawOffset += degrees;
            else if (motor != null) motor.Look(degrees, 0f);
        }

        public float EyeSeparation
        {
            get => eyeSeparation;
            set { eyeSeparation = Mathf.Clamp(value, MinSeparation, MaxSeparation); ApplyCameraSettings(); Save(); }
        }

        public float MonoFieldOfView
        {
            get => monoFieldOfView;
            set { monoFieldOfView = Mathf.Clamp(value, MinMonoFov, MaxMonoFov); ApplyCameraSettings(); Save(); }
        }

        public float StereoFieldOfView
        {
            get => stereoFieldOfView;
            set { stereoFieldOfView = Mathf.Clamp(value, MinStereoFov, MaxStereoFov); ApplyCameraSettings(); Save(); }
        }

        public void ResetSettings()
        {
            eyeSeparation = 0.064f; monoFieldOfView = 60f; stereoFieldOfView = 85f;
            ApplyCameraSettings(); Save();
        }

        /// <summary>
        /// Cardboard must be landscape (the headset holds the phone sideways, one half of the
        /// screen per eye). Mono mode follows the phone: portrait or landscape.
        /// Needs Player Settings > iOS > Default Orientation = Auto Rotation
        /// with Portrait, Landscape Left and Landscape Right allowed.
        /// </summary>
        void ApplyOrientation(bool vr)
        {
            if (vr)
            {
                // Either landscape works in a Cardboard. We pick it ourselves from the phone's
                // motion sensor (Input.deviceOrientation) and set it explicitly, instead of
                // AutoRotation, because Screen.orientation can report the wrong side on iOS
                // after switching to AutoRotation - that made the world appear upside down.
                SetVRLandscape(Input.deviceOrientation == DeviceOrientation.LandscapeRight
                    ? ScreenOrientation.LandscapeRight
                    : ScreenOrientation.LandscapeLeft);
            }
            else
            {
                // Mono: landscape only (either way round), as in the original design.
                Screen.autorotateToPortrait = allowPortraitInMono;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
        }

        ScreenOrientation gyroLandscape = ScreenOrientation.LandscapeLeft;
        float flipTimer;

        /// <summary>Lock the screen to one landscape and use the matching gyro mapping (they always agree).</summary>
        void SetVRLandscape(ScreenOrientation o)
        {
            if (UnityRemoteConnected) o = ScreenOrientation.LandscapeLeft;
            Screen.orientation = o;
            gyroLandscape = o;
            gyroInitialised = false; // keep looking the same way after a flip
            flipTimer = 0f;
        }

        /// <summary>In VR: if the phone is turned the other way round for a moment, flip the screen to match.</summary>
        void FollowDeviceLandscape()
        {
            if (UnityRemoteConnected) return;
            DeviceOrientation d = Input.deviceOrientation; // physical, from the accelerometer
            ScreenOrientation want =
                d == DeviceOrientation.LandscapeLeft ? ScreenOrientation.LandscapeLeft :   // notch on the left
                d == DeviceOrientation.LandscapeRight ? ScreenOrientation.LandscapeRight : // notch on the right
                gyroLandscape;                                                            // flat / unknown: keep
            if (want != gyroLandscape)
            {
                flipTimer += Time.unscaledDeltaTime;
                if (flipTimer > 0.4f) SetVRLandscape(want);
            }
            else flipTimer = 0f;
        }

        int lastScreenW, lastScreenH;

        void ApplyCameraSettings()
        {
            if (leftEye == null) return;
            lastScreenW = Screen.width; lastScreenH = Screen.height;
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            // Unity's FOV is vertical. In portrait, use the slider value as the HORIZONTAL
            // field of view instead, so turning the phone upright doesn't feel zoomed in.
            monoCamera.fieldOfView = aspect >= 1f
                ? monoFieldOfView
                : Mathf.Min(150f, Camera.HorizontalToVerticalFieldOfView(monoFieldOfView, aspect));
            leftEye.fieldOfView = stereoFieldOfView;
            rightEye.fieldOfView = stereoFieldOfView;
            // Parallel (off-axis free) stereo: each eye is shifted half the IOC sideways.
            leftEye.transform.localPosition = new Vector3(-eyeSeparation * 0.5f, 0f, 0f);
            rightEye.transform.localPosition = new Vector3(eyeSeparation * 0.5f, 0f, 0f);
            SettingsChanged?.Invoke();
        }

        void Save()
        {
            PlayerPrefs.SetFloat(KeySep, eyeSeparation);
            PlayerPrefs.SetFloat(KeyMonoFov, monoFieldOfView);
            PlayerPrefs.SetFloat(KeyStereoFov, stereoFieldOfView);
        }

        void OnApplicationPause(bool paused) { if (paused) PlayerPrefs.Save(); }

        // ---------- head tracking ----------

        void LateUpdate()
        {
            // Phone rotated (portrait <-> landscape): refresh the mono FOV for the new shape.
            if (Screen.width != lastScreenW || Screen.height != lastScreenH) ApplyCameraSettings();

            if (IsVR) FollowDeviceLandscape();
            if (!GyroActive || head == null) return;

            Quaternion g = GyroToUnity(Input.gyro.attitude, gyroLandscape);
            if (!gyroInitialised)
            {
                smoothedGyro = g;
                // Align so that the current physical direction = the direction we were already facing.
                yawOffset = (motor != null ? motor.Yaw : 0f) - g.eulerAngles.y;
                gyroInitialised = true;
            }
            smoothedGyro = gyroSmoothing <= 0f ? g : Quaternion.Slerp(smoothedGyro, g, 1f - gyroSmoothing);
            Quaternion full = Quaternion.Euler(0f, yawOffset, 0f) * smoothedGyro;
            HeadRoll = PlayerMotor.NormalizeAngle(full.eulerAngles.z);
            Vector3 f = full * Vector3.forward;
            if (keepHorizonLevel && Mathf.Abs(f.y) < 0.995f)
                head.rotation = Quaternion.LookRotation(f, Vector3.up); // same look direction, no sideways tilt
            else
                head.rotation = full;
            // Keep the motor's heading in sync so leaving VR / re-centering keeps the same direction.
            if (motor != null) motor.SetLook(head.eulerAngles.y, 0f);
        }

        /// <summary>True in the Editor while the Unity Remote 5 app is streaming from a phone.</summary>
        public static bool UnityRemoteConnected
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorApplication.isRemoteConnected;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Converts the gyroscope attitude (right-handed, device space) to a Unity camera
        /// rotation for a landscape screen. LandscapeLeft = notch on the left, LandscapeRight = notch on the right.
        /// </summary>
        public static Quaternion GyroToUnity(Quaternion q, ScreenOrientation landscape)
        {
            Quaternion r = Quaternion.Euler(90f, 0f, 0f) * new Quaternion(q.x, q.y, -q.z, -q.w);
            return r * Quaternion.Euler(0f, 0f, landscape == ScreenOrientation.LandscapeRight ? 90f : -90f);
        }
    }
}
