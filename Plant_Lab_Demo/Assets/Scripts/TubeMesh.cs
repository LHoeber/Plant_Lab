using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a tube with a given radius per center line point and appends it to shared vertex/triangle lists,
/// so several tubes (all branches) end up in one mesh.
/// </summary>
public static class TubeMesh
{
    /// <param name="pts">center line points, base first</param>
    /// <param name="radii">radius at each center line point</param>
    /// <param name="arc">scratch list, filled with the distance from the base for each point</param>
    public static void Append(List<Vector3> pts, List<float> radii, int radialSegments, List<float> arc,
                              List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
    {
        int n = pts.Count;
        if (n < 2) return;//a tube needs at least two points
        int stride = radialSegments + 1; // duplicate seam vertex for clean UVs
        int first = verts.Count;         // index of this tube's first vertex in the shared list

        // Arc length from base for each point.
        arc.Clear(); arc.Add(0f);
        for (int i = 1; i < n; i++) arc.Add(arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));

        // Parallel-transport frames so the tube doesn't twist
        //normal at base is used as reference and changed as little as possible along the tube
        Vector3 prevT = (pts[1] - pts[0]).normalized;
        //normal is chosen arbitrary between growing direction and either up, or right pointing vector(if growing direction itself is currently up)
        //but once it's chosen it's fixed
        Vector3 normal = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.99f ? Vector3.up : Vector3.right).normalized;

        //placing a ring of vertices around every point along the center line
        //makes sure cylinders transition smoothly over to one another, instead of having sticking-out edges of cylinders
        for (int i = 0; i < n; i++)
        {
            Vector3 t = i == 0 ? pts[1] - pts[0]//forward-looking vector
                      : i == n - 1 ? pts[n - 1] - pts[n - 2]//backward-looking vector
                      : pts[i + 1] - pts[i - 1];//vector from previous to next point, jumping current point
            t.Normalize();
            //take smallest rotation from previous to current t and change the normal by it
            //this makes sure indices of vertices in consecutive rings keep parallel, not twist
            if (i > 0) normal = Quaternion.FromToRotation(prevT, t) * normal;
            prevT = t;
            //gives the third normal vector, that completes the 3D reference grid around a segment
            Vector3 binormal = Vector3.Cross(t, normal);

            float radius = radii[i];
            //vertices along one ring +1, because ring needs to get closed
            for (int j = 0; j <= radialSegments; j++)
            {
                float a = (float)j / radialSegments * Mathf.PI * 2f;//position around circumference of unit circle
                //finding xyz coords of point in the plane that goes through the circle, normal to growing direction t
                Vector3 offset = Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal;
                verts.Add(pts[i] + offset * radius);//local coords of circle vertex relative to plant origin
                norms.Add(offset);//vector pointing from center line to circle vertex
                uvs.Add(new Vector2((float)j / radialSegments, arc[i]));
            }
        }

        //connecting neighboring rings via triangles
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                //ring i+1:   b ──────── b+1
                //            │ ╲         │
                //            │   ╲   ②  │
                //            │ ①   ╲    │
                //            │       ╲   │
                //ring i:     a ──────── a+1
                //
                //① = (a, a+1, b)      ② = (a+1, b+1, b)
                int a = first + i * stride + j;//index part of lower ring (offset by the tubes already in the list)
                int b = a + stride;//index part of upper ring
                tris.Add(a); tris.Add(a + 1); tris.Add(b);//left triangle
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);//right triangle
            }
        }
    }
}
