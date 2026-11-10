using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRAssignment.EditorTools
{
    /// <summary>
    /// Menu:  VR Assignment > Set Up Village Scene
    /// Turns the Low-Poly Medieval Village SampleScene into the VR app in one click.
    /// </summary>
    public static class VRAssignmentSetup
    {
        static readonly Regex InteractableNames = new Regex(
            @"^(barrel|barrel_no_cap|barrel_fish|wooden_box|bucket|bucket_with_fish|watermelon|round_cheese|cake|" +
            @"shield_01|shield_02|cauldron|wicker_bottle|wooden_jar|bag|apple_box|bananas_box|watermelon_box|" +
            @"cart_wheel|anvil|helmet_visor_solo|helmet_armor_solo|pie|candy_jar)(\s*\(\d+\))?$",
            RegexOptions.IgnoreCase);

        [MenuItem("VR Assignment/Set Up Village Scene", priority = 0)]
        public static void SetUp()
        {
            if (BlockedByPlayMode()) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("VR Assignment", "Open the village scene first:\nAssets/Low-Poly Medieval Village/Scenes/SampleScene", "OK");
                return;
            }

            if (Object.FindFirstObjectByType<PlayerMotor>() != null &&
                !EditorUtility.DisplayDialog("VR Assignment", "This scene already has a Player rig. Rebuild it?", "Rebuild", "Cancel"))
                return;

            Undo.SetCurrentGroupName("VR Assignment Setup");
            int group = Undo.GetCurrentGroup();
            var report = new List<string>();

            // Remove an old rig (but keep the camera).
            var oldMotor = Object.FindFirstObjectByType<PlayerMotor>();
            Camera cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(camGo, "camera");
                cam = camGo.GetComponent<Camera>();
            }
            if (oldMotor != null)
            {
                Undo.SetTransformParent(cam.transform, null, "unparent camera");
                foreach (var eye in cam.transform.Cast<Transform>().Where(t => t.name.EndsWith(" Eye")).ToList())
                    Undo.DestroyObjectImmediate(eye.gameObject);
                Undo.DestroyObjectImmediate(oldMotor.gameObject);
            }

            // 1. Remove the package's mouse/keyboard fly camera (it would fight the touch controls).
            foreach (var mb in cam.GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "SimpleCameraController")
                {
                    Undo.DestroyObjectImmediate(mb);
                    report.Add("Removed SimpleCameraController from Main Camera");
                }
            }

            // 2. Grass doesn't need colliders (they make the ground bumpy).
            int grass = 0;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mc.name.StartsWith("grass", System.StringComparison.OrdinalIgnoreCase))
                {
                    Undo.DestroyObjectImmediate(mc);
                    grass++;
                }
            }
            report.Add($"Removed {grass} grass colliders");

            // 3. Find the ground below the camera (before buildings get colliders, so we don't land on a roof).
            Physics.SyncTransforms();
            Vector3 camPos = cam.transform.position;
            Vector3 spawn = GroundBelow(camPos) ?? camPos + Vector3.down * 1.6f;
            float yaw = cam.transform.eulerAngles.y;

            // 4. Give houses, fences, markets... colliders so you can't walk through them.
            int added = AddPropColliders();
            report.Add($"Added {added} mesh colliders to buildings and props");
            int bridges = FixBridges(out _);
            if (bridges > 0) report.Add($"Gave {bridges} bridge(s) a smooth walkable deck with ramps and side rails");

            // If the spawn point is now inside a building, look for free ground nearby.
            Physics.SyncTransforms();
            spawn = FindFreeSpot(spawn);

            // 5. Interactive props.
            int interactables = AddInteractables();
            MakeInteractablesWalkThrough();
            report.Add($"Made {interactables} props interactive (barrels, crates, shields...)");

            // 6. Build the player rig:  Player (CharacterController) / Head / Main Camera
            var player = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(player, "player");
            player.transform.position = spawn + Vector3.up * 0.05f;
            player.layer = 2; // Ignore Raycast
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = StepHeight; cc.slopeLimit = 60f; cc.skinWidth = 0.04f;

            var head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            head.rotation = Quaternion.Euler(0f, yaw, 0f);

            Undo.SetTransformParent(cam.transform, head, "parent camera");
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.05f;

            var motor = player.AddComponent<PlayerMotor>();
            motor.head = head;
            var rig = player.AddComponent<StereoCameraRig>();
            rig.motor = motor; rig.monoCamera = cam;
            var mono = player.AddComponent<MonoTouchControls>();
            mono.motor = motor; mono.rig = rig;
            var thumb = player.AddComponent<CardboardThumbControls>();
            thumb.motor = motor; thumb.rig = rig;
            var ui = player.AddComponent<VRAppUI>();
            ui.motor = motor; ui.rig = rig; ui.thumb = thumb;
            report.Add($"Created Player rig at {spawn}");

            // 7. Project settings: orientation (mono rotates freely, Cardboard forces Landscape Left), scene in build.
            ApplyOrientationSettings();
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == scene.path))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scene.path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                report.Add("Added scene to Build Settings");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;

            string msg = string.Join("\n• ", report.Prepend("Done!"));
