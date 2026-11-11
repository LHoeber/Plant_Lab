using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a closed mesh (e.g. the soil filling of a container, modeled in Blender) as substrate that roots can grow in.
/// The mesh is only used for the question "is this point inside?": on start it is baked into vertical columns
/// (seen from above, a grid of cells; per cell the height ranges that lie inside the mesh). A lookup is then just
/// a grid index plus a few comparisons. The shape can be anything closed: tapered pots, holes in the floor, a mound.
/// Put it on the soil object itself (it uses that object's MeshFilter). The soil object must NOT be on the
/// obstacle layer (roots grow through it, and the shoot starts on it); the container walls are the obstacles.
/// Needs Read/Write enabled on the mesh import for the WebGL build (the editor can always read meshes).
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class SoilVolume : MonoBehaviour
{
    [Tooltip("Side length of the grid cells seen from above, in meters. Smaller = closer to the mesh, slower bake. " +
             "Heights are exact, only the horizontal position is rounded to this.")]
    [Min(0.001f)] public float cellSize = 0.005f;

    [Tooltip("How strongly the substrate resists root spreading: 0 = none (water), 1 = maximal (dense soil).")]
    [Range(0f, 1f)] public float resistance = 0.5f;

    [Tooltip("Scene view: draw every n-th column of the baked volume while this object is selected.")]
    [Min(1)] public int gizmoStride = 6;

    /// <summary>All active volumes in the scene (plants ask all of them).</summary>
    public static readonly List<SoilVolume> All = new List<SoilVolume>();

    //baked data, in the mesh's local space: nx * nz columns over the mesh bounds (x, z);
    //column c has the sorted heights crossings[start[c] .. start[c+1]), in pairs (enter, exit)
    int nx, nz;
    float x0, z0, cx, cz;//grid origin (min corner) and cell size, local units
    int[] start;
    float[] crossings;
    Mesh bakedMesh;
    float bakedCellSize;
    Vector3 bakedScale;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    /// <summary>Is this world-space point inside the volume?</summary>
    public bool ContainsWorld(Vector3 world)
    {
        if (!EnsureBaked()) return false;
        Vector3 p = transform.InverseTransformPoint(world);
        int ix = Mathf.FloorToInt((p.x - x0) / cx), iz = Mathf.FloorToInt((p.z - z0) / cz);
        if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return false;
        int c = iz * nx + ix;
        for (int k = start[c]; k + 1 < start[c + 1]; k += 2)
        {
            if (p.y < crossings[k]) return false;//sorted: below this range, so below all following ones
            if (p.y <= crossings[k + 1]) return true;
        }
        return false;
    }

    /// <summary>Bakes (again) if the mesh, cell size or scale changed since the last bake. False = no usable mesh.</summary>
    bool EnsureBaked()
    {
        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) return false;
        if (mesh == bakedMesh && cellSize == bakedCellSize && transform.lossyScale == bakedScale && start != null) return true;
        if (!mesh.isReadable && !Application.isEditor)
        {
            Debug.LogError($"SoilVolume: mesh '{mesh.name}' is not readable. Enable Read/Write in its import settings.", this);
            bakedMesh = mesh; bakedCellSize = cellSize; bakedScale = transform.lossyScale;//log once
            start = new int[1]; crossings = new float[0]; nx = nz = 0;
            return false;
        }
        Bake(mesh);
        return true;
    }

    void Bake(Mesh mesh)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bakedMesh = mesh; bakedCellSize = cellSize; bakedScale = transform.lossyScale;

        //cell size in local units, so the cells are cellSize meters in the scene
        Vector3 s = transform.lossyScale;
        cx = cellSize / Mathf.Max(1e-6f, Mathf.Abs(s.x));
        cz = cellSize / Mathf.Max(1e-6f, Mathf.Abs(s.z));
        Bounds b = mesh.bounds;
        x0 = b.min.x; z0 = b.min.z;
        nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / cx));
        nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / cz));

        //every triangle adds the height where it crosses each column center above/below it.
        //Column centers are shifted by an odd fraction of a cell, so they never lie exactly on an edge or vertex
        //shared by two triangles (which would count the crossing twice)
        const float jx = 0.5137f, jz = 0.4729f;
        var hits = new List<float>[nx * nz];
        Vector3[] v = mesh.vertices;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] tri = mesh.GetTriangles(sub);
            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                Vector3 a = v[tri[t]], bb = v[tri[t + 1]], c = v[tri[t + 2]];
                //area of the triangle seen from above; vertical triangles don't cross any column
                float det = (bb.x - a.x) * (c.z - a.z) - (c.x - a.x) * (bb.z - a.z);
                if (Mathf.Abs(det) < 1e-12f) continue;
                int ix0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, bb.x, c.x) - x0) / cx - jx));
                int ix1 = Mathf.Min(nx - 1, Mathf.FloorToInt((Mathf.Max(a.x, bb.x, c.x) - x0) / cx - jx));
                int iz0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, bb.z, c.z) - z0) / cz - jz));
                int iz1 = Mathf.Min(nz - 1, Mathf.FloorToInt((Mathf.Max(a.z, bb.z, c.z) - z0) / cz - jz));
                for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float px = x0 + (ix + jx) * cx, pz = z0 + (iz + jz) * cz;
                    //barycentric coordinates of the column center in the triangle (seen from above)
                    float u = ((px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z)) / det;
                    float w = ((bb.x - a.x) * (pz - a.z) - (px - a.x) * (bb.z - a.z)) / det;
                    if (u < 0f || w < 0f || u + w > 1f) continue;
                    float y = a.y + u * (bb.y - a.y) + w * (c.y - a.y);
                    int col = iz * nx + ix;
                    (hits[col] ??= new List<float>(2)).Add(y);
                }
            }
        }

        //flatten: sorted heights per column, in (enter, exit) pairs. An odd count (mesh not closed there) drops the last
        start = new int[nx * nz + 1];
        var all = new List<float>();
        int odd = 0;
        for (int col = 0; col < hits.Length; col++)
        {
            start[col] = all.Count;
            List<float> h = hits[col];
            if (h == null) continue;
            h.Sort();
            int n = h.Count;
            if ((n & 1) == 1) { n--; odd++; }
            for (int k = 0; k < n; k++) all.Add(h[k]);
        }
        start[hits.Length] = all.Count;
        crossings = all.ToArray();
        if (odd > 0) Debug.LogWarning($"SoilVolume '{name}': {odd} columns cross the mesh an odd number of times. " +
                                      "Is the mesh closed (no holes in its surface)?", this);
        Debug.Log($"SoilVolume '{name}': baked {nx}x{nz} columns, {crossings.Length / 2} ranges in {clock.Elapsed.TotalMilliseconds:0.0} ms.", this);
    }

    void OnDrawGizmosSelected()
    {
        if (!EnsureBaked() || start == null) return;
        Gizmos.color = new Color(0.55f, 0.35f, 0.15f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        for (int iz = 0; iz < nz; iz += gizmoStride)
        for (int ix = 0; ix < nx; ix += gizmoStride)
        {
            int c = iz * nx + ix;
            float px = x0 + (ix + 0.5f) * cx, pz = z0 + (iz + 0.5f) * cz;
            for (int k = start[c]; k + 1 < start[c + 1]; k += 2)
                Gizmos.DrawLine(new Vector3(px, crossings[k], pz), new Vector3(px, crossings[k + 1], pz));
        }
    }
}
