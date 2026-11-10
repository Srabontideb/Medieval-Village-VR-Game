# Medieval Village VR

A mobile VR walkthrough of a low-poly medieval village, built in **Unity 6 (URP)** for iPhone and Android. You can explore it two ways:

- **Mono mode:** hold the phone and use touch gestures.
- **Cardboard mode:** put the phone in a Google Cardboard viewer for split-screen stereo, with gyroscope head tracking and thumb-hole controls.

![Medieval Village VR: the village scene in Unity](medieval%20village.png)

| | |
|---|---|
| 🎬 **Demo video** | [Watch on Google Drive](https://drive.google.com/file/d/15RtmbNYxUL55p08Xj6Wk6uRQwxNMN_VK/view?usp=sharing) |
| 🤖 **Android APK** | [Download the APK](https://drive.google.com/file/d/1x-ymt7N_YfNcJrVRzHnZudOOLkJ8ay4C/view?usp=sharing) |
| 📦 **Unity project (zip)** | [Download from Google Drive](https://drive.google.com/file/d/18jM8GNReybHTgrJRnKhMbsYpv6runOhL/view?usp=drive_link) |
| 🍎 **iPhone** | Build from this project with Xcode ([steps below](#iphone-xcode)) |

---

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Opening the project](#opening-the-project)
- [Running on a phone](#running-on-a-phone)
- [Controls](#controls)
- [VR Assignment editor menu](#vr-assignment-editor-menu)
- [How the stereo view works](#how-the-stereo-view-works)
- [Project structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [Credits](#credits)

---

## Features

### Part I – Mono navigation (touch gestures)
- One-finger drag to look, tap to walk, double-tap to teleport, press and hold to walk.
- Two-finger drag to move and strafe, pinch to zoom (changes the FOV), and two-finger twist to turn.

### Part II – Google Cardboard stereo
- Side-by-side stereo with a rounded lens view for each eye, about 64 mm apart, on a black background.
- Gyroscope head tracking. The horizon stays level.
- Thumb-hole input: tap, double-tap, hold and swipe, plus head-tilt strafing and gaze-dwell interaction.

### Extras
- **Adjustable FOV:** separate settings for mono and Cardboard.
- **Adjustable IOC:** eye separation from 0 to 200 mm, live in Cardboard mode.
- **Navigation aids:**
  - Mini-map: heading-up; tap it to walk to that spot.
  - Snap-turn and speed levels.
  - Teleport.
  - Jump, plus **auto-hop** over small stones and logs.
  - Walkable bridges.
- **Menu:**
  - Start in Mono or Cardboard.
  - Switch view at any time.
  - Restart and Quit.
  - Sound, Caption and Auto-hop toggles.
- **Villagers** that wander the paths, turn to face you, and wave and say hello when tapped.
- **On-screen captions:** each action is described on screen, so a recording explains itself without a voice-over.
- **Sound:** wind ambience and a jump sound, generated in code.
- **Landscape only.** Settings are saved between launches.

---

## Requirements

| | |
|---|---|
| Unity | **6000.3 LTS** (Unity 6.3) or newer, with the **Universal Render Pipeline** |
| Modules | **iOS Build Support** (needs a Mac with Xcode) and/or **Android Build Support** |
| Input | *Project Settings → Player → Active Input Handling* = **Both** (already set in this project) |
| Device | A phone with a gyroscope; Google Cardboard is optional |

---

## Opening the project

1. Clone or download this repository.
2. In **Unity Hub**, choose **Add → Add project from disk** and select the folder that contains `Assets`, `Packages` and `ProjectSettings`.
3. Open it with Unity 6.3 LTS. The first import rebuilds the `Library` folder, which takes a few minutes.
4. Open the scene `Assets/Low-Poly Medieval Village/Scenes/SampleScene.unity`, or use **VR Assignment → Open Village Scene**.
5. Press **▶ Play** to try it in the Editor. Keyboard and mouse controls are listed below.

The scene is already set up and saved. The **VR Assignment** menu can rebuild it if needed.

---

## Running on a phone

### Android (APK)
1. **File → Build Profiles → Android → Switch Platform**.
2. In *Player Settings → Other Settings*, set:
   - **Scripting Backend:** IL2CPP
   - **Target Architectures:** ARM64
3. Click **Build** to make an `.apk`, or **Build And Run** with the phone connected over USB and USB debugging turned on.

You can also install the ready-made [APK](https://drive.google.com/file/d/1x-ymt7N_YfNcJrVRzHnZudOOLkJ8ay4C/view?usp=sharing). On the phone, allow *Install unknown apps* for your browser or Files app.

### iPhone (Xcode)
1. **File → Build Profiles → iOS → Switch Platform**, then **Build** into a new folder, e.g. `iOSBuild/`.
2. Open `iOSBuild/Unity-iPhone.xcodeproj` in Xcode.
3. Go to **Unity-iPhone target → Signing & Capabilities**:
   - Tick **Automatically manage signing**.
   - Choose your own **Team**.
   - If needed, change the **Bundle Identifier** to something unique. The project uses `com.srabonti.medievalvr`.
4. Connect the iPhone, select it as the run destination, and press **▶ Run**.
5. Then, on the phone:
   - Turn on **Settings → Privacy & Security → Developer Mode**.
   - Go to **Settings → General → VPN & Device Management**, select your Apple ID, and tap **Trust** / **Verify App**.

With a free Apple ID, the app stops working after 7 days. Press ▶ Run in Xcode again to reinstall it.

---

## Controls

### Mono (phone in hand)

| Gesture | Action |
|---|---|
| One-finger drag | Look around |
| Tap | Walk to that spot. Tapping a prop or villager interacts with it. |
| Double-tap | Teleport |
| Press and hold | Walk forward; drag to steer |
| Two-finger drag | Move forward/back and strafe |
| Pinch / spread | Zoom (FOV) |
| Two-finger twist | Turn |
| **Jump** button | Jump |
| **Mini-map** (bottom-left) | Tap to walk there; the zoom button changes the range |
| **Help / Settings / Menu** (top bar) | Gesture help, FOV/IOC/speed sliders, main menu |

### Cardboard (thumb hole + head)

Insert the phone in landscape.

| Input | Action |
|---|---|
| Move your head | Look around |
| Tap | Start or stop walking where you look |
| Look at a prop or villager and tap, or stare for 2 s | Interact |
| Double-tap | Teleport to the gaze point |
| Press and hold | Walk while held |
| Swipe left / right | Snap-turn 30° |
| Swipe up / down | Faster / slower; swiping down at the slowest speed stops |
| Tilt head left / right | Strafe |
| Look straight down | Pause walking |
| **✕ / ⚙ / ≡** icons | Back to mono / Settings / Menu |

### Editor (keyboard and mouse)

| Key | Action |
|---|---|
| Mouse | Acts as a finger: drag, click, double-click, hold |
| W A S D / arrow keys | Move |
| Q / E | Turn |
| Scroll | Zoom (FOV) |
| Space | Jump in mono; tap in Cardboard |
| Right-drag (Cardboard) | Look; the Mac has no gyroscope |
| Esc | Menu |

---

## VR Assignment editor menu

The menu is in Unity's top menu bar. Each item is a one-click scene tool, and none of them work during Play mode.

| Menu item | What it does |
|---|---|
| **Set Up Village Scene** | Creates the Player rig, colliders, interactive props and the UI, and adds the scene to the build |
| **Landscape Only (no portrait)** | Locks the app to Landscape Left/Right |
| **Smoother Walking** | Removes small colliders that catch the player |
| **Make Stones & Logs Solid** | Makes stones and logs solid so you can hop over them |
| **Fix Bridges** | Adds a smooth walkable deck, ramps and side rails to each bridge |
| **Add Villagers** | Places 8 low-poly villagers |
| **Open Village Scene** | Opens the village scene |
| **Check Scene** | Reports what is and isn't set up |

To rebuild the scene from scratch, run the items in this order, then save with **⌘S / Ctrl+S**:

1. Set Up Village Scene
2. Landscape Only
3. Make Stones & Logs Solid
4. Fix Bridges
5. Add Villagers

---

## How the stereo view works

The app doesn't use the Google Cardboard SDK, because the SDK fixes FOV and IOC from the viewer's QR profile. Instead it builds its own rig:

- Two eye cameras are offset by ±IOC/2 and render into RenderTextures.
- Each eye is shown through a **barrel-distorted** mesh (`BarrelImage.cs`), sized like a lens and centred 64 mm apart on a black background.
- The orientation comes from the gyroscope, using `Input.gyro.attitude` converted into Unity's coordinate system for Landscape Left or Right.
- The horizon is kept level, and head roll is read separately to drive tilt-strafing.

Because of this, FOV and IOC can be changed while the app runs.

---

## Project structure

```
Assets/
├── Low-Poly Medieval Village/      # Village art, models and SampleScene (course-provided asset)
└── VRAssignment/
    ├── Editor/
    │   └── VRAssignmentSetup.cs    # "VR Assignment" menu tools
    ├── Materials/                  # Villager materials
    └── Scripts/
        ├── PlayerMotor.cs          # CharacterController movement, jump/auto-hop, teleport, look
        ├── MonoTouchControls.cs    # Part I touch gestures
        ├── StereoCameraRig.cs      # Mono/stereo switch, eye cameras, FOV, IOC, gyroscope, orientation
        ├── CardboardThumbControls.cs # Part II thumb-hole input, gaze, dwell, head-tilt strafe
        ├── BarrelImage.cs          # Lens-shaped barrel distortion for each eye
        ├── VRAppUI.cs              # Title screen, menu, settings, HUD (built in code)
        ├── MiniMap.cs / MiniMapTap.cs # Top-down mini-map, tap to walk
        ├── GazeInteractable.cs     # Props that react to taps and gaze
        ├── Villager.cs             # Wandering, greeting villagers
        ├── Caption.cs              # On-screen action captions
        ├── SoundFX.cs              # Procedural wind and jump sounds
        ├── InputUtil.cs            # Touch/mouse abstraction
        └── HoldButton.cs           # Optional on-screen move pad
Packages/                           # Unity package manifest
ProjectSettings/                    # Player, input, quality and build settings
```

`Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/` and build folders are not included in the repository. Unity regenerates them when the project is opened.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Everything is pink | The project isn't using URP. Open it with Unity 6.3 and the URP package from `Packages/`. |
| `InvalidOperationException … UnityEngine.Input` | Set **Active Input Handling** to **Both**. |
| No villagers, or the bridges block you | Run **VR Assignment → Add Villagers** / **Fix Bridges**, then save the scene. |
| Xcode: "Signing requires a development team" | Choose your own Team under Signing & Capabilities. |
| iPhone: "Unable to Verify App" / "Untrusted Developer" | Make sure the phone is online, then go to Settings → General → VPN & Device Management → Verify/Trust. |
| The Cardboard view looks tilted in the Editor | Unity Remote is sending the phone's gyroscope. Hold the phone level, or disconnect Remote. |
| Stuck somewhere | Use Menu → **Restart**, or Settings → **Respawn**. |
