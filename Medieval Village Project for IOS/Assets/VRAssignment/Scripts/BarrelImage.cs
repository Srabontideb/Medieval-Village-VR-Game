using UnityEngine;
using UnityEngine.UI;

namespace VRAssignment
{
    /// <summary>
    /// Shows one eye's camera image with Cardboard-style BARREL distortion
    /// (the rounded "bulging" shape). Cardboard lenses bend the picture the
    /// opposite way (pincushion), so after the lens it looks straight again.
    /// Implemented as a RawImage drawn on a grid mesh whose vertices are pulled
    /// in towards the centre more strongly the further they are from it.
    /// </summary>
    public class BarrelImage : RawImage
    {
        [Tooltip("Distortion strength. 0 = flat rectangle, ~0.2 = Cardboard look.")]
        [Range(0f, 0.4f)] public float strength = 0.22f;
        [Range(4, 48)] public int gridSize = 24;

        /// <summary>Where a point of the eye camera's image (viewport 0..1) ends up inside this rect (local, centre = 0,0).</summary>
        public Vector2 Map(Vector2 uv)
        {
            Vector2 p = uv * 2f - Vector2.one;              // -1..1
            float k = Mathf.Clamp(strength, 0f, 0.45f);
            Vector2 d = p * ((1f - k * p.sqrMagnitude) / (1f - k)); // edge midpoints stay at ±1, corners pulled in
            Rect r = rectTransform.rect;
            return new Vector2(r.center.x + d.x * r.width * 0.5f, r.center.y + d.y * r.height * 0.5f);
        }

        public void SetStrength(float k)
        {
            if (Mathf.Approximately(k, strength)) return;
            strength = k;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 c = color;
            Rect uvr = uvRect;
            int n = Mathf.Max(2, gridSize);
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                {
                    var uv = new Vector2((float)x / n, (float)y / n);
                    vh.AddVert(Map(uv), c, new Vector2(uvr.x + uv.x * uvr.width, uvr.y + uv.y * uvr.height));
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * (n + 1) + x;
                    vh.AddTriangle(i, i + n + 1, i + n + 2);
                    vh.AddTriangle(i, i + n + 2, i + 1);
                }
        }
    }
}
