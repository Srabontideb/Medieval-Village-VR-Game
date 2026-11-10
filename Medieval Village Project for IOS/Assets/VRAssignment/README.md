# Medieval Village VR – Assignment 2 setup guide (Mac + iPhone)

This package turns the **Low-Poly Medieval Village** scene into an iPhone VR app:

- **Part I (mono):** touch-gesture navigation
- **Part II (Cardboard):** split-screen stereo, gyroscope head tracking, and thumb-hole taps and swipes
- **Extras:** adjustable **FOV**, adjustable **IOC** (eye separation), and extra navigation features

Everything in the scene is created by one menu command, so there is nothing to wire up by hand.

---

## 0. Start the Xcode download first (it is the slowest step)

1. On the Mac, open the **App Store** and install **Xcode** (free, several GB).
   If the App Store says your macOS is too old for the latest Xcode, update macOS first.
2. Open Xcode once and let it finish installing its components. If it asks which platforms to download, include **iOS**.
3. Go to **Xcode → Settings → Accounts → +** and sign in with your normal **Apple ID**. A free account is enough. Apps installed this way expire after 7 days, which is fine for the assignment.

## 1. Install Unity and create the project (about 15 min)

1. Install **Unity Hub** from unity.com and sign in.
2. Under **Installs → Install Editor**, choose **Unity 6 LTS**. Tick the module **iOS Build Support**; you don't need Android.
   If Unity is already installed, use **Installs → ⚙ → Add modules → iOS Build Support** instead.
3. Under **Projects → New project**, pick the **Universal 3D (URP)** template and give it a name, e.g. `MedievalVillageVR`.
   The village uses URP materials; in a Built-in pipeline project everything shows up pink.
4. Switch the platform: **File → Build Profiles → iOS → Switch Platform**.

## 2. Import

1. **Assets → Import Package → Custom Package… →** `Medieval_Village.unitypackage`, then **Import** everything.
2. **Assets → Import Package → Custom Package… →** `VRAssignment.unitypackage`, then **Import**.
3. **Edit → Project Settings → Player → Other Settings → Active Input Handling → `Both`**.
   Unity will restart. This step is required because the touch code uses `Input.touches`, as in the Mobile Input manual linked in the assignment.

## 3. One-click scene setup

1. Click **VR Assignment → Open Village Scene**.
2. Click **VR Assignment → Set Up Village Scene**. This:
   - removes the package's mouse/keyboard camera script
   - creates the `Player` rig (CharacterController → Head → Main Camera) on the ground
   - adds colliders to houses, fences and markets so you can't walk through walls, and removes grass colliders
   - makes barrels, crates, shields and similar props interactive
   - sets **landscape** orientation (either way round) and adds the scene to the build
3. Save the scene (**⌘S**).

If you spawn somewhere awkward, move the `Player` object in the Scene view to an open street.

## 4. Test on the Mac (no iPhone needed)

Press **Play** (▶ at the top of the Editor).

| Mouse / trackpad / keyboard | Action |
|---|---|
| Click-drag | Look around |
| Click | Walk to a spot |
| Double-click | Teleport |
| Click and hold | Walk forward |
| WASD / arrows | Move |
| Q / E | Turn |
| Scroll (two-finger swipe on the trackpad) | Zoom (FOV) |
| **Cardboard VR** button | Split screen. Right-drag (two-finger click-drag on the trackpad) to look, since the Mac has no gyroscope. |

## 5. Build to the iPhone

### 5a. Unity Player settings (once)

Open **Edit → Project Settings → Player → iOS tab (phone icon) → Other Settings**:

- **Bundle Identifier:** something unique, e.g. `com.yourname.medievalvr`. Use only letters, numbers and dots.
- **Target minimum iOS Version:** leave the default.
- **Default Orientation:** *Auto Rotation* with only **Landscape Left + Landscape Right** allowed (no portrait), set by the setup menu or **VR Assignment → Landscape Only**.

### 5b. Build the Xcode project

1. **File → Build Profiles → iOS → Build**, and choose a new folder such as `iOSBuild` (outside `Assets/`).
2. When rebuilding later, Unity asks **Replace** or **Append**. Choose **Append**, which keeps your Xcode signing settings.
3. In Finder, open the `iOSBuild` folder and double-click **`Unity-iPhone.xcodeproj`**.

