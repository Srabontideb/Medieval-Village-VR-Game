using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRAssignment
{
    /// <summary>
    /// Builds the whole on-screen UI from code (no prefabs to wire up):
    ///  • Mono: [Cardboard VR] [Settings] [Help] buttons, gesture help, walk-to marker.
    ///  • Cardboard: centre divider, one gaze reticle per eye (drawn at the correct
    ///    stereo depth), per-eye messages, small [Exit VR] [Settings] buttons in the corner.
    ///  • Settings panel: FOV (mono + Cardboard), IOC (eye separation), walk speed, head-tilt strafe.
    /// Android back button: closes settings / leaves VR.
    /// </summary>
    public class VRAppUI : MonoBehaviour
    {
        public PlayerMotor motor;
        public StereoCameraRig rig;
        public CardboardThumbControls thumb;

        [Tooltip("Show the gesture help when the app starts.")]
        public bool showHelpOnStart = true;
        [Tooltip("Show the title screen (Start Mono / Start Cardboard / Quit) when the app opens.")]
        public bool showStartScreen = true;

        GameObject startScreen, menuPanel;
        RectTransform captionBar;
        Text captionText;
        Toggle captionToggle;
        Text menuSwitchLabel;
        Toggle soundToggle, hopToggle;
        RectTransform jumpButton;
        SoundFX sound;
        const string KeyHop = "vr_autoHop";

        // palette
        static readonly Color Panel = new Color(0.08f, 0.09f, 0.11f, 0.88f);
        static readonly Color ButtonCol = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        static readonly Color Accent = new Color(1f, 0.74f, 0.25f, 1f);
        static readonly Color TextCol = new Color(0.95f, 0.95f, 0.95f, 1f);

        Font font;
        Sprite circle, ring, rounded;
        RectTransform canvasRect;
        GameObject monoHud, vrHud, settingsPanel, helpPanel;
        RectTransform reticleL, reticleR, marker;
        // Cardboard-style eye views (lens-sized, barrel-distorted, centred on the lenses)
        BarrelImage eyeL, eyeR;
        RectTransform divider;
        Canvas canvas;
        [Tooltip("Distance between the two Cardboard lenses (mm). Standard Cardboard ≈ 64 mm.")]
        public float lensSeparationMm = 64f;
        [Tooltip("Size of each eye view on the screen (mm).")]
        public float eyeSizeMm = 56f;
        Image dwellL, dwellR;
        Text toastL, toastR, toastMono;
        float toastUntil;

        Slider monoFov, vrFov, ioc, speed;
        Text monoFovLabel, vrFovLabel, iocLabel, speedLabel;
        Toggle tiltToggle, padToggle, mapToggle;
        MiniMap miniMap;
        CanvasScaler scaler;
        int lastLayout = -1; // 0 = landscape, 1 = portrait

        // on-screen move pad (mono)
        Sprite triangle;
        GameObject movePad;
        HoldButton padFwd, padBack, padLeft, padRight, padTurnL, padTurnR;

        const string KeySpeed = "vr_walkSpeed", KeyTilt = "vr_tiltStrafe", KeyPad = "vr_showPad", KeyMap = "vr_showMap";

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (rig == null) rig = FindFirstObjectByType<StereoCameraRig>();
            if (thumb == null) thumb = FindFirstObjectByType<CardboardThumbControls>();

            motor.walkSpeed = PlayerPrefs.GetFloat(KeySpeed, motor.walkSpeed);
            if (thumb != null) thumb.headTiltStrafe = PlayerPrefs.GetInt(KeyTilt, thumb.headTiltStrafe ? 1 : 0) == 1;

            motor.autoHop = PlayerPrefs.GetInt(KeyHop, motor.autoHop ? 1 : 0) == 1;
            sound = GetComponent<SoundFX>();
            if (sound == null) sound = gameObject.AddComponent<SoundFX>();

            LoadFont();
            MakeSprites();
            Build();
        }

        void OnEnable()
        {
            rig.ModeChanged += OnModeChanged;
            rig.SettingsChanged += RefreshSettings;
            if (thumb != null) thumb.Message += ShowToast;
            Caption.Shown += ShowToast;
            motor.Jumped += OnJumped;
        }

        void OnDisable()
        {
            rig.ModeChanged -= OnModeChanged;
            rig.SettingsChanged -= RefreshSettings;
            if (thumb != null) thumb.Message -= ShowToast;
            Caption.Shown -= ShowToast;
            motor.Jumped -= OnJumped;
        }

        void OnJumped() => Caption.Show(motor.LastJumpWasAuto ? "Auto-hop over the obstacle" : "Jump button → jump");

        void Start()
        {
            OnModeChanged(rig.IsVR);
            RefreshSettings();
            helpPanel.SetActive(showHelpOnStart);
            settingsPanel.transform.SetAsLastSibling(); // draw on top of everything
            menuPanel.transform.SetAsLastSibling();
            startScreen.transform.SetAsLastSibling();
            startScreen.SetActive(showStartScreen);
        }

        // ======================= per frame =======================

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) // Android back button / Esc key
            {
                if (settingsPanel.activeSelf) settingsPanel.SetActive(false);
                else if (menuPanel.activeSelf) menuPanel.SetActive(false);
                else if (!startScreen.activeSelf) OpenMenu();
            }

            // The game is paused while the title screen or the menu is open.
            Time.timeScale = (startScreen.activeSelf || menuPanel.activeSelf) ? 0f : 1f;
            if (jumpButton != null)
                jumpButton.anchoredPosition = movePad.activeSelf ? new Vector2(-40, 180) : new Vector2(-40, 40);

            UpdateLayoutForOrientation();

            if (rig.IsVR) { UpdateEyeLayout(); UpdateReticles(); }
            else { UpdateMarker(); UpdateMovePad(); }

            // Mini-map sits bottom-left, above the move buttons when those are switched on.
            if (miniMap != null && miniMap.Frame != null)
                miniMap.Frame.anchoredPosition = movePad.activeSelf ? new Vector2(30, 450) : new Vector2(30, 30);

            bool showToast = Time.unscaledTime < toastUntil;
            toastL.enabled = toastR.enabled = showToast && rig.IsVR;
            toastMono.enabled = false; // mono messages use the caption bar
            captionBar.gameObject.SetActive(showToast && !rig.IsVR);
        }

        /// <summary>Resize the UI when the phone turns between portrait and landscape (mono mode).</summary>
        void UpdateLayoutForOrientation()
        {
            int layout = Screen.height > Screen.width ? 1 : 0;
            if (layout == lastLayout) return;
            lastLayout = layout;
            bool portrait = layout == 1;
            // Landscape: UI sized to the screen height. Portrait: sized to the screen width,
            // so the buttons and Settings panel still fit across the narrow screen.
            scaler.referenceResolution = portrait ? new Vector2(1200, 1920) : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = portrait ? 0f : 1f;
            // Help card sits under the top buttons in portrait (there's no room beside them).
            ((RectTransform)helpPanel.transform).anchoredPosition = portrait ? new Vector2(30, -170) : new Vector2(30, -30);
        }

        void UpdateMovePad()
        {
            if (movePad == null || !movePad.activeInHierarchy) return;
            if (padFwd.Held) motor.WalkForward(1f);
            if (padBack.Held) motor.WalkForward(-1f);
            if (padLeft.Held) motor.Strafe(-1f);
            if (padRight.Held) motor.Strafe(1f);
            if (padTurnL.Held) motor.Look(-90f * Time.deltaTime, 0f);
            if (padTurnR.Held) motor.Look(90f * Time.deltaTime, 0f);
        }

        void UpdateReticles()
        {
            if (thumb == null) return;
            PlaceOnEye(reticleL, rig.LeftEye, eyeL, thumb.GazePoint);
            PlaceOnEye(reticleR, rig.RightEye, eyeR, thumb.GazePoint);

            float size = thumb.GazeTarget != null ? 46f : (thumb.HasGazeHit ? 26f : 20f);
            reticleL.sizeDelta = reticleR.sizeDelta = Vector2.one * Mathf.Lerp(reticleL.sizeDelta.x, size, Time.deltaTime * 12f);
            Color c = thumb.GazeTarget != null ? Accent : (thumb.IsWalking ? new Color(0.5f, 1f, 0.6f) : Color.white);
            reticleL.GetComponent<Image>().color = reticleR.GetComponent<Image>().color = c;
            dwellL.fillAmount = dwellR.fillAmount = thumb.DwellProgress;
        }

        /// <summary>Put a reticle where the gaze point appears inside one (distorted) eye view.</summary>
        void PlaceOnEye(RectTransform rt, Camera cam, BarrelImage eye, Vector3 world)
        {
            Vector3 vp = cam.WorldToViewportPoint(world);
            bool visible = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
            rt.gameObject.SetActive(visible);
            if (visible) rt.anchoredPosition = eye.Map(new Vector2(vp.x, vp.y));
        }

        /// <summary>
        /// Cardboard layout: each eye view is a lens-sized square centred in front of its lens
        /// (lenses are ~64 mm apart), with black around it - like Google's Cardboard SDK.
        /// </summary>
        void UpdateEyeLayout()
        {
            if (eyeL.texture == null) eyeL.texture = rig.LeftTexture;
            if (eyeR.texture == null) eyeR.texture = rig.RightTexture;

            float pxPerMm = (Screen.dpi > 1f ? Screen.dpi : Screen.height / (70f / 25.4f)) / 25.4f;
            float sep = lensSeparationMm * pxPerMm;
            float size = Mathf.Min(eyeSizeMm * pxPerMm, Screen.height - 16f, sep - 4f * pxPerMm);
            sep = Mathf.Min(sep, Screen.width - size - 8f);           // keep both views on screen
            float k = canvas.scaleFactor > 0f ? 1f / canvas.scaleFactor : 1f; // pixels -> canvas units

            eyeL.rectTransform.sizeDelta = eyeR.rectTransform.sizeDelta = new Vector2(size, size) * k;
            eyeL.rectTransform.anchoredPosition = new Vector2(-sep * 0.5f * k, 0f);
            eyeR.rectTransform.anchoredPosition = new Vector2(sep * 0.5f * k, 0f);
            divider.sizeDelta = new Vector2(3f, (Screen.height * 0.5f + size * 0.25f) * k); // from the bottom edge up past the centre
            toastL.rectTransform.anchoredPosition = toastR.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.3f * k);
        }

        void UpdateMarker()
        {
            bool show = motor.IsWalkingToTarget;
            if (show)
            {
                Vector3 sp = rig.monoCamera.WorldToScreenPoint(motor.Target);
                show = sp.z > 0f;
                if (show)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);
                    marker.anchoredPosition = local;
                    float pulse = 50f + 10f * Mathf.Sin(Time.time * 6f);
                    marker.sizeDelta = new Vector2(pulse, pulse * 0.6f);
                }
            }
            marker.gameObject.SetActive(show);
        }

        bool? lastMode;

        void OnModeChanged(bool vr)
        {
            monoHud.SetActive(!vr);
            vrHud.SetActive(vr);
            if (lastMode.HasValue && lastMode.Value != vr)
            {
                helpPanel.SetActive(false);
                ShowToast(vr ? "Part II – Cardboard VR: stereo view + head tracking" : "Part I – Mono: touch gestures");
            }
            lastMode = vr;
        }

        public void ShowToast(string msg)
        {
            toastL.text = toastR.text = toastMono.text = msg;
            if (captionText != null) captionText.text = msg;
            toastUntil = Time.unscaledTime + 2.2f;
        }

        // ======================= settings =======================

        void RefreshSettings()
        {
            if (monoFov == null) return;
            monoFov.SetValueWithoutNotify(rig.MonoFieldOfView);
            vrFov.SetValueWithoutNotify(rig.StereoFieldOfView);
            ioc.SetValueWithoutNotify(rig.EyeSeparation * 1000f);
            speed.SetValueWithoutNotify(motor.walkSpeed);
            monoFovLabel.text = $"Field of view (mono / pinch):  {rig.MonoFieldOfView:0}°";
            vrFovLabel.text = $"Field of view (Cardboard):  {rig.StereoFieldOfView:0}°";
            iocLabel.text = $"IOC – eye separation:  {rig.EyeSeparation * 1000f:0} mm   (human ≈ 63 mm)";
            speedLabel.text = $"Walk speed:  {motor.walkSpeed:0.0} m/s";
            if (tiltToggle != null && thumb != null) tiltToggle.SetIsOnWithoutNotify(thumb.headTiltStrafe);
        }

        void ToggleSettings() => settingsPanel.SetActive(!settingsPanel.activeSelf);

        // ======================= building the UI =======================

        void Build()
        {
            // Canvas
            var canvasGo = new GameObject("VR App UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            canvasRect = canvasGo.GetComponent<RectTransform>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            Transform root = canvasGo.transform;

            // ---------- MONO HUD ----------
            monoHud = NewRect("Mono HUD", root, Vector2.zero, Vector2.one).gameObject;
            var bar = NewRect("Top buttons", monoHud.transform, new Vector2(1, 1), new Vector2(1, 1));
            bar.pivot = new Vector2(1, 1);
            bar.anchoredPosition = new Vector2(-30, -30);
            bar.sizeDelta = new Vector2(760, 110);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18; h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            NewButton(bar, "Help", 150, () => helpPanel.SetActive(!helpPanel.activeSelf));
            NewButton(bar, "Settings", 200, ToggleSettings);
            NewButton(bar, "Menu", 220, OpenMenu, Accent, Color.black);

            // Jump button (bottom-right): hop over stones and logs.
            var jb = NewImage("Jump Button", monoHud.transform, circle, new Color(0.1f, 0.11f, 0.14f, 0.7f), true);
            jumpButton = jb.rectTransform;
            jumpButton.anchorMin = jumpButton.anchorMax = new Vector2(1, 0);
            jumpButton.pivot = new Vector2(1, 0);
            jumpButton.sizeDelta = new Vector2(170, 170);
            var jbtn = jb.gameObject.AddComponent<Button>();
            jbtn.onClick.AddListener(() => motor.Jump());
            var jt = NewText(jumpButton, 34, TextAnchor.MiddleCenter);
            Stretch(jt.rectTransform, 6);
            jt.text = "Jump";

            marker = NewImage("Walk target", monoHud.transform, ring, Accent, false).rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.gameObject.SetActive(false);

            // On-screen move pad (bottom-left) and turn buttons (bottom-right).
            movePad = NewRect("Move Pad", monoHud.transform, Vector2.zero, Vector2.one).gameObject;
            padFwd = NewPadButton(movePad.transform, new Vector2(0, 0), new Vector2(160, 290), 0f, null);
            padBack = NewPadButton(movePad.transform, new Vector2(0, 0), new Vector2(160, 40), 180f, null);
            padLeft = NewPadButton(movePad.transform, new Vector2(0, 0), new Vector2(35, 165), 90f, null);
            padRight = NewPadButton(movePad.transform, new Vector2(0, 0), new Vector2(285, 165), -90f, null);
            padTurnL = NewPadButton(movePad.transform, new Vector2(1, 0), new Vector2(-280, 40), 0f, "Turn L");
            padTurnR = NewPadButton(movePad.transform, new Vector2(1, 0), new Vector2(-40, 40), 0f, "Turn R");
            movePad.SetActive(PlayerPrefs.GetInt(KeyPad, 0) == 1); // off by default: Part I is about gestures

            // Mini-map (navigation aid): tap it to walk to that spot.
            miniMap = gameObject.AddComponent<MiniMap>();
            miniMap.Init(motor, rig, monoHud.transform, circle, triangle, rounded, font, ShowToast);
            miniMap.SetVisible(PlayerPrefs.GetInt(KeyMap, 1) == 1);

            helpPanel = NewPanel("Help", monoHud.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), new Vector2(820, 690)).gameObject;
            var helpText = NewText(helpPanel.transform, 30, TextAnchor.UpperLeft);
            Stretch(helpText.rectTransform, 34);
            helpText.text =
                "<b><color=#FFBD40>MONO – touch gestures</color></b>\n" +
                "1 finger drag ........ look around\n" +
                "Tap ........................ walk there / poke a prop\n" +
                "Double-tap ............ teleport there\n" +
                "Press & hold .......... walk forward\n" +
                "2 finger drag ........ move & strafe\n" +
                "Pinch ..................... zoom (FOV)\n" +
                "2 finger twist ....... turn\n" +
                "Mini-map ............... tap it to walk there\n" +
                "Jump button .......... hop over stones\n\n" +
                "<b><color=#FFBD40>CARDBOARD – thumb hole + head</color></b>\n" +
                "Tap = walk/stop · Hold = walk · Double-tap = teleport\n" +
                "Swipe ←/→ turn · ↑/↓ speed · Tilt head = strafe\n" +
                "Look at a prop + tap (or stare 2 s) to interact";
            helpText.supportRichText = true;
            helpPanel.AddComponent<Button>().onClick.AddListener(() => helpPanel.SetActive(false)); // tap card to close

            toastMono = NewText(monoHud.transform, 42, TextAnchor.MiddleCenter);
            Anchor(toastMono.rectTransform, new Vector2(0.5f, 0.18f), new Vector2(1000, 80));
            AddShadow(toastMono);

            // Caption bar (bottom centre): describes each action - for silent screen recordings.
            captionBar = NewImage("Caption Bar", monoHud.transform, rounded, new Color(0.05f, 0.06f, 0.08f, 0.8f), false).rectTransform;
            captionBar.anchorMin = captionBar.anchorMax = new Vector2(0.5f, 0f);
            captionBar.pivot = new Vector2(0.5f, 0f);
            captionBar.anchoredPosition = new Vector2(0, 40);
            captionBar.sizeDelta = new Vector2(1000, 90);
            captionText = NewText(captionBar, 38, TextAnchor.MiddleCenter);
            Stretch(captionText.rectTransform, 12);
            captionText.resizeTextForBestFit = true;
            captionText.resizeTextMinSize = 24;
            captionText.resizeTextMaxSize = 38;
            captionBar.gameObject.SetActive(false);

            // ---------- VR HUD (Cardboard look) ----------
            vrHud = NewRect("VR HUD", root, Vector2.zero, Vector2.one).gameObject;
            var black = NewImage("Black", vrHud.transform, null, Color.black, false).rectTransform;
            Stretch(black, 0);

            eyeL = NewEye("Left Eye View", vrHud.transform);
            eyeR = NewEye("Right Eye View", vrHud.transform);

            divider = NewImage("Divider", vrHud.transform, null, Color.white, false).rectTransform;
            divider.anchorMin = divider.anchorMax = new Vector2(0.5f, 0f);
            divider.pivot = new Vector2(0.5f, 0f);
            divider.anchoredPosition = Vector2.zero;

            reticleL = MakeReticle("Reticle L", eyeL.transform, out dwellL);
            reticleR = MakeReticle("Reticle R", eyeR.transform, out dwellR);

            toastL = NewText(eyeL.transform, 30, TextAnchor.MiddleCenter);
            Anchor(toastL.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600, 60));
            AddShadow(toastL);
            toastR = NewText(eyeR.transform, 30, TextAnchor.MiddleCenter);
            Anchor(toastR.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600, 60));
            AddShadow(toastR);

            // ✕ (exit) top-left and ⚙-style settings top-right, like the Cardboard SDK.
            var exit = NewIconButton("Exit VR", vrHud.transform, new Vector2(0, 1), () => rig.SetVR(false), true);
            var gear = NewIconButton("Settings", vrHud.transform, new Vector2(1, 1), ToggleSettings, false);
            NewBurgerIcon(vrHud.transform);

            // ---------- SETTINGS PANEL ----------
            settingsPanel = NewPanel("Settings", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 1040)).gameObject;
            var v = settingsPanel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(48, 48, 36, 36); v.spacing = 10;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var title = NewText(settingsPanel.transform, 44, TextAnchor.MiddleLeft);
            title.text = "<b>Settings</b>"; title.supportRichText = true; title.color = Accent;
            Prefer(title.gameObject, 64);

            monoFov = NewSlider(settingsPanel.transform, StereoCameraRig.MinMonoFov, StereoCameraRig.MaxMonoFov, out monoFovLabel, x => { rig.MonoFieldOfView = x; Caption.Show($"FOV slider (mono) → {x:0}°"); });
            vrFov = NewSlider(settingsPanel.transform, StereoCameraRig.MinStereoFov, StereoCameraRig.MaxStereoFov, out vrFovLabel, x => { rig.StereoFieldOfView = x; Caption.Show($"FOV slider (Cardboard) → {x:0}°"); });
            ioc = NewSlider(settingsPanel.transform, StereoCameraRig.MinSeparation * 1000f, StereoCameraRig.MaxSeparation * 1000f, out iocLabel, x => { rig.EyeSeparation = x / 1000f; Caption.Show($"IOC slider → eye separation {x:0} mm"); });
            speed = NewSlider(settingsPanel.transform, 1f, 10f, out speedLabel, x =>
            {
                motor.walkSpeed = x;
                Caption.Show($"Walk speed → {x:0.0} m/s");
                PlayerPrefs.SetFloat(KeySpeed, x);
                RefreshSettings();
            });

            mapToggle = NewToggle(settingsPanel.transform, "Mini-map (mono) – tap it to walk there", PlayerPrefs.GetInt(KeyMap, 1) == 1, on =>
            {
                miniMap.SetVisible(on);
                PlayerPrefs.SetInt(KeyMap, on ? 1 : 0);
            });

            padToggle = NewToggle(settingsPanel.transform, "On-screen move buttons (mono)", movePad.activeSelf, on =>
            {
                movePad.SetActive(on);
                PlayerPrefs.SetInt(KeyPad, on ? 1 : 0);
            });

            if (thumb != null)
            {
                tiltToggle = NewToggle(settingsPanel.transform, "Tilt head to strafe (Cardboard)", thumb.headTiltStrafe, on =>
                {
                    thumb.headTiltStrafe = on;
                    PlayerPrefs.SetInt(KeyTilt, on ? 1 : 0);
                });
            }

            var row = NewRect("Buttons", settingsPanel.transform, Vector2.zero, Vector2.one);
            Prefer(row.gameObject, 100);
            var hr = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hr.spacing = 18; hr.childAlignment = TextAnchor.MiddleCenter;
            hr.childControlWidth = hr.childControlHeight = true; hr.childForceExpandWidth = true; hr.childForceExpandHeight = true;
            NewButton(row, "Reset", 0, () => { rig.ResetSettings(); motor.walkSpeed = 3f; PlayerPrefs.SetFloat(KeySpeed, 3f); RefreshSettings(); });
            NewButton(row, "Level view", 0, () => rig.Recenter());
            NewButton(row, "Respawn", 0, () => motor.ResetToSpawn());
            NewButton(row, "Close", 0, ToggleSettings, Accent, Color.black);

            settingsPanel.SetActive(false);

            BuildMenu(root);
            BuildStartScreen(root);
        }

        // ======================= menu & title screen =======================

        void OpenMenu()
        {
            settingsPanel.SetActive(false);
            helpPanel.SetActive(false);
            menuSwitchLabel.text = rig.IsVR ? "Switch to Mono (touch)" : "Switch to Cardboard VR";
            soundToggle.SetIsOnWithoutNotify(sound.SoundOn);
            hopToggle.SetIsOnWithoutNotify(motor.autoHop);
            captionToggle.SetIsOnWithoutNotify(Caption.Enabled);
            menuPanel.SetActive(true);
        }

        void BuildMenu(Transform root)
        {
            menuPanel = NewPanel("Menu", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 1050)).gameObject;
            var v = menuPanel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(48, 48, 36, 36); v.spacing = 12;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var title = NewText(menuPanel.transform, 46, TextAnchor.MiddleCenter);
            title.text = "<b>Menu</b>"; title.color = Accent;
            Prefer(title.gameObject, 64);

            NewButton(menuPanel.transform, "Resume", 0, () => menuPanel.SetActive(false), Accent, Color.black);
            var sw = NewButton(menuPanel.transform, "Switch view", 0, () =>
            {
                menuPanel.SetActive(false);
                rig.SetVR(!rig.IsVR);
            });
            menuSwitchLabel = sw.GetComponentInChildren<Text>();
            NewButton(menuPanel.transform, "Settings", 0, () => { menuPanel.SetActive(false); settingsPanel.SetActive(true); });
            NewButton(menuPanel.transform, "Restart (back to start point)", 0, () =>
            {
                menuPanel.SetActive(false);
                motor.ResetToSpawn();
                ShowToast("Restarted");
            });
            NewButton(menuPanel.transform, "Quit to title screen", 0, () => { menuPanel.SetActive(false); ShowStartScreen(); });
            NewButton(menuPanel.transform, "Quit app", 0, QuitApp);

            soundToggle = NewToggle(menuPanel.transform, "Sound", sound.SoundOn, on => sound.SoundOn = on);
            captionToggle = NewToggle(menuPanel.transform, "Video captions (describe each action)", Caption.Enabled, on => Caption.Enabled = on);
            hopToggle = NewToggle(menuPanel.transform, "Auto-hop over small stones", motor.autoHop, on =>
            {
                motor.autoHop = on;
                PlayerPrefs.SetInt(KeyHop, on ? 1 : 0);
            });

            menuPanel.SetActive(false);
        }

        void BuildStartScreen(Transform root)
        {
            startScreen = NewImage("Title Screen", root, null, new Color(0.03f, 0.04f, 0.06f, 0.82f), true).gameObject;
            Stretch((RectTransform)startScreen.transform, 0);

            var card = NewPanel("Card", startScreen.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 760));
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(60, 60, 50, 50); v.spacing = 18;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var title = NewText(card, 64, TextAnchor.MiddleCenter);
            title.text = "<b>Medieval Village VR</b>"; title.color = Accent;
            Prefer(title.gameObject, 90);
            var sub = NewText(card, 30, TextAnchor.MiddleCenter);
            sub.text = "Explore the village with touch gestures or a Google Cardboard";
            Prefer(sub.gameObject, 80);

            NewButton(card, "Start – Mono (touch)", 0, () => StartGame(false), Accent, Color.black);
            NewButton(card, "Start – Cardboard VR", 0, () => StartGame(true));
            NewButton(card, "Quit", 0, QuitApp);

            var hint = NewText(card, 26, TextAnchor.MiddleCenter);
            hint.text = "Tip: in Cardboard mode, tap through the thumb hole to walk";
            hint.color = new Color(1, 1, 1, 0.6f);
            Prefer(hint.gameObject, 60);
        }

        void ShowStartScreen()
        {
            settingsPanel.SetActive(false);
            menuPanel.SetActive(false);
            if (rig.IsVR) rig.SetVR(false);
            startScreen.SetActive(true);
        }

        void StartGame(bool cardboard)
        {
            startScreen.SetActive(false);
            if (rig.IsVR != cardboard) rig.SetVR(cardboard);
            ShowToast(cardboard ? "Put the phone in the Cardboard – tap to walk" : "Drag to look · tap to walk");
        }

        void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_IOS
            // Apple doesn't allow apps to close themselves: go back to the title screen instead.
            ShowStartScreen();
            ShowToast("On iPhone, swipe up to close the app");
