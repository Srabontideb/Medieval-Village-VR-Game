using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace VRAssignment
{
    /// <summary>
    /// Navigation aid (mono mode): a round top-down mini-map in the corner.
    ///  • A camera high above the player renders the village into a texture.
    ///  • The map turns with you ("heading up"): the arrow in the middle is you, pointing where you look.
    ///  • Tap anywhere on the map to walk there (a marker shows the destination).
    ///  • [Zoom] switches between 15 m / 30 m / 60 m around you.
    /// Hidden in Cardboard mode (a corner map doesn't work through the lenses).
    /// </summary>
    public class MiniMap : MonoBehaviour
    {
        PlayerMotor motor;
        StereoCameraRig rig;
        Action<string> toast;

        Camera mapCam;
        RenderTexture rt;
        RectTransform frame, view, targetDot;
        Text zoomText;
        bool visible = true;

        readonly float[] zoomLevels = { 15f, 30f, 60f }; // metres from the centre to the edge
        int zoomIndex = 1;

        const float Size = 380f;
        const string KeyZoom = "vr_mapZoom";

        public RectTransform Frame => frame;

        public void Init(PlayerMotor motor, StereoCameraRig rig, Transform parent, Sprite circle, Sprite triangle, Sprite rounded, Font font, Action<string> toast)
        {
            this.motor = motor;
            this.rig = rig;
            this.toast = toast;
            zoomIndex = Mathf.Clamp(PlayerPrefs.GetInt(KeyZoom, 1), 0, zoomLevels.Length - 1);

            // ---- top-down camera ----
            var camGo = new GameObject("MiniMap Camera");
            mapCam = camGo.AddComponent<Camera>();
            mapCam.orthographic = true;
            mapCam.orthographicSize = zoomLevels[zoomIndex];
            mapCam.nearClipPlane = 1f;
            mapCam.farClipPlane = 300f;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = new Color(0.36f, 0.56f, 0.72f);
            mapCam.depth = -10;
            rt = new RenderTexture(384, 384, 16) { name = "MiniMap", antiAliasing = 1 };
            mapCam.targetTexture = rt;
            var data = mapCam.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            // ---- UI: dark ring, circular masked map, arrow, target dot, zoom button ----
            frame = NewImage("Mini-map", parent, circle, new Color(0.08f, 0.09f, 0.11f, 0.9f), false).rectTransform;
            frame.anchorMin = frame.anchorMax = Vector2.zero;
            frame.pivot = Vector2.zero;
            frame.sizeDelta = new Vector2(Size, Size);
            frame.anchoredPosition = new Vector2(30, 30);

            var maskImg = NewImage("Mask", frame, circle, Color.white, true);
            Stretch(maskImg.rectTransform, 8);
            maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            maskImg.gameObject.AddComponent<MiniMapTap>().map = this; // tap = walk there

            var raw = new GameObject("Map View", typeof(RectTransform)).AddComponent<RawImage>();
            raw.transform.SetParent(maskImg.transform, false);
            raw.texture = rt;
            raw.raycastTarget = false;
            view = raw.rectTransform;
            Stretch(view, 0);

            targetDot = NewImage("Target", maskImg.transform, circle, new Color(1f, 0.74f, 0.25f), false).rectTransform;
            targetDot.sizeDelta = new Vector2(26, 26);
            targetDot.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            targetDot.gameObject.SetActive(false);

            var arrow = NewImage("You", maskImg.transform, triangle, Color.white, false);
            arrow.rectTransform.sizeDelta = new Vector2(44, 44);
            var outline = arrow.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f);
            outline.effectDistance = new Vector2(2, -2);

            // Zoom button above the map
            var zoomBg = NewImage("Zoom", frame, rounded, new Color(0.1f, 0.11f, 0.14f, 0.85f), true);
            var zr = zoomBg.rectTransform;
            zr.anchorMin = zr.anchorMax = new Vector2(1, 1);
            zr.pivot = new Vector2(1, 0);
            zr.sizeDelta = new Vector2(190, 70);
            zr.anchoredPosition = new Vector2(0, 8);
            zoomBg.gameObject.AddComponent<Button>().onClick.AddListener(CycleZoom);
            zoomText = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            zoomText.transform.SetParent(zr, false);
            Stretch(zoomText.rectTransform, 4);
            zoomText.font = font; zoomText.fontSize = 28; zoomText.alignment = TextAnchor.MiddleCenter;
            zoomText.color = Color.white; zoomText.raycastTarget = false;
            UpdateZoomText();
        }

        public void SetVisible(bool on)
        {
            visible = on;
            if (frame != null) frame.gameObject.SetActive(on);
        }

        void CycleZoom()
        {
            zoomIndex = (zoomIndex + 1) % zoomLevels.Length;
            mapCam.orthographicSize = zoomLevels[zoomIndex];
            PlayerPrefs.SetInt(KeyZoom, zoomIndex);
            UpdateZoomText();
        }

        void UpdateZoomText() => zoomText.text = $"Zoom: {zoomLevels[zoomIndex] * 2f:0} m";

        void LateUpdate()
        {
            if (mapCam == null) return;
            bool show = visible && !rig.IsVR && frame.gameObject.activeInHierarchy;
            mapCam.enabled = show;
            if (!show) return;

            // Follow the player, high above, rotated so "up" on the map = the way you're looking.
            Vector3 p = motor.transform.position;
            mapCam.transform.SetPositionAndRotation(new Vector3(p.x, p.y + 80f, p.z),
                Quaternion.Euler(90f, motor.head.eulerAngles.y, 0f));

            // Destination marker
            bool hasTarget = motor.IsWalkingToTarget;
            if (hasTarget)
            {
                Vector3 vp = mapCam.WorldToViewportPoint(motor.Target);
                Vector2 local = new Vector2((vp.x - 0.5f) * view.rect.width, (vp.y - 0.5f) * view.rect.height);
                float max = view.rect.width * 0.5f - 14f;
                if (local.magnitude > max) local = local.normalized * max; // pin to the edge if it's off the map
                targetDot.anchoredPosition = local;
            }
            targetDot.gameObject.SetActive(hasTarget);
        }

        /// <summary>Called by MiniMapTap: walk to the tapped spot on the map.</summary>
        public void TapAt(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(view, e.position, e.pressEventCamera, out Vector2 local)) return;
            Vector2 uv = new Vector2(local.x / view.rect.width + 0.5f, local.y / view.rect.height + 0.5f);
            if ((uv - new Vector2(0.5f, 0.5f)).magnitude > 0.5f) return; // outside the circle

            Vector3 w = mapCam.ViewportToWorldPoint(new Vector3(uv.x, uv.y, 1f));
            // Find the ground there: of everything below that spot, take the surface closest to our own height (so not a roof).
            var hits = Physics.RaycastAll(new Vector3(w.x, motor.transform.position.y + 100f, w.z), Vector3.down, 300f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return;
            RaycastHit best = hits[0];
            float py = motor.transform.position.y;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - py) < Mathf.Abs(best.point.y - py)) best = h;

            motor.WalkTo(best.point);
            toast?.Invoke("Mini-map tap → walk there");
        }

        void OnDestroy()
        {
            if (rt != null) rt.Release();
        }

        // ---- tiny UI helpers ----

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
