using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VRAssignment
{
    /// <summary>One finger on the screen (from Input.touches, or the mouse in the Editor).</summary>
    public struct TouchPoint
    {
        public int id;
        public Vector2 position;
        public Vector2 delta;
        public TouchPhase phase;
    }

    /// <summary>
    /// Small helpers shared by the touch scripts. Uses Unity's classic mobile input
    /// (Input.touches – https://docs.unity3d.com/Manual/MobileInput.html).
    /// In the Editor the left mouse button behaves like one finger so you can test without a phone.
    /// </summary>
    public static class InputUtil
    {
        static Vector2 lastMouse;
        static int lastFrame = -1;
        static readonly List<TouchPoint> cache = new List<TouchPoint>();
        static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        /// <summary>Screen DPI with a sensible fallback (some devices report 0).</summary>
        public static float Dpi => Screen.dpi > 1f ? Screen.dpi : 300f;

        /// <summary>Convert inches to pixels on this screen.</summary>
        public static float Inches(float inches) => inches * Dpi;

        /// <summary>All touches this frame (computed once per frame, shared by all scripts).</summary>
        public static List<TouchPoint> Touches
        {
            get
            {
                if (lastFrame == Time.frameCount) return cache;
                lastFrame = Time.frameCount;
                cache.Clear();

                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    cache.Add(new TouchPoint { id = t.fingerId, position = t.position, delta = t.deltaPosition, phase = t.phase });
                }

                // Mouse = one finger, only on desktop/Editor (on phones Unity already mirrors touches to the mouse).
                if (cache.Count == 0 && !Application.isMobilePlatform && !StereoCameraRig.UnityRemoteConnected)
                {
                    Vector2 m = Input.mousePosition;
                    if (Input.GetMouseButtonDown(0))
                        cache.Add(new TouchPoint { id = 0, position = m, delta = Vector2.zero, phase = TouchPhase.Began });
                    else if (Input.GetMouseButton(0))
                        cache.Add(new TouchPoint { id = 0, position = m, delta = m - lastMouse, phase = (m - lastMouse).sqrMagnitude > 0f ? TouchPhase.Moved : TouchPhase.Stationary });
                    else if (Input.GetMouseButtonUp(0))
                        cache.Add(new TouchPoint { id = 0, position = m, delta = m - lastMouse, phase = TouchPhase.Ended });
                    lastMouse = m;
                }
                return cache;
            }
        }

        /// <summary>True if a screen position is on top of a UI element (button, slider, panel).</summary>
        public static bool IsOverUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            uiHits.Clear();
            EventSystem.current.RaycastAll(data, uiHits);
            return uiHits.Count > 0;
        }
    }
}