#else
            Application.Quit();
#endif
        }

        /// <summary>Three-bar "menu" icon at the top centre of the Cardboard view.</summary>
        void NewBurgerIcon(Transform parent)
        {
            var bg = NewImage("Menu", parent, circle, new Color(1f, 1f, 1f, 0.001f), true);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(110, 110);
            rt.anchoredPosition = new Vector2(0, -40);
            var b = bg.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => { if (sound != null) sound.Click(); OpenMenu(); });
            for (int i = -1; i <= 1; i++)
            {
                var bar = NewImage("Bar", rt, null, Color.white, false).rectTransform;
                bar.sizeDelta = new Vector2(46, 6);
                bar.anchoredPosition = new Vector2(0, i * 14);
            }
        }

        /// <summary>Square hold-button at a corner anchor; shows a triangle (rotated) or a text label.</summary>
        HoldButton NewPadButton(Transform parent, Vector2 anchor, Vector2 pos, float arrowRotation, string label)
        {
            var bg = NewImage(label ?? "Arrow", parent, rounded, new Color(0.1f, 0.11f, 0.14f, 0.65f), true);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, 0f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = label == null ? new Vector2(115, 115) : new Vector2(220, 115);
            if (label == null)
            {
                var icon = NewImage("Icon", rt, triangle, Color.white, false);
                icon.rectTransform.sizeDelta = new Vector2(56, 56);
                icon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, arrowRotation);
            }
            else
            {
                var t = NewText(rt, 34, TextAnchor.MiddleCenter);
                Stretch(t.rectTransform, 6);
                t.text = label;
            }
            return bg.gameObject.AddComponent<HoldButton>();
        }

        BarrelImage NewEye(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<BarrelImage>();
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(500, 500);
            return img;
        }

        /// <summary>Small round icon button in a screen corner: an ✕ (drawn with two bars) or a settings cog (ring + dot).</summary>
        Button NewIconButton(string name, Transform parent, Vector2 corner, Action onClick, bool cross)
        {
            var bg = NewImage(name, parent, circle, new Color(1f, 1f, 1f, 0.001f), true); // invisible but tappable
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = corner;
            rt.pivot = corner;
            rt.sizeDelta = new Vector2(110, 110);
            rt.anchoredPosition = new Vector2(corner.x < 0.5f ? 40 : -40, -40);
            var b = bg.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => { if (sound != null) sound.Click(); onClick(); });
            if (cross)
            {
                for (int i = 0; i < 2; i++)
                {
                    var bar = NewImage("Bar", rt, null, Color.white, false).rectTransform;
                    bar.sizeDelta = new Vector2(44, 6);
                    bar.localRotation = Quaternion.Euler(0, 0, i == 0 ? 45 : -45);
                }
            }
            else
            {
                var cog = NewImage("Cog", rt, ring, Color.white, false).rectTransform;
                cog.sizeDelta = new Vector2(46, 46);
                for (int i = 0; i < 4; i++)
                {
                    var tooth = NewImage("Tooth", cog, null, Color.white, false).rectTransform;
                    tooth.sizeDelta = new Vector2(60, 9);
                    tooth.localRotation = Quaternion.Euler(0, 0, i * 45);
                }
                var hole = NewImage("Hole", cog, circle, Color.black, false).rectTransform;
                hole.sizeDelta = new Vector2(24, 24);
            }
            return b;
        }

        RectTransform MakeReticle(string name, Transform parent, out Image dwell)
        {
            var img = NewImage(name, parent, ring, Color.white, false);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24, 24);
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.6f);
            dwell = NewImage("Dwell", rt, circle, new Color(1f, 0.74f, 0.25f, 0.7f), false);
            dwell.type = Image.Type.Filled;
            dwell.fillMethod = Image.FillMethod.Radial360;
            dwell.fillOrigin = (int)Image.Origin360.Top;
            dwell.fillClockwise = true;
            dwell.fillAmount = 0f;
            Stretch(dwell.rectTransform, 6);
            return rt;
        }

        // ======================= tiny UI toolkit =======================

        void LoadFont()
        {
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { font = null; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { font = null; }
            }
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 32);
        }

        void MakeSprites()
        {
            circle = MakeSprite(128, (x, y, r) => Mathf.Clamp01(r - Mathf.Sqrt(x * x + y * y) + 0.5f), Vector4.zero);
            ring = MakeSprite(128, (x, y, r) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float outer = Mathf.Clamp01(r - d + 0.5f);
                float inner = Mathf.Clamp01(d - (r - 14f) + 0.5f);
                return Mathf.Min(outer, inner);
            }, Vector4.zero);
            // upward-pointing triangle (rotated for the other arrows)
            triangle = MakeSprite(64, (x, y, r) =>
            {
                float top = r * 0.75f, bottom = -r * 0.6f;
                if (y > top || y < bottom) return 0f;
                float halfWidth = r * 0.8f * (top - y) / (top - bottom);
                return Mathf.Clamp01(halfWidth - Mathf.Abs(x) + 0.5f);
            }, Vector4.zero);
            // 9-sliced rounded rectangle (corner radius 24px)
            rounded = MakeSprite(64, (x, y, r) =>
            {
                const float cr = 24f;
                float ax = Mathf.Max(Mathf.Abs(x) - (r - cr), 0f), ay = Mathf.Max(Mathf.Abs(y) - (r - cr), 0f);
                return Mathf.Clamp01(cr - Mathf.Sqrt(ax * ax + ay * ay) + 0.5f);
            }, new Vector4(28, 28, 28, 28));
        }

        static Sprite MakeSprite(int size, Func<float, float, float, float> alpha, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = alpha(x + 0.5f - r, y + 0.5f - r, r);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color, bool raycast)
        {
            var rt = NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        RectTransform NewPanel(string name, Transform parent, Vector2 anchor, Vector2 anchor2, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var img = NewImage(name, parent, rounded, Panel, true); // raycast = blocks touches behind it
            var rt = img.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor2; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        Text NewText(Transform parent, int size, TextAnchor align)
        {
            var rt = NewRect("Text", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = TextCol;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void AddShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.85f);
            s.effectDistance = new Vector2(2, -2);
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        static void Prefer(GameObject go, float height, float width = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            if (width > 0f) { le.preferredWidth = width; le.minWidth = width; }
        }

        Button NewButton(Transform parent, string label, float width, Action onClick, Color? bg = null, Color? fg = null, int fontSize = 34)
        {
            var img = NewImage(label + " Button", parent, rounded, bg ?? ButtonCol, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => { if (sound != null) sound.Click(); onClick(); });
            var t = NewText(img.transform, fontSize, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 8);
            t.text = label;
            t.color = fg ?? TextCol;
            Prefer(img.gameObject, 96, width);
            if (width <= 0f) img.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return b;
        }

        Slider NewSlider(Transform parent, float min, float max, out Text label, Action<float> onChange)
        {
            var row = NewRect("Slider Row", parent, Vector2.zero, Vector2.one);
            Prefer(row.gameObject, 105);

            label = NewText(row, 32, TextAnchor.MiddleLeft);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(1, 1); lrt.pivot = new Vector2(0.5f, 1);
            lrt.sizeDelta = new Vector2(0, 50); lrt.anchoredPosition = Vector2.zero;

            var srt = NewRect("Slider", row, new Vector2(0, 0), new Vector2(1, 0));
            srt.pivot = new Vector2(0.5f, 0); srt.sizeDelta = new Vector2(-20, 60); srt.anchoredPosition = new Vector2(0, 4);
            var slider = srt.gameObject.AddComponent<Slider>();

            var bg = NewImage("Background", srt, rounded, new Color(1, 1, 1, 0.15f), true);
            bg.rectTransform.anchorMin = new Vector2(0, 0.35f); bg.rectTransform.anchorMax = new Vector2(1, 0.65f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = NewRect("Fill Area", srt, new Vector2(0, 0.35f), new Vector2(1, 0.65f));
            fillArea.offsetMin = new Vector2(10, 0); fillArea.offsetMax = new Vector2(-10, 0);
            var fill = NewImage("Fill", fillArea, rounded, Accent, false);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = new Vector2(-10, 0); fill.rectTransform.offsetMax = new Vector2(10, 0);

            var handleArea = NewRect("Handle Slide Area", srt, Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(24, 0); handleArea.offsetMax = new Vector2(-24, 0);
            var handle = NewImage("Handle", handleArea, circle, Color.white, true);
            handle.rectTransform.anchorMin = new Vector2(0, 0); handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.sizeDelta = new Vector2(56, 0);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min; slider.maxValue = max;
            slider.onValueChanged.AddListener(x => onChange(x));
            return slider;
        }

        Toggle NewToggle(Transform parent, string text, bool value, Action<bool> onChange)
        {
            var row = NewRect("Toggle Row", parent, Vector2.zero, Vector2.one);
            Prefer(row.gameObject, 64);
            var toggle = row.gameObject.AddComponent<Toggle>();

            var box = NewImage("Box", row, rounded, new Color(1, 1, 1, 0.15f), true);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0, 0.5f);
            box.rectTransform.pivot = new Vector2(0, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(56, 56);
            box.rectTransform.anchoredPosition = Vector2.zero;
            var check = NewImage("Check", box.transform, rounded, Accent, false);
            Stretch(check.rectTransform, 10);

            var label = NewText(row, 32, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(76, 0); label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(on => { if (sound != null) sound.Click(); onChange(on); });
            return toggle;
        }
    }
}