### 5c. Sign and run in Xcode

1. Connect the iPhone to the Mac with a cable. Unlock it and tap **Trust This Computer**.
2. In Xcode, click **Unity-iPhone** in the left sidebar, then select the **Unity-iPhone** target.
3. Open **Signing & Capabilities**, tick **Automatically manage signing**, and set **Team** to *Your Name (Personal Team)*.
   If Xcode complains the bundle ID is taken, change it to something more unique.
4. At the top of the window, choose **your iPhone** as the run destination, then press **▶ Run**.

### 5d. First-launch prompts on the iPhone

- **Developer Mode** (iOS 16 and later): go to **Settings → Privacy & Security → Developer Mode → On**. The phone restarts and asks you to confirm. Then press ▶ Run in Xcode again.
- **"Untrusted Developer"**: go to **Settings → General → VPN & Device Management**, tap your Apple ID, then tap **Trust**. Then open the app.

The gyroscope needs no permission prompt on iOS.

---

## Controls

### Part I – mono (phone in hand)

| Gesture | Action |
|---|---|
| One-finger drag | Look around |
| Tap | Walk to that spot (a marker shows the target). Tapping a prop makes it hop. |
| Double-tap | Teleport there |
| Press and hold | Walk forward; drag while holding to steer |
| Two-finger drag | Up/down moves forward/back; left/right strafes |
| Pinch / spread | Zoom, which changes the **FOV** |
| Two-finger twist | Turn left/right |
| Buttons | **Cardboard VR**, **Settings**, **Help** |
| **Mini-map** (bottom-left) | Top-down map that turns with you; the arrow is you. **Tap the map to walk there.** **Zoom** switches 30 / 60 / 120 m |

### Part II – Cardboard (thumb hole and head)

Put the iPhone in the Cardboard in landscape, with the **top of the phone (Dynamic Island / notch) on the left**.

| Input | Action |
|---|---|
| Move your head | Look around (gyroscope) |
| Tap | Start or stop walking where you look |
| Tap while looking at a prop | It hops; rapid taps repeat |
| Look at a prop for 2 s | Interact without touching (gaze dwell) |
| Double-tap | Teleport to where you look |
| Press and hold | Walk while held |
| Swipe left / right | Snap-turn 30° |
| Swipe up / down | Faster / slower (×0.5 → ×3); swiping down at the slowest speed stops |
| Tilt head left / right | Strafe sideways |
| Look straight down | Pause walking |
| **Exit VR** button (top-left) | Back to mono; tap it with the phone out of the Cardboard |

### Settings panel (works in both modes)

- **FOV (mono)**: 20–110°. Pinch changes it too.
- **FOV (Cardboard)**: 40–120°, per eye.
- **IOC (eye separation)**: 0–200 mm. The human average is about 63 mm. 0 gives flat images, and large values give exaggerated depth.
- **Walk speed**, **tilt-to-strafe** on/off, **Reset**, **Level view**, **Respawn**.
- All values are saved between launches.

## Menu, sounds and hopping

