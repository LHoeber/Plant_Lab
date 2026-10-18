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

    public sealed class Geometry
    {
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Mark> Marks = new List<Mark>();
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

    public static Geometry Interpret(List<LModule> modules, double step = 1, double angle = 25, double width = 1)
    {
        var pos = new V3(0, 0, 0);
        V3 H = new V3(0, 0, 1), L = new V3(-1, 0, 0), U = new V3(0, -1, 0);
        double w = width;
        int depth = 0;
        var stack = new Stack<(V3 pos, V3 H, V3 L, V3 U, double w, int depth)>();
        var geo = new Geometry();
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
                case '[': stack.Push((pos, H, L, U, w, depth)); depth++; break;
                case ']':
                    if (stack.Count > 0) (pos, H, L, U, w, depth) = stack.Pop();//an unmatched ']' is ignored
                    break;
                default:
                    geo.Marks.Add(new Mark { symbol = m.Symbol, position = pos, heading = H, parameters = p, depth = depth });
                    break;
            }
        }
        return geo;
    }
}
