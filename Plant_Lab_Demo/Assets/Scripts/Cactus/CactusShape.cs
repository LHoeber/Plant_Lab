using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shape of one cactus segment: a ribbed 3D ellipsoid (width x height x depth, "the ball") that grows outward from its
/// attachment point, optionally stretched into a column by tip growth. Plain math and mesh building, used by the tuning
/// component (CactusSegmentPreview) and later by the plant body.
///
/// Segment space: the attachment point (base) is at the origin, the axis runs along +y to the tip (the point farthest from
/// the attachment, where a flower sits). Surface points are given by (angle around the axis, v): v = 0 at the base,
/// v = 1 at the tip.
///
/// Growth (0..1): the ball swells from a bud to full size. With column growth on, a straight-sided column is inserted at
/// the ball's equator and gets longer: new tissue forms only at the tip (with new tufts), what is below stays in place.
/// The column can start while the ball is still swelling. Without column growth the ball simply scales up.
///
/// Internally the outline from base to tip is walked with a parameter p: 0..pi/2 = lower half of the ball,
/// then the column (length in units of the ball's half height), then the upper half of the ball.
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

    [Header("Column growth (tip growth)")]
    [Tooltip("Off: the ball scales up while growing (width x height x depth at full size). " +
             "On: after (or while) the ball swells, a straight-sided column grows at the tip.")]
    public bool columnGrowth = false;
    [Tooltip("Extra length the column adds at full growth (m), on top of the ball's height.")]
    [Range(0f, 1.5f)] public float columnLength = 0.25f;
    [Tooltip("Growth (0..1) at which the ball has reached its full size.")]
    [Range(0.05f, 1f)] public float ballDone = 0.6f;
    [Tooltip("Growth (0..1) at which the column starts. Below Ball Done: the column grows while the ball is still swelling.")]
    [Range(0f, 0.99f)] public float columnStart = 0.3f;

    [Header("Ribs")]
    [Range(0, 40)] public int ribs = 12;
    [Tooltip("How far the ribs stand out (fraction of the radius).")]
    [Range(0f, 0.4f)] public float ribDepth = 0.08f;
    [Tooltip("1 = round waves; higher = narrow ridges between wide, round valleys.")]
    [Range(1f, 8f)] public float ribSharpness = 2f;
    [Tooltip("Degrees the ribs turn around the axis from base to tip of the ball (spiral ribs; a column continues the turn).")]
    [Range(-180f, 180f)] public float twistDeg = 0f;

    [Header("Ends")]
    [Tooltip("Depth of the dip at the tip (fraction of the ball's height): the top is pressed in as if by a finger, " +
             "deepest on the axis (a heart-shaped outline seen from the side).")]
    [Range(0f, 0.3f)] public float tipDip = 0.06f;
    [Tooltip("Width of the dip at the tip (fraction of the ball's radius).")]
    [Range(0.05f, 1f)] public float tipDipWidth = 0.5f;
    [Tooltip("Narrower near the attachment (0 = plain ellipsoid, 1 = pointed base).")]
    [Range(0f, 1f)] public float baseTaper = 0.15f;

    [Header("Areoles (tufts on the rib ridges)")]
    [Tooltip("Tufts per rib on the ball, from areoleStart to areoleEnd. A column gets more at the same spacing.")]
    [Range(0, 30)] public int areolesPerRib = 6;
    [Tooltip("Where the tufts start and end along the ball (0 = base, 1 = tip). The end also sets how far below the tip " +
             "new tufts appear on a growing column.")]
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
    /// The segment at one growth state: scale = size of the ball (0..1, everything is scaled by it),
    /// column = column length inside the scaled shape, in units of the ball's half height.
    /// </summary>
    struct State
    {
        public float scale, column;
        public float Length => Mathf.PI + column;//outline parameter p from base to tip
    }

    State StateAt(float growth)
    {
        growth = Mathf.Clamp01(growth);
        if (!columnGrowth) return new State { scale = growth, column = 0f };
        float scale = Mathf.Clamp(growth / ballDone, 1e-4f, 1f);
        float c = Mathf.Clamp01((growth - columnStart) / Mathf.Max(1e-4f, 1f - columnStart));
        //the column's real length is columnLength * c; inside the scaled shape it's that divided by the scale
        float column = columnLength * c / scale / Mathf.Max(1e-4f, 0.5f * height);
        return new State { scale = scale, column = column };
    }

    /// <summary>
    /// Surface position at angle (radians, around the axis) and v (0 base .. 1 tip).
    /// growth: 0..1 (1 = full size); wither: 0 = fresh, 1 = fully deflated.
    /// </summary>
    public Vector3 Point(float angle, float v, float growth = 1f, float wither = 0f) =>
        Point(angle, Mathf.Clamp01(v) * StateAt(growth).Length, StateAt(growth), wither);

    Vector3 Point(float angle, float p, State st, float wither)
    {
        float a = 0.5f * width, b = 0.5f * height, c = 0.5f * depth;
        //outline: lower half of the ball, column, upper half of the ball
        float half = 0.5f * Mathf.PI;
        float s, y;
        bool upper = p > half + st.column;
        if (p <= half) { s = Mathf.Sin(p); y = b * (1f - Mathf.Cos(p)); }
        else if (!upper) { s = 1f; y = b + (p - half) * b; }
        else { float phi = p - st.column; s = Mathf.Sin(phi); y = b * (1f - Mathf.Cos(phi)) + st.column * b; }
        s = Mathf.Max(0f, s);
        //narrower toward the base: the lower half is pulled in
        if (baseTaper > 0f && p < half) s *= 1f - baseTaper * Mathf.Pow(1f - p / half, 2f);
        //dip at the tip: pressed in, deepest on the axis. Depends on the distance from the axis, so the outline can't fold.
        if (upper && tipDip > 0f && s < tipDipWidth)
        {
            float d = 1f - s / tipDipWidth;
            y -= tipDip * height * d * d;
        }
        //ribs: the radius varies around the axis; while withering they deepen and everything sinks toward the core
        float ribPhase = RibPhase(angle, p);
        float rib = ribs > 0 ? ribDepth * (1f + 1.5f * wither) * Profile(ribPhase) : 0f;
        //hills: bumps along each ridge under the tufts, narrow across like the ridge itself (fade out in the valleys)
        if (hillHeight > 0f && ribs > 0)
            rib += hillHeight * (1f + 1.5f * wither) * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ribPhase), ribSharpness) * Hill(ribPhase, p, st);
        float radial = s * (1f + rib) * Mathf.Lerp(1f, witheredCore, wither);
        y *= Mathf.Lerp(1f, 0.92f, wither);//a withered segment also slumps a little
        return new Vector3(a * radial * Mathf.Cos(angle), y, c * radial * Mathf.Sin(angle)) * st.scale;
    }

    /// <summary>Phase of the rib wave: a multiple of 2 pi on a ridge. The twist turns with p (a column continues it).</summary>
    float RibPhase(float angle, float p) => ribs * (angle + twistDeg * Mathf.Deg2Rad * p / Mathf.PI);

    /// <summary>Rib wave: +1 on a ridge, -1 in a valley; sharpness makes ridges narrow and valleys wide.</summary>
    float Profile(float x) => 2f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(x), ribSharpness) - 1f;

    // --- tuft places (areoles) along a rib, in the outline parameter p ---

    float AreoleStep => Mathf.Max(0f, areoleEnd - areoleStart) * Mathf.PI / Mathf.Max(1, areolesPerRib);

    /// <summary>Tufts on rib r that exist at this growth state: from areoleStart up to the same distance below the tip as on the full ball.</summary>
    int AreoleCount(int r, State st)
    {
        if (areolesPerRib <= 0 || AreoleStep <= 1e-5f) return 0;
        float shift = staggerAreoles && r % 2 == 1 ? 0.5f : 0f;
        float top = st.Length - (1f - areoleEnd) * Mathf.PI;
        int n = Mathf.FloorToInt((top - areoleStart * Mathf.PI) / AreoleStep - 0.5f - shift - 1e-4f) + 1;
        return Mathf.Max(0, n);
    }

    float AreoleP(int r, int m)
    {
        float shift = staggerAreoles && r % 2 == 1 ? 0.5f : 0f;
        return areoleStart * Mathf.PI + (m + 0.5f + shift) * AreoleStep;
    }

    /// <summary>
    /// Bump along the nearest rib at p: 1 on a tuft's place, 0 halfway to the next one and outside the tufts' range.
    /// Uses the same places as Areoles (without the jitter), including the staggering of every other rib.
    /// </summary>
    float Hill(float ribPhase, float p, State st)
    {
        int r = Mathf.RoundToInt(ribPhase / (2f * Mathf.PI));
        r = ((r % ribs) + ribs) % ribs;//same rib numbering as Areoles (matters for the staggering with an odd rib count)
        int count = AreoleCount(r, st);
        if (count <= 0) return 0f;
        float shift = staggerAreoles && r % 2 == 1 ? 0.5f : 0f;
        float step = AreoleStep;
        int m = Mathf.Clamp(Mathf.RoundToInt((p - areoleStart * Mathf.PI) / step - 0.5f - shift), 0, count - 1);
        float d = (p - AreoleP(r, m)) / step;//distance to that tuft, in tuft spacings
        if (Mathf.Abs(d) >= 0.5f) return 0f;
        return Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * d), hillSharpness);
    }

    /// <summary>Outward surface normal at (angle, v), from the surface's slopes.</summary>
    public Vector3 Normal(float angle, float v, float growth = 1f, float wither = 0f)
    {
        State st = StateAt(growth);
        return Normal(angle, Mathf.Clamp01(v) * st.Length, st, wither);
    }

    Vector3 Normal(float angle, float p, State st, float wither)
    {
        const float e = 1e-3f;
        if (p <= e) return Vector3.down;
        if (p >= st.Length - e) return Vector3.up;
        //slopes per unit of p and angle (divided by the step, so they're not tiny numbers); the scale doesn't change directions
        st.scale = 1f;
        Vector3 dp = (Point(angle, p + e, st, wither) - Point(angle, p - e, st, wither)) / (2f * e);
        Vector3 da = (Point(angle + e, p, st, wither) - Point(angle - e, p, st, wither)) / (2f * e);
        Vector3 n = Vector3.Cross(dp, da);
        //own normalization: Unity's .normalized returns (0,0,0) below length 1e-5, which the shader turns into NaN
        float len = n.magnitude;
        return len > 1e-9f ? n / len : Vector3.up;
    }

    /// <summary>The tip (where a flower sits): the point on the axis farthest from the attachment, at the bottom of the dip.</summary>
    public Vector3 Tip(float growth = 1f, float wither = 0f) => Point(0f, 1f, growth, wither);

    /// <summary>
    /// Builds (or rebuilds into `mesh`) the segment's surface: rings from base to tip (for the ball; a column gets more at
    /// the same spacing), columns around the axis.
    /// </summary>
    public Mesh BuildMesh(Mesh mesh, int rings, int columns, float growth = 1f, float wither = 0f)
    {
        State st = StateAt(growth);
        rings = Mathf.Max(3, Mathf.CeilToInt(Mathf.Max(3, rings) * st.Length / Mathf.PI));
        columns = Mathf.Max(3, columns);
        int w = columns + 1;//the first column again at the end, so the texture wraps without a seam in the coordinates
        var verts = new Vector3[(rings + 1) * w];
        var normals = new Vector3[verts.Length];
        var uv = new Vector2[verts.Length];
        for (int i = 0; i <= rings; i++)
        {
            float v = i / (float)rings;
            float p = v * st.Length;
            for (int j = 0; j <= columns; j++)
            {
                float angle = 2f * Mathf.PI * j / columns;
                int k = i * w + j;
                verts[k] = Point(angle, p, st, wither);
                normals[k] = Normal(angle, p, st, wither);
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
    /// Where the tufts sit: on every rib ridge, evenly spaced from areoleStart (every other rib shifted by half a step if
    /// staggerAreoles), slightly jittered with the given seed. A growing column gets new ones below the tip; existing ones
    /// keep their place and jitter.
    /// </summary>
    public List<SurfacePoint> Areoles(int seed, float growth = 1f, float wither = 0f)
    {
        var result = new List<SurfacePoint>();
        if (ribs <= 0 || areolesPerRib <= 0) return result;
        State st = StateAt(growth);
        float step = AreoleStep;
        for (int r = 0; r < ribs; r++)
        {
            int count = AreoleCount(r, st);
            for (int m = 0; m < count; m++)
            {
                //jitter from a hash of (seed, rib, tuft): a tuft keeps its jitter when new ones appear
                float jitter = (Hash01(seed, r, m) * 2f - 1f) * areoleJitter * step;
                float p = Mathf.Clamp(AreoleP(r, m) + jitter, 0.01f, st.Length - 0.01f);
                //on the ridge: where the rib wave is at its top, following the twist
                float angle = (2f * Mathf.PI * r) / ribs - twistDeg * Mathf.Deg2Rad * p / Mathf.PI;
                result.Add(new SurfacePoint
                {
                    angle = angle, v = p / st.Length,
                    position = Point(angle, p, st, wither), normal = Normal(angle, p, st, wither),
                });
            }
        }
        return result;
    }

    static float Hash01(int seed, int a, int b)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