- **Title screen** at launch: *Start – Mono (touch)*, *Start – Cardboard VR*, *Quit*.
- **Menu** (mono: top-right **Menu** button; Cardboard: **≡** icon at the top centre; Android back / Esc key): Resume · Switch to Cardboard VR / Mono · Settings · Restart · Quit to title · Quit app · **Sound** on/off · **Auto-hop over small stones**. The game pauses while the menu is open. (On iPhone, Apple doesn't allow apps to close themselves, so *Quit* returns to the title screen.)
- **Video captions** (`Caption.cs`, on by default, Menu → Video captions): every action shows a short description, e.g. *"Pinch → zoom (FOV 52°)"*, *"Double-tap → teleport"*, *"IOC slider → eye separation 64 mm"* - at the bottom in mono, inside both eyes in Cardboard - so a screen recording explains itself without a voice-over.
- **Villagers** (`Villager.cs`, added with **VR Assignment → Add Villagers**): eight low-poly people built from simple shapes walk around the village paths, stop and turn towards you when you come close, and wave and say hello (as a caption) when you tap them, or look at them and tap / stare 2 s in Cardboard.
- **Sounds** (generated in code, `SoundFX.cs`): a soft **wind** loop and a **jump** sound only.
- **Hopping**: **Jump** button (mono, bottom-right; Space key in the Editor) and **Auto-hop** (on by default): walking into a stone or log lower than ~1.1 m makes you hop over it, in mono and in Cardboard. Use **VR Assignment → Make Stones & Logs Solid** if you want stones to block you (and be hopped over) rather than be walk-through.

## How the stereo works

Instead of the Cardboard SDK, the app renders the scene with **two cameras side by side** (left eye in the left half, right eye in the right half). The cameras are offset sideways by ±IOC/2, and each uses the chosen FOV. Head orientation comes from the iPhone's gyroscope (`Input.gyro.attitude`), converted to Unity's coordinate system for landscape-left.

Building the rig this way makes FOV and IOC adjustable at runtime. With the official Cardboard SDK, both are fixed by the headset's QR profile.

The gaze reticle is drawn in **each** eye at the projected position of whatever you are looking at, so it appears at the correct depth.

## Files

| Script | Role |
|---|---|
| `PlayerMotor.cs` | CharacterController movement, gravity, walk-to, teleport, look |
| `MonoTouchControls.cs` | Part I gestures |
| `StereoCameraRig.cs` | Mono/stereo switch, eye cameras, FOV, IOC, gyroscope head tracking |
| `CardboardThumbControls.cs` | Part II thumb-hole taps and swipes, head tilt, gaze and dwell |
| `GazeInteractable.cs` | Props that hop when tapped or gazed at |
| `VRAppUI.cs` | All on-screen UI, built in code |
| `InputUtil.cs` | Touch helpers; the mouse acts as a finger in the Editor |
| `Editor/VRAssignmentSetup.cs` | The one-click setup menu |

## Recording the video (suggested order, about 2 min)

1. Mono: drag to look, tap-walk, double-tap teleport, hold to walk, two-finger move, twist, pinch zoom.
2. Settings: drag the **FOV** slider, then switch to Cardboard and drag the **IOC** slider so the eye images visibly shift.
3. Cardboard: head tracking, tap to walk and stop, swipe to turn, swipe for speed, tilt to strafe, tap a barrel, double-tap teleport.

**Screen recording on iPhone:**
1. If the button isn't there yet, add it: **Settings → Control Center → + Screen Recording**.
2. Swipe down from the top-right corner and tap ⏺. Recording starts after 3 s. To stop, tap the red indicator at the top.
3. The video is saved to **Photos**. AirDrop it to the Mac to attach it to the email.

Optionally add a short clip, filmed on another phone, of you using the Cardboard.

## Submitting

- **GitHub** (easiest on Mac with GitHub Desktop): create the repo with the **Unity** `.gitignore` template, which excludes `Library/`, `Temp/`, `Logs/`, `Obj/`, `Build/` and `.DS_Store`. Don't commit `iOSBuild/`. Commit `Assets/`, `Packages/` and `ProjectSettings/`.
- **Or Google Drive:** in Finder, select `Assets`, `Packages` and `ProjectSettings`, then **right-click → Compress**, upload the zip and share the link.

Email the video and the link to **VRARMM806.2026@gmail.com**.

## Troubleshooting

| Problem | Fix |
|---|---|
| Everything is pink | The project isn't URP. Recreate it from the *Universal 3D* template. |
| `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class` | Set Active Input Handling to **Both** (step 2.3). |
| Xcode: "Signing requires a development team" | Signing & Capabilities → Team → your Personal Team (step 5c). |
| Xcode: "Failed to register bundle identifier" | Make the Bundle Identifier more unique. |
| Xcode: iPhone shown as "unavailable" or "not supported" | Update Xcode, unlock the phone, and turn on Developer Mode. |
| App won't open: "Untrusted Developer" | Settings → General → VPN & Device Management → Trust. |
| App stopped opening after a week | Free signing expired. Press ▶ Run in Xcode again. |
| Head tracking turns the wrong way | Hold the phone in landscape with the notch on the **left** (Cardboard mode locks to landscape automatically). |
| Hard to fuse the two images | Lower the IOC (try 55–65 mm) or the Cardboard FOV. |
| Laggy on an older iPhone | **Project Settings → Quality**: pick a lower level, or turn off shadows on the URP asset. |
| Stuck inside a house | Open Settings → **Respawn**, or move `Player` in the scene. |
