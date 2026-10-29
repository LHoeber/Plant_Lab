using System;
using System.Collections.Generic;

/// <summary>
/// Turtle interpretation of a module sequence (same symbols and conventions as the Python notebook):
/// walks once through the modules and records line segments and the positions of all other symbols.
///
///   F(len)  move forward and draw          f(len)  move forward without drawing
///   +(a) -(a)  turn left / right around U   &amp;(a) ^(a)  pitch down / up around L
///   \(a) /(a)  roll left / right around H   |  turn around (180 deg)
///   [  ]    save / restore the turtle state (start / end of a branch)
///   !(w)    set the segment width          any other symbol: recorded at the turtle's position
///
/// Cactus symbols (only with a segment type given; otherwise they're recorded like any other symbol):
///   S(s)    a cactus segment (size factor s, 1 = as tuned) with its base at the turtle and its axis along H;
///           the turtle then moves to the segment's tip (where a flower sits), so S S stacks segments
///   ~       to the surface ('@' is taken: it starts a rule's weight): from the current segment's center along H to
///           the nearest free areole (tuft), which the child replaces; the turtle stands there, heading out of the
///           surface. Turns before it choose the spot, roll first: /(b) &amp;(a) ~ = b degrees around the axis, then
///           a degrees away from the tip (after a pitch, a roll would only spin the turtle). No segment so far: ignored.
///
/// Coordinates are the notebook's: right-handed, Z up; the turtle starts at the origin with H = +Z.
/// ToUnity converts them to Unity's (left-handed, Y up) without mirroring the plant.
/// </summary>
public static class LTurtle
{
    public struct V3
    {
        public double x, y, z;
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator *(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static double Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    public struct Segment { public V3 start, end; public double width; public int depth; }
    public struct Mark { public char symbol; public V3 position, heading; public double[] parameters; public int depth; }

    /// <summary>A cactus segment placed by S: base position, frame (axis = H; segment x = L, z = U), size factor.</summary>
    public sealed class CactusSegment
    {
        public V3 position, H, L, U;
        public double size;
        public int depth, parent = -1, seed;
        /// <summary>Its areoles (segment space, full grown), and the ones child segments grow out of (no tuft there).</summary>
        public List<CactusShape.SurfacePoint> areoles;
        public readonly HashSet<int> usedAreoles = new HashSet<int>();
    }

    public sealed class Geometry
    {
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Mark> Marks = new List<Mark>();
        public readonly List<CactusSegment> CactusSegments = new List<CactusSegment>();
    }

    /// <summary>Notebook coordinates (Z up, right-handed) -> Unity (Y up, left-handed). Swapping Y and Z keeps the shape unmirrored.</summary>
    public static UnityEngine.Vector3 ToUnity(V3 p) => new UnityEngine.Vector3((float)p.x, (float)p.z, (float)p.y);

    /// <summary>Rotates v around a unit axis by deg degrees (right-handed).</summary>
    static V3 Rotate(V3 v, V3 axis, double deg)
    {
        double t = deg * Math.PI / 180.0, c = Math.Cos(t), s = Math.Sin(t);
        double d = V3.Dot(v, axis);
        V3 cr = V3.Cross(axis, v);
        return v * c + cr * s + axis * (d * (1 - c));
    }

    static V3 Normalized(V3 v)
    {
        double len = Math.Sqrt(V3.Dot(v, v));
        return len > 1e-12 ? v * (1 / len) : v;
    }

    /// <summary>
    /// Interprets the modules. With a cactus segment type, S and ~ place segments (sizes in meters; unit = meters per
    /// turtle unit; seed varies the segments' tufts).
    /// </summary>
    public static Geometry Interpret(List<LModule> modules, double step = 1, double angle = 25, double width = 1,
                                     CactusSegmentSettings cactus = null, double unit = 0.1, int seed = 0)
    {
        var pos = new V3(0, 0, 0);
        V3 H = new V3(0, 0, 1), L = new V3(-1, 0, 0), U = new V3(0, -1, 0);
        double w = width;
        int depth = 0;
        int current = -1;//the cactus segment the turtle is on (for ~)
        var stack = new Stack<(V3 pos, V3 H, V3 L, V3 U, double w, int depth, int current)>();
        var geo = new Geometry();
        double toUnits = 1 / Math.Max(1e-9, unit);
        //segment space (meters) -> turtle units, for a segment with the given frame and size
        V3 Local(UnityEngine.Vector3 p, CactusSegment c) => (c.L * p.x + c.H * p.y + c.U * p.z) * (c.size * toUnits);
        foreach (LModule m in modules)
        {
            double[] p = m.Params;
            double a = p.Length > 0 ? p[0] : angle;
            switch (m.Symbol)
            {
                case 'F':
                case 'f':
                    V3 next = pos + H * (p.Length > 0 ? p[0] : step);
                    if (m.Symbol == 'F') geo.Segments.Add(new Segment { start = pos, end = next, width = w, depth = depth });
                    pos = next;
                    break;
                case '+': H = Rotate(H, U, a); L = Rotate(L, U, a); break;
                case '-': H = Rotate(H, U, -a); L = Rotate(L, U, -a); break;
                case '&': H = Rotate(H, L, a); U = Rotate(U, L, a); break;
                case '^': H = Rotate(H, L, -a); U = Rotate(U, L, -a); break;
                case '\\': L = Rotate(L, H, a); U = Rotate(U, H, a); break;
                case '/': L = Rotate(L, H, -a); U = Rotate(U, H, -a); break;
                case '|': H = H * -1; L = L * -1; break;
                case '!': w = p.Length > 0 ? p[0] : width; break;
                case '[': stack.Push((pos, H, L, U, w, depth, current)); depth++; break;
                case ']':
                    if (stack.Count > 0) (pos, H, L, U, w, depth, current) = stack.Pop();//an unmatched ']' is ignored
                    break;
                case 'S' when cactus != null:
                {
                    var c = new CactusSegment
                    {
                        position = pos, H = H, L = L, U = U, depth = depth, parent = current,
                        size = p.Length > 0 ? p[0] : 1,
                        seed = unchecked(seed * 1000003 + geo.CactusSegments.Count * 7919),
                    };
                    c.areoles = cactus.shape.Areoles(c.seed);
                    current = geo.CactusSegments.Count;
                    geo.CactusSegments.Add(c);
                    pos = pos + Local(cactus.shape.Tip(), c);//on to the tip
                    break;
                }
                case '~' when cactus != null:
                {
                    if (current < 0) break;
                    CactusSegment c = geo.CactusSegments[current];
                    if (c.areoles.Count == 0) break;
                    //the heading in segment space, seen from the segment's center: the areole lying most in that direction
                    var dir = new UnityEngine.Vector3((float)V3.Dot(H, c.L), (float)V3.Dot(H, c.H), (float)V3.Dot(H, c.U));
                    var center = 0.5f * cactus.shape.Tip();
                    int best = -1, bestUsed = -1;
                    float bestScore = float.NegativeInfinity, bestUsedScore = float.NegativeInfinity;
                    for (int i = 0; i < c.areoles.Count; i++)
                    {
                        float score = UnityEngine.Vector3.Dot((c.areoles[i].position - center).normalized, dir);
                        if (c.usedAreoles.Contains(i)) { if (score > bestUsedScore) { bestUsedScore = score; bestUsed = i; } }
                        else if (score > bestScore) { bestScore = score; best = i; }
                    }
                    if (best < 0) best = bestUsed;//all taken: share one
                    c.usedAreoles.Add(best);
                    CactusShape.SurfacePoint spot = c.areoles[best];
                    pos = c.position + Local(spot.position, c);
                    //heading out of the surface; L and U follow (L kept as close as possible, U = H x L as everywhere)
                    V3 n = Normalized(c.L * spot.normal.x + c.H * spot.normal.y + c.U * spot.normal.z);
                    V3 l = L + n * -V3.Dot(L, n);
                    if (V3.Dot(l, l) < 1e-8) l = U + n * -V3.Dot(U, n);
                    H = n;
                    L = Normalized(l);
                    U = V3.Cross(H, L);
                    break;
                }
                default:
                    geo.Marks.Add(new Mark { symbol = m.Symbol, position = pos, heading = H, parameters = p, depth = depth });
                    break;
            }
        }
        return geo;
    }
}
