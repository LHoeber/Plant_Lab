using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shape of one cactus segment: a ribbed 3D ellipsoid (width x height x depth) that grows outward from its attachment
/// point. Plain math and mesh building, used by the tuning component (CactusSegmentPreview) and later by the plant body.
///
/// Segment space: the attachment point (base) is at the origin, the axis runs along +y to the tip (the point farthest from
/// the attachment, where a flower sits). Surface points are given by (angle around the axis, v): v = 0 at the base,
/// v = 1 at the tip.
/// </summary>
[Serializable]
public class CactusShape
{
    [Header("Size (meters, full grown)")]
    [Tooltip("Ellipsoid width (x).")]
    [Range(0.01f, 1f)] public float width = 0.2f;
    [Tooltip("Ellipsoid height along the axis, base to tip.")]
    [Range(0.01f, 1f)] public float height = 0.17f;
    [Tooltip("Ellipsoid depth (z).")]
    [Range(0.01f, 1f)] public float depth = 0.2f;

    [Header("Ribs")]
    [Range(0, 40)] public int ribs = 12;
    [Tooltip("How far the ribs stand out (fraction of the radius).")]
    [Range(0f, 0.4f)] public float ribDepth = 0.08f;
    [Tooltip("1 = round waves; higher = narrow ridges between wide, round valleys.")]
    [Range(1f, 8f)] public float ribSharpness = 2f;
    [Tooltip("Degrees the ribs turn around the axis from base to tip (spiral ribs).")]
    [Range(-180f, 180f)] public float twistDeg = 0f;

    [Header("Ends")]
    [Tooltip("Depth of the dip at the tip (fraction of the height).")]
    [Range(0f, 0.3f)] public float tipDip = 0.06f;
    [Tooltip("Width of the dip at the tip (fraction of the base-to-tip distance).")]
    [Range(0.02f, 0.5f)] public float tipDipWidth = 0.15f;
    [Tooltip("Narrower near the attachment (0 = plain ellipsoid, 1 = pointed base).")]
    [Range(0f, 1f)] public float baseTaper = 0.15f;

    [Header("Areoles (tufts on the rib ridges)")]
    [Tooltip("Tufts per rib, from areoleStart to areoleEnd.")]
    [Range(0, 30)] public int areolesPerRib = 6;
    [Tooltip("Where the tufts start and end along the segment (0 = base, 1 = tip).")]
    [Range(0f, 1f)] public float areoleStart = 0.15f;
    [Range(0f, 1f)] public float areoleEnd = 0.9f;
    [Tooltip("Shift every other rib's tufts by half a step (a staggered pattern).")]
    public bool staggerAreoles = true;
    [Tooltip("Random displacement of the tufts along the rib (fraction of their spacing).")]
    [Range(0f, 0.5f)] public float areoleJitter = 0.1f;

    [Header("Hills (tubercles: a bump under every tuft)")]
    [Tooltip("How far each tuft's bump stands out (fraction of the radius). 0 = smooth ribs. Strong + deep, sharp ribs = " +
             "spikes with a tuft on top. The bumps sit at the tufts' unjittered places: use little areole jitter with them.")]
    [Range(0f, 0.6f)] public float hillHeight = 0f;
    [Tooltip("1 = round bumps that merge into each other; higher = narrow peaks with long dips in between.")]
    [Range(1f, 8f)] public float hillSharpness = 2f;

    [Header("Withered look (try it with State > Wither on the preview)")]
    [Tooltip("Radius of a fully withered segment, as a fraction of its full radius: the ribs sink toward a thin core. " +
             "Only visible when the segment is withering (Wither above 0).")]
    [Range(0.1f, 1f)] public float witheredCore = 0.35f;

    /// <summary>A position on the surface with its outward normal, in segment space.</summary>
    public struct SurfacePoint { public Vector3 position, normal; public float angle, v; }

