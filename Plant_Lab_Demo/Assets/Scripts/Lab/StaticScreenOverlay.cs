using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A static "screen glass" look over the console screen: darker edges (vignette), a soft diagonal glare and faint
/// scanlines. Computed once and never changes, so it carries no information (time-varying noise would add
/// uncontrolled noise to what participants see). It's only on the participant's screen, not in the observation.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class StaticScreenOverlay : MonoBehaviour
{
    [Range(0f, 1f)] public float vignette = 0.45f;
    [Range(0f, 1f)] public float glare = 0.12f;
    [Range(0f, 1f)] public float scanlines = 0.06f;
    [Tooltip("Rows per scanline period, in overlay pixels.")]
    [Range(2, 8)] public int scanlinePeriod = 3;
    [Tooltip("Overlay texture size; same aspect as the screen, so the scanlines stay one texture row thick.")]
    public int width = 768, height = 512;

    Texture2D tex;

    void Awake()
    {
        tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ScreenOverlay" };
        var px = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                //vignette: darker toward the edges and corners
                float dx = u - 0.5f, dy = v - 0.5f;//in screen fractions: the vignette follows the screen shape
                float edge = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.35f) / 0.4f);
                float dark = vignette * edge * edge;
                //glare: a soft bright band from top-left toward the middle
                float band = Mathf.Abs((u - (1f - v)) * 0.7071f + 0.18f);
                float bright = glare * Mathf.Exp(-band * band / 0.004f) * Mathf.Clamp01(1.4f - (u + (1f - v)));
                //scanlines: every few rows slightly darker
                if (y % scanlinePeriod == 0) dark += scanlines;
                //one premultiplied-looking color: white for glare, black for darkening
                float a = Mathf.Clamp01(dark + bright);
                float white = a > 1e-4f ? bright / a : 0f;
                byte c = (byte)(white * 255f);
                px[y * width + x] = new Color32(c, c, c, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var img = GetComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;//never blocks button presses
    }

    void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }
}