#if !ENABLE_LEGACY_INPUT_MANAGER
            msg += "\n\n⚠ IMPORTANT: Edit > Project Settings > Player > Other Settings > " +
                   "Active Input Handling must be set to \"Both\" (touch input uses Input.touches). Unity will restart.";
#endif
            msg += "\n\nSave the scene (Ctrl/Cmd+S), then press Play to test with the mouse, or build to your phone.";
            Debug.Log("[VR Assignment] " + msg);
            EditorUtility.DisplayDialog("VR Assignment", msg, "OK");
        }

        /// <summary>Ground (hills, paths, water) below a point, ignoring tree box colliders.</summary>
        static Vector3? GroundBelow(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits.OrderBy(h => h.distance))
                if (!(h.collider is BoxCollider) && h.normal.y > 0.6f) return h.point;
            return null;
        }

        static bool CapsuleFree(Vector3 feet)
        {
            const float r = 0.3f;
            return !Physics.CheckCapsule(feet + Vector3.up * (r + 0.1f), feet + Vector3.up * (1.8f - r), r,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static Vector3 FindFreeSpot(Vector3 start)
        {
            if (CapsuleFree(start)) return start;
            for (float radius = 1f; radius <= 20f; radius += 1f)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    Vector3 probe = start + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    Vector3? g = GroundBelow(probe + Vector3.up * 2f);
                    if (g.HasValue && Mathf.Abs(g.Value.y - start.y) < 3f && CapsuleFree(g.Value)) return g.Value;
                }
            Debug.LogWarning("[VR Assignment] Couldn't find a free spawn spot; move the Player object to open ground.");
            return start;
        }

        static int AddPropColliders()
        {
            int count = 0;
            var root = GameObject.Find("All props");
            IEnumerable<MeshFilter> filters = root != null
                ? root.GetComponentsInChildren<MeshFilter>(true)
                : Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var go = mf.gameObject;
                string n = go.name.ToLowerInvariant();
                if (n.StartsWith("grass") || n.StartsWith("flower") || n.Contains("fire") || n.StartsWith("mountains")) continue;
                if (go.GetComponent<Collider>() != null) continue;
                if (go.GetComponentInParent<Collider>() != null) continue; // e.g. trees already have a box collider
                var r = go.GetComponent<Renderer>();
                if (r == null || r.bounds.size.magnitude < 0.25f) continue; // tiny props: skip
                if (IsLowObstacle(go.transform, r)) continue;                // stones, logs, crates: walk through
                var mc = Undo.AddComponent<MeshCollider>(go);
                mc.sharedMesh = mf.sharedMesh;
                count++;
            }
            return count;
        }

        static int AddInteractables()
        {
            int count = 0;
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var t in all)
            {
                if (!InteractableNames.IsMatch(t.name)) continue;
                if (t.GetComponentInParent<GazeInteractable>() != null) continue;
                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;

                // Make sure a ray can hit it.
                if (t.GetComponentInChildren<Collider>() == null)
                {
                    Bounds b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    var box = Undo.AddComponent<BoxCollider>(t.gameObject);
                    box.center = t.InverseTransformPoint(b.center);
                    Vector3 s = t.lossyScale;
                    box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
                }

                // Moving objects can't be static.
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);

                Undo.AddComponent<GazeInteractable>(t.gameObject);
                count++;
            }
            return count;
        }

        // ---------------- smoother walking ----------------

        const float StepHeight = 0.6f;          // the player climbs anything up to 60 cm
        const float MinBlockingHeight = 0.9f;   // lower objects don't block (stones, logs, crates, tables)
        static readonly Regex WalkSurfaceNames = new Regex(
            @"bridge|plank|trailway|dirt|sand|stair|step|beam|handrail|landscape|hill|boat|pier|road|path|floor|ground",
            RegexOptions.IgnoreCase);

        /// <summary>A low object you should be able to walk over/through (but never a walkable surface like a bridge).</summary>
        static bool IsLowObstacle(Transform t, Renderer r)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (WalkSurfaceNames.IsMatch(p.name)) return false;
            return r.bounds.size.y < MinBlockingHeight;
        }

        [MenuItem("VR Assignment/Smoother Walking (fewer obstacles)", priority = 11)]
        public static void SmootherWalking()
        {
            if (BlockedByPlayMode()) return;
            Undo.SetCurrentGroupName("Smoother walking");
            int group = Undo.GetCurrentGroup();

            // 1. Low mesh obstacles under "All props" stop blocking (trees' box colliders and the ground are kept).
            int removed = 0;
            var root = GameObject.Find("All props");
            if (root != null)
            {
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.GetComponentInParent<GazeInteractable>() != null) continue; // handled below
                    var r = mc.GetComponent<Renderer>();
                    if (r == null || !IsLowObstacle(mc.transform, r)) continue;
                    Undo.DestroyObjectImmediate(mc);
                    removed++;
                }
            }

            // 2. Interactive props: still tappable / gazeable, but you walk through them.
            int props = MakeInteractablesWalkThrough();

            // 3. Climb higher steps.
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            if (motor != null)
            {
                var cc = motor.GetComponent<CharacterController>();
                Undo.RecordObject(cc, "step height");
                cc.stepOffset = StepHeight;
                cc.slopeLimit = 60f;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Assignment",
                $"Done!\n• {removed} low obstacles (stones, logs, crates...) no longer block you\n" +
                $"• {props} interactive props are walk-through (still tap / gaze to make them hop)\n" +
                $"• Step height raised to {StepHeight * 100:0} cm\n\n" +
                "Houses, big rocks, trees, tall fences and bridges are unchanged.\n\n" +
                "Save the scene (Cmd+S), then rebuild for iOS (Build → Append) and press ▶ in Xcode.", "OK");
        }

        // ---------------- bridges ----------------

        static readonly Regex GroundNames = new Regex(@"^(hill|landscape|plane|mountains|trailway|stones_trailway|dirt|sand)", RegexOptions.IgnoreCase);
        static readonly Regex BridgeRootNames = new Regex(@"^bridge(_with_roof)?(\s*\(\d+\))?$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Bridges are built from dozens of small planks, beams, handrail posts and roof pieces. Colliders on
        /// every piece make a bumpy deck with blocked entrances. Replace them with clean invisible shapes:
        /// one smooth deck, a ramp at each end (if the bank is higher or lower), and low side rails.
        /// </summary>
        [MenuItem("VR Assignment/Fix Bridges (smooth walkable deck)", priority = 13)]
        public static void FixBridgesMenu()
        {
            if (BlockedByPlayMode()) return;
            Undo.SetCurrentGroupName("Fix bridges");
            int group = Undo.GetCurrentGroup();
            int n = FixBridges(out string details);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Assignment",
                n == 0 ? "No bridges found. Open the village scene first."
                       : $"Fixed {n} bridge(s):\n{details}\nThe bridges look the same; only their invisible collision changed.\n\nSave the scene (Cmd+S) and rebuild.", "OK");
        }

        static int FixBridges(out string details)
        {
            details = "";
            int count = 0;
            var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => BridgeRootNames.IsMatch(t.name) && (t.parent == null || !t.parent.name.ToLowerInvariant().Contains("bridge")))
                .ToList();

            foreach (var root in roots)
            {
                // 1. Deck size from the planks (in the bridge's own coordinates).
                var planks = root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.name.ToLowerInvariant().StartsWith("plank")).ToList();
                if (planks.Count == 0) continue;
                bool first = true;
                Bounds deck = new Bounds();
                foreach (var mf in planks)
                {
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 local = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                        if (first) { deck = new Bounds(local, Vector3.zero); first = false; } else deck.Encapsulate(local);
                    }
                }

                // 2. Remove every old collider on the bridge (planks, beams, handrails, roof).
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    if (col.gameObject.name != "VR Walkable") Undo.DestroyObjectImmediate(col);
                var old = root.Find("VR Walkable");
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

                var holder = new GameObject("VR Walkable");
                Undo.RegisterCreatedObjectUndo(holder, "walkable");
                holder.transform.SetParent(root, false);
                Physics.SyncTransforms();

                float top = deck.max.y;
                float halfW = Mathf.Max(0.5f, deck.extents.x);
                // Is the bridge long along its z axis (usual) or along x?
                bool alongZ = deck.size.z >= deck.size.x;
                if (!alongZ) halfW = Mathf.Max(0.5f, deck.extents.z);

                // 3. Ramps at both ends, measured before the deck exists so the probe hits the real ground.
                var ramps = new List<string>();
                foreach (int dir in new[] { -1, 1 })
                {
                    Vector3 endLocal = alongZ ? new Vector3(deck.center.x, top, dir > 0 ? deck.max.z : deck.min.z)
                                              : new Vector3(dir > 0 ? deck.max.x : deck.min.x, top, deck.center.z);
                    Vector3 outward = alongZ ? new Vector3(0, 0, dir) : new Vector3(dir, 0, 0);
                    Vector3 probe = root.TransformPoint(endLocal + outward * 1.5f + Vector3.up * 3f);
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit g, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    float groundY = root.InverseTransformPoint(g.point).y;
                    float dy = groundY - top;
                    if (Mathf.Abs(dy) < 0.15f || Mathf.Abs(dy) > 2.5f) continue; // already flush, or no bank (water)
                    float run = Mathf.Max(1.2f, Mathf.Abs(dy) / Mathf.Tan(30f * Mathf.Deg2Rad));
                    Vector3 a = endLocal - outward * 0.3f;                        // overlap the deck a little
                    Vector3 b = endLocal + outward * run + Vector3.up * dy;
                    Vector3 d = b - a;
                    Quaternion rot = Quaternion.LookRotation(d.normalized, Vector3.up);
                    var ramp = new GameObject(dir > 0 ? "Ramp +" : "Ramp -");
                    ramp.transform.SetParent(holder.transform, false);
                    ramp.transform.localRotation = rot;
                    ramp.transform.localPosition = (a + b) * 0.5f - (rot * Vector3.up) * 0.1f;
                    var rb = ramp.AddComponent<BoxCollider>();
                    rb.size = new Vector3(halfW * 2f, 0.2f, d.magnitude + 0.2f);
                    ramps.Add($"{(dy > 0 ? "up" : "down")} {Mathf.Abs(dy):0.00} m");
                }

                // 4. Smooth deck (a thin box whose top is the plank surface).
                // extends 0.8 m onto the bank at both ends, so there is no lip to catch on at the entrance
                const float overhang = 0.8f;
                var deckBox = holder.AddComponent<BoxCollider>();
                deckBox.center = new Vector3(deck.center.x, top - 0.1f, deck.center.z);
                deckBox.size = alongZ ? new Vector3(halfW * 2f, 0.2f, deck.size.z + 2f * overhang) : new Vector3(deck.size.x + 2f * overhang, 0.2f, halfW * 2f);

                // 6. Clear the approach: anything standing in a 2.4 m wide, 3 m long corridor in front of
                //    each entrance (bush, stone, lantern, fence post...) stops blocking. It stays visible.
                Physics.SyncTransforms();
                var cleared = new List<string>();
                foreach (int dir in new[] { -1, 1 })
                {
                    Vector3 endLocal = alongZ ? new Vector3(deck.center.x, top, dir > 0 ? deck.max.z : deck.min.z)
                                              : new Vector3(dir > 0 ? deck.max.x : deck.min.x, top, deck.center.z);
                    Vector3 outward = alongZ ? new Vector3(0, 0, dir) : new Vector3(dir, 0, 0);
                    Vector3 c = root.TransformPoint(endLocal + outward * 1.5f + Vector3.up * 1.3f);
                    Vector3 half = alongZ ? new Vector3(1.2f, 1.0f, 1.6f) : new Vector3(1.6f, 1.0f, 1.2f);
                    foreach (var col in Physics.OverlapBox(c, half, root.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (col.transform.IsChildOf(root)) continue;                       // our own deck/ramps/rails
                        if (GroundNames.IsMatch(col.name)) continue;                       // never remove the ground
                        if (col.GetComponentInParent<PlayerMotor>() != null) continue;
                        if (col.GetComponentInParent<Villager>() != null) continue;
                        cleared.Add(Regex.Replace(col.name, @"\s*\(\d+\)", ""));
                        Undo.DestroyObjectImmediate(col);
                    }
                }

                // 5. Low invisible side rails so you can't walk off into the river.
                foreach (int side in new[] { -1, 1 })
                {
                    var rail = holder.AddComponent<BoxCollider>();
                    rail.center = alongZ ? new Vector3(deck.center.x + side * (halfW + 0.05f), top + 0.6f, deck.center.z)
                                         : new Vector3(deck.center.x, top + 0.6f, deck.center.z + side * (halfW + 0.05f));
                    rail.size = alongZ ? new Vector3(0.1f, 1.2f, deck.size.z) : new Vector3(deck.size.x, 1.2f, 0.1f);
                }

                count++;
                details += $"• {root.name}: deck {Mathf.Max(deck.size.x, deck.size.z):0.0} m long" + (ramps.Count > 0 ? $", ramps {string.Join(" / ", ramps)}" : "")
                         + (cleared.Count > 0 ? $", cleared at the entrances: {string.Join(", ", cleared.Distinct())}" : "") + "\n";
            }
            return count;
        }


        // ---------------- villagers ----------------

        [MenuItem("VR Assignment/Add Villagers (walking people)", priority = 14)]
        public static void AddVillagers()
        {
            if (BlockedByPlayMode()) return;
            if (GameObject.Find("All props") == null) { EditorUtility.DisplayDialog("VR Assignment", "Open the village scene first.", "OK"); return; }
            var existing = GameObject.Find("Villagers");
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("VR Assignment", "The scene already has villagers. Replace them with new ones?", "Replace", "Cancel")) return;
                Undo.DestroyObjectImmediate(existing);
            }

            Undo.SetCurrentGroupName("Add villagers");
            int group = Undo.GetCurrentGroup();
            Physics.SyncTransforms();

            // Homes on the village paths.
            var paths = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => Regex.IsMatch(t.name, @"^(trailway|stones_trailway)", RegexOptions.IgnoreCase) && t.GetComponent<Renderer>() != null)
                .Select(t => t.GetComponent<Renderer>().bounds.center).ToList();
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            if (paths.Count == 0 && motor != null) paths.Add(motor.transform.position);
            var rng = new System.Random(2026);
            paths = paths.OrderBy(_ => rng.Next()).ToList();

            var root = new GameObject("Villagers");
            Undo.RegisterCreatedObjectUndo(root, "villagers");
            var used = new List<Vector3>();
            int made = 0;
            foreach (var p in paths)
            {
                if (made >= 8) break;
                var ground = WalkableGround(p);
                if (!ground.HasValue || !CapsuleFree(ground.Value)) continue;
                if (used.Any(u => (u - ground.Value).sqrMagnitude < 36f)) continue; // keep them ~6 m apart
                if (motor != null && (motor.transform.position - ground.Value).sqrMagnitude < 9f) continue;
                used.Add(ground.Value);
                BuildVillager(root.transform, ground.Value, made, rng);
                made++;
            }

            // Not enough path spots? Try random places around the player's start.
            Vector3 centre = motor != null ? motor.transform.position : root.transform.position;
            for (int tries = 0; made < 8 && tries < 300; tries++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, dist = 4f + (float)rng.NextDouble() * 26f;
                var ground = WalkableGround(centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist);
                if (!ground.HasValue || Mathf.Abs(ground.Value.y - centre.y) > 3f || !CapsuleFree(ground.Value)) continue;
                if (used.Any(u => (u - ground.Value).sqrMagnitude < 16f)) continue;
                used.Add(ground.Value);
                BuildVillager(root.transform, ground.Value, made, rng);
                made++;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Assignment",
                $"Added {made} villagers on the village paths.\n\nThey wander around, stop and look at you when you come close, " +
                "and wave and say hello when you tap them (or look at them + tap / stare 2 s in Cardboard).\n\n" +
                "Save the scene (Cmd+S), press Play to see them, then rebuild.", "OK");
        }

        /// <summary>Ground below a point that is not water and not a roof.</summary>
        static Vector3? WalkableGround(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits.OrderBy(h => h.distance))
            {
                if (h.collider.name.StartsWith("Plane")) return null; // river
                if (h.normal.y > 0.7f) return h.point;
            }
            return null;
        }

        static readonly Color[] Tunics = { new Color(0.62f, 0.16f, 0.14f), new Color(0.18f, 0.32f, 0.58f), new Color(0.24f, 0.45f, 0.22f), new Color(0.55f, 0.42f, 0.18f), new Color(0.42f, 0.24f, 0.5f), new Color(0.82f, 0.62f, 0.2f) };
        static readonly Color[] Pants = { new Color(0.32f, 0.22f, 0.14f), new Color(0.22f, 0.22f, 0.25f), new Color(0.4f, 0.32f, 0.22f) };
        static readonly Color[] Skins = { new Color(0.96f, 0.8f, 0.66f), new Color(0.82f, 0.62f, 0.45f), new Color(0.55f, 0.38f, 0.26f) };
        static readonly Color[] Hairs = { new Color(0.12f, 0.08f, 0.05f), new Color(0.38f, 0.22f, 0.1f), new Color(0.85f, 0.7f, 0.38f), new Color(0.6f, 0.6f, 0.6f) };

        static void BuildVillager(Transform parent, Vector3 pos, int index, System.Random rng)
        {
            Color Pick(Color[] a) => a[rng.Next(a.Length)];
            Material tunic = VillagerMat(Pick(Tunics)), pants = VillagerMat(Pick(Pants)), skin = VillagerMat(Pick(Skins)),
                     hair = VillagerMat(Pick(Hairs)), dark = VillagerMat(new Color(0.15f, 0.1f, 0.07f)), black = VillagerMat(Color.black);
            bool dress = rng.Next(2) == 0, hat = rng.Next(3) == 0;

            var v = new GameObject($"Villager {index + 1}");
            v.transform.SetParent(parent, false);
            v.transform.position = pos;
            v.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            Transform Pivot(string name, Transform under, Vector3 local)
            {
                var t = new GameObject(name).transform;
                t.SetParent(under, false);
                t.localPosition = local;
                return t;
            }
            void Part(Transform under, PrimitiveType type, Vector3 local, Vector3 scale, Material m)
            {
                var g = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(under, false);
                g.transform.localPosition = local;
                g.transform.localScale = scale;
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var legL = Pivot("Leg L", v.transform, new Vector3(-0.11f, 0.82f, 0f));
            var legR = Pivot("Leg R", v.transform, new Vector3(0.11f, 0.82f, 0f));
            Part(legL, PrimitiveType.Cube, new Vector3(0, -0.4f, 0), new Vector3(0.15f, 0.8f, 0.16f), pants);
            Part(legR, PrimitiveType.Cube, new Vector3(0, -0.4f, 0), new Vector3(0.15f, 0.8f, 0.16f), pants);
            Part(legL, PrimitiveType.Cube, new Vector3(0, -0.78f, 0.04f), new Vector3(0.16f, 0.08f, 0.24f), dark);   // shoes
            Part(legR, PrimitiveType.Cube, new Vector3(0, -0.78f, 0.04f), new Vector3(0.16f, 0.08f, 0.24f), dark);

            Part(v.transform, PrimitiveType.Cube, new Vector3(0, 1.12f, 0), new Vector3(0.44f, 0.6f, 0.26f), tunic);  // torso
            Part(v.transform, PrimitiveType.Cube, new Vector3(0, 0.86f, 0), new Vector3(0.46f, 0.08f, 0.28f), dark);  // belt
            if (dress) Part(v.transform, PrimitiveType.Cylinder, new Vector3(0, 0.56f, 0), new Vector3(0.5f, 0.3f, 0.42f), tunic);

            var armL = Pivot("Arm L", v.transform, new Vector3(-0.29f, 1.38f, 0f));
            var armR = Pivot("Arm R", v.transform, new Vector3(0.29f, 1.38f, 0f));
            foreach (var arm in new[] { armL, armR })
            {
                Part(arm, PrimitiveType.Cube, new Vector3(0, -0.28f, 0), new Vector3(0.12f, 0.58f, 0.13f), tunic);
                Part(arm, PrimitiveType.Sphere, new Vector3(0, -0.6f, 0), new Vector3(0.11f, 0.11f, 0.11f), skin);
            }

            var head = Pivot("Head", v.transform, new Vector3(0f, 1.45f, 0f));
            Part(head, PrimitiveType.Sphere, new Vector3(0, 0.17f, 0), new Vector3(0.27f, 0.3f, 0.27f), skin);
            Part(head, PrimitiveType.Sphere, new Vector3(0, 0.25f, -0.02f), new Vector3(0.29f, 0.2f, 0.29f), hair);
            Part(head, PrimitiveType.Cube, new Vector3(0, 0.15f, 0.135f), new Vector3(0.045f, 0.06f, 0.05f), skin);   // nose
            Part(head, PrimitiveType.Sphere, new Vector3(-0.055f, 0.2f, 0.12f), Vector3.one * 0.035f, black);          // eyes
            Part(head, PrimitiveType.Sphere, new Vector3(0.055f, 0.2f, 0.12f), Vector3.one * 0.035f, black);
            if (hat)
            {
                Part(head, PrimitiveType.Cylinder, new Vector3(0, 0.33f, 0), new Vector3(0.38f, 0.015f, 0.38f), dark);
                Part(head, PrimitiveType.Cylinder, new Vector3(0, 0.39f, 0), new Vector3(0.22f, 0.06f, 0.22f), dark);
            }

            var cap = v.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.85f, 0);
            cap.height = 1.7f;
            cap.radius = 0.25f;
            v.AddComponent<GazeInteractable>();
            var villager = v.AddComponent<Villager>();
            villager.leftLeg = legL; villager.rightLeg = legR;
            villager.leftArm = armL; villager.rightArm = armR;
            villager.head = head;
            villager.walkSpeed = 0.9f + (float)rng.NextDouble() * 0.5f;
        }

        /// <summary>One saved material per colour (saved as assets so they are included in the build).</summary>
        static Material VillagerMat(Color c)
        {
            const string folder = "Assets/VRAssignment/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/VRAssignment", "Materials");
            string path = $"{folder}/Villager_{ColorUtility.ToHtmlStringRGB(c)}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        [MenuItem("VR Assignment/Make Stones & Logs Solid (hop over them)", priority = 12)]
        public static void MakeStonesSolid()
        {
            if (BlockedByPlayMode()) return;
            var root = GameObject.Find("All props");
            if (root == null) { EditorUtility.DisplayDialog("VR Assignment", "Open the village scene first.", "OK"); return; }
            Undo.SetCurrentGroupName("Solid stones");
            int group = Undo.GetCurrentGroup();
            var names = new Regex(@"^(stone|rock|log|big_log|pile_of_logs|rune_stone)", RegexOptions.IgnoreCase);
            int added = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !names.IsMatch(mf.name)) continue;
                if (mf.GetComponent<Collider>() != null || mf.GetComponentInParent<GazeInteractable>() != null) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || r.bounds.size.magnitude < 0.25f) continue;
                Undo.AddComponent<MeshCollider>(mf.gameObject).sharedMesh = mf.sharedMesh;
                added++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Assignment",
                $"Done: {added} stones and logs are solid again.\n\n" +
                "With Auto-hop on (Menu), you jump over the small ones automatically; in mono you can also press Jump.\n\n" +
                "Save the scene (Cmd+S) and rebuild.", "OK");
        }

        /// <summary>Replace each interactive prop's colliders with one trigger box: rays still hit it, the player passes through.</summary>
        static int MakeInteractablesWalkThrough()
        {
            int count = 0;
            foreach (var gi in Object.FindObjectsByType<GazeInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (gi.GetComponent<Villager>() != null) continue; // villagers keep their solid capsule
                var renderers = gi.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var existing = gi.GetComponentsInChildren<Collider>(true);
                if (existing.Length == 1 && existing[0] is BoxCollider bc && bc.isTrigger && bc.gameObject == gi.gameObject) continue; // already done

                foreach (var col in existing) Undo.DestroyObjectImmediate(col);
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var box = Undo.AddComponent<BoxCollider>(gi.gameObject);
                Transform t = gi.transform;
                Vector3 s = t.lossyScale;
                box.center = t.InverseTransformPoint(b.center);
                box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
                box.isTrigger = true;
                count++;
            }
            return count;
        }

        /// <summary>Landscape only: Auto Rotation between Landscape Left and Landscape Right, no portrait.</summary>
        static void ApplyOrientationSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        [MenuItem("VR Assignment/Landscape Only (no portrait)", priority = 10)]
        public static void LandscapeOnly()
        {
            if (BlockedByPlayMode()) return;
            ApplyOrientationSettings();
            foreach (var r in Object.FindObjectsByType<StereoCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(r, "landscape only");
                r.allowPortraitInMono = false;
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("VR Assignment",
                "Done: the app is landscape only (notch on the left or right), in both mono and Cardboard mode.\n\n" +
                "Save the scene (Cmd+S), rebuild for iOS (Build → Append), untick Portrait in Xcode " +
                "(General → Deployment Info) and press ▶.", "OK");
        }


        /// <summary>Changes made in Play mode are thrown away when Play stops - so refuse to run then.</summary>
        static bool BlockedByPlayMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return false;
            EditorUtility.DisplayDialog("VR Assignment",
                "Stop Play mode first (press the ▶ button at the top so it's no longer blue).\n\n" +
                "Anything changed while playing is thrown away when Play stops.", "OK");
            return true;
        }

        [MenuItem("VR Assignment/Check Scene (what's set up?)", priority = 30)]
        public static void CheckScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var lines = new List<string>();
            void Line(bool ok, string text) => lines.Add((ok ? "✔ " : "✘ ") + text);

            Line(!EditorApplication.isPlaying, EditorApplication.isPlaying ? "Play mode is ON - stop it before changing anything" : "Not in Play mode");
            Line(scene.path.Contains("Medieval Village"), $"Open scene: {(string.IsNullOrEmpty(scene.path) ? "(none)" : scene.path)}");
            Line(!scene.isDirty, scene.isDirty ? "Scene has UNSAVED changes - press Cmd+S before building" : "Scene is saved");
            Line(Object.FindFirstObjectByType<PlayerMotor>() != null, "Player rig");

            int villagers = Object.FindObjectsByType<Villager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Line(villagers > 0, $"Villagers: {villagers}" + (villagers == 0 ? "  → run VR Assignment → Add Villagers" : ""));

            var bridges = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => BridgeRootNames.IsMatch(t.name) && (t.parent == null || !t.parent.name.ToLowerInvariant().Contains("bridge"))).ToList();
            int fixedBridges = bridges.Count(b => b.Find("VR Walkable") != null);
            Line(bridges.Count > 0 && fixedBridges == bridges.Count, $"Bridges fixed: {fixedBridges} of {bridges.Count}" + (fixedBridges < bridges.Count ? "  → run VR Assignment → Fix Bridges" : ""));

            int props = Object.FindObjectsByType<GazeInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length - villagers;
            Line(props > 0, $"Interactive props: {props}");
            bool inBuild = EditorBuildSettings.scenes.Any(sc => sc.enabled && sc.path == scene.path);
            Line(inBuild && EditorBuildSettings.scenes.FirstOrDefault(sc => sc.enabled)?.path == scene.path, inBuild ? "Scene is in the build (first)" : "Scene is NOT in the build list");
            Line(true, $"Build platform: {EditorUserBuildSettings.activeBuildTarget}");

            EditorUtility.DisplayDialog("VR Assignment – Check Scene", string.Join("\n", lines) +
                "\n\nAfter changes: Cmd+S, then Build Profiles → Build → Append, then ▶ in Xcode.", "OK");
        }

        [MenuItem("VR Assignment/Open Village Scene", priority = 20)]
        public static void OpenScene()
        {
            string path = AssetDatabase.FindAssets("SampleScene t:Scene")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .FirstOrDefault(p => p.Contains("Medieval Village"));
            if (path == null) { EditorUtility.DisplayDialog("VR Assignment", "Couldn't find the village SampleScene. Import Medieval_Village.unitypackage first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }
    }
}