    /// <summary>
    /// Surface position at angle (radians, around the axis) and v (0 base .. 1 tip).
    /// growth: 0..1 scales the whole segment (1 = full size); wither: 0 = fresh, 1 = fully deflated.
    /// </summary>
    public Vector3 Point(float angle, float v, float growth = 1f, float wither = 0f)
    {
        float a = 0.5f * width, b = 0.5f * height, c = 0.5f * depth;
        float phi = Mathf.PI * Mathf.Clamp01(v);
        float s = Mathf.Sin(phi);
        float y = b * (1f - Mathf.Cos(phi));
        //narrower toward the base: the lower half is pulled in
        if (baseTaper > 0f && v < 0.5f) s *= 1f - baseTaper * Mathf.Pow(1f - 2f * v, 2f);
        //ribs: the radius varies around the axis; while withering they deepen and everything sinks toward the core
        float ribPhase = ribs * (angle + twistDeg * Mathf.Deg2Rad * v);
        float rib = ribs > 0 ? ribDepth * (1f + 1.5f * wither) * Profile(ribPhase) : 0f;
        //hills: bumps along each ridge under the tufts, narrow across like the ridge itself (fade out in the valleys)
        if (hillHeight > 0f && ribs > 0)
            rib += hillHeight * (1f + 1.5f * wither) * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ribPhase), ribSharpness) * Hill(ribPhase, v);
        float radial = s * (1f + rib) * Mathf.Lerp(1f, witheredCore, wither);
        //dip at the tip
        float t = 1f - v;
        y -= tipDip * height * Mathf.Exp(-(t / tipDipWidth) * (t / tipDipWidth));
        y *= Mathf.Lerp(1f, 0.92f, wither);//a withered segment also slumps a little
        return new Vector3(a * radial * Mathf.Cos(angle), y, c * radial * Mathf.Sin(angle)) * growth;
    }

    /// <summary>Rib wave: +1 on a ridge, -1 in a valley; sharpness makes ridges narrow and valleys wide.</summary>
    float Profile(float x) => 2f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(x), ribSharpness) - 1f;

    /// <summary>
    /// Bump along the nearest rib at height v: 1 on a tuft's place, 0 halfway to the next one and outside the tufts' range.
    /// Uses the same places as Areoles (without the jitter), including the staggering of every other rib.
    /// </summary>
    float Hill(float ribPhase, float v)
    {
        if (areolesPerRib <= 0) return 0f;
        float step = Mathf.Max(0f, areoleEnd - areoleStart) / areolesPerRib;
        if (step <= 1e-5f) return 0f;
        int r = Mathf.RoundToInt(ribPhase / (2f * Mathf.PI));
        r = ((r % ribs) + ribs) % ribs;//same rib numbering as Areoles (matters for the staggering with an odd rib count)
        bool shifted = staggerAreoles && r % 2 == 1;
        float shift = shifted ? 0.5f : 0f;
        int count = shifted ? areolesPerRib - 1 : areolesPerRib;
        if (count <= 0) return 0f;
        int m = Mathf.Clamp(Mathf.RoundToInt((v - areoleStart) / step - 0.5f - shift), 0, count - 1);
        float d = (v - (areoleStart + (m + 0.5f + shift) * step)) / step;//distance to that tuft, in tuft spacings
        if (Mathf.Abs(d) >= 0.5f) return 0f;
        return Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * d), hillSharpness);
    }

    /// <summary>Outward surface normal at (angle, v), from the surface's slopes.</summary>
    public Vector3 Normal(float angle, float v, float wither = 0f)
    {
        const float e = 1e-3f;
        if (v <= e) return Vector3.down;
        if (v >= 1f - e) return Vector3.up;
        //slopes per unit of v and angle (divided by the step, so they're not tiny numbers)
        Vector3 dv = (Point(angle, v + e, 1f, wither) - Point(angle, v - e, 1f, wither)) / (2f * e);
        Vector3 da = (Point(angle + e, v, 1f, wither) - Point(angle - e, v, 1f, wither)) / (2f * e);
        Vector3 n = Vector3.Cross(dv, da);
        //own normalization: Unity's .normalized returns (0,0,0) below length 1e-5, which the shader turns into NaN
        float len = n.magnitude;
        return len > 1e-9f ? n / len : Vector3.up;
    }

    /// <summary>The tip (where a flower sits): the point on the axis farthest from the attachment.</summary>
    public Vector3 Tip(float growth = 1f, float wither = 0f) => Point(0f, 1f, growth, wither);

    /// <summary>Builds (or rebuilds into `mesh`) the segment's surface: rings from base to tip, columns around the axis.</summary>
    public Mesh BuildMesh(Mesh mesh, int rings, int columns, float growth = 1f, float wither = 0f)
    {
        rings = Mathf.Max(3, rings);
        columns = Mathf.Max(3, columns);
        int w = columns + 1;//the first column again at the end, so the texture wraps without a seam in the coordinates
        var verts = new Vector3[(rings + 1) * w];
        var normals = new Vector3[verts.Length];
        var uv = new Vector2[verts.Length];
        for (int i = 0; i <= rings; i++)
        {
            float v = i / (float)rings;
            for (int j = 0; j <= columns; j++)
            {
                float angle = 2f * Mathf.PI * j / columns;
                int k = i * w + j;
                verts[k] = Point(angle, v, growth, wither);
                normals[k] = Normal(angle, v, wither);
                uv[k] = new Vector2(j / (float)columns, v);
            }
        }
        var tris = new int[rings * columns * 6];
        int n = 0;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < columns; j++)
            {
                int a = i * w + j, b = (i + 1) * w + j, c = (i + 1) * w + j + 1, d = i * w + j + 1;
                //clockwise seen from outside (Unity's front faces)
                tris[n++] = a; tris[n++] = b; tris[n++] = c;
                tris[n++] = a; tris[n++] = c; tris[n++] = d;
            }
        if (mesh == null) mesh = new Mesh { name = "Cactus segment" };
        mesh.Clear();
        mesh.indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Where the tufts sit: on every rib ridge, areolesPerRib of them between areoleStart and areoleEnd
    /// (every other rib shifted by half a step if staggerAreoles), slightly jittered with the given seed.
    /// </summary>
    public List<SurfacePoint> Areoles(int seed, float growth = 1f, float wither = 0f)
    {
        var result = new List<SurfacePoint>();
        if (ribs <= 0 || areolesPerRib <= 0) return result;
        var rng = new System.Random(seed);
        float span = Mathf.Max(0f, areoleEnd - areoleStart);
        float step = span / areolesPerRib;
        for (int r = 0; r < ribs; r++)
            for (int m = 0; m < areolesPerRib; m++)
            {
                float jitter = ((float)rng.NextDouble() * 2f - 1f) * areoleJitter * step;//always drawn: same pattern per seed
                //staggered ribs: the tufts sit between those of the neighboring ribs (one fewer, so none goes past the end)
                float shift = staggerAreoles && r % 2 == 1 ? 0.5f : 0f;
                if (shift > 0f && m == areolesPerRib - 1) continue;
                float v = areoleStart + (m + 0.5f + shift) * step + jitter;
                v = Mathf.Clamp(v, 0.01f, 0.99f);
                //on the ridge: where the rib wave is at its top, following the twist
                float angle = (2f * Mathf.PI * r - twistDeg * Mathf.Deg2Rad * v * ribs) / ribs;
                result.Add(new SurfacePoint { angle = angle, v = v, position = Point(angle, v, growth, wither), normal = Normal(angle, v, wither) });
            }
        return result;
    }
}
