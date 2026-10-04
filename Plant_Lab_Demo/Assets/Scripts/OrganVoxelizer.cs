using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an OrganCompound from a leaf/flower prefab (Unity side, once at the start):
/// 1. takes the prefab's mesh in every morph state (basis + each blend shape at 100)
/// 2. marks every grid cell its surface passes through
/// 3. fills closed volumes from outside inward (flood fill): enclosed cells become solid,
///    anything connected to the outside (a cup's opening, gaps between petals) stays free
/// 4. merges neighboring solid cells into as few boxes as possible
/// </summary>
public static class OrganVoxelizer
{
    public static OrganCompound Build(GameObject prefab, int resolution)
    {
        //a temporary instance (far away, removed right away), so meshes can be baked in any morph state
        GameObject probe = Object.Instantiate(prefab, new Vector3(0f, -10000f, 0f), Quaternion.identity);
        Transform root = probe.transform;
        root.localScale = Vector3.one;//measure at size 1
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        var skinned = probe.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var filters = probe.GetComponentsInChildren<MeshFilter>(true);

        //states: basis ("") + every blend shape name found on any part
        var states = new List<string> { "" };
        foreach (var smr in skinned)
            if (smr.sharedMesh != null)
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string n = smr.sharedMesh.GetBlendShapeName(i);
                    if (!states.Contains(n)) states.Add(n);
                }

        //triangles (3 points each, in the root's space) per state
        var tris = new List<Vector3>[states.Count];
        var baked = new Mesh();
        bool warned = false;
        for (int st = 0; st < states.Count; st++)
        {
            tris[st] = new List<Vector3>();
            foreach (var smr in skinned)
            {
                Mesh m = smr.sharedMesh;
                if (m == null) continue;
                for (int i = 0; i < m.blendShapeCount; i++) smr.SetBlendShapeWeight(i, 0f);
                int idx = st > 0 ? m.GetBlendShapeIndex(states[st]) : -1;
                if (idx >= 0) smr.SetBlendShapeWeight(idx, 100f);
                smr.BakeMesh(baked);//the mesh as it looks in this state (a readable copy)
                AddTriangles(baked, toRoot * smr.transform.localToWorldMatrix, tris[st]);
            }
            foreach (var mf in filters)
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                if (!m.isReadable)
                {
                    //mesh data can't be read: use its bounding box as a stand-in
                    if (!warned) Debug.LogWarning($"OrganVoxelizer: mesh '{m.name}' of '{prefab.name}' isn't readable; " +
                        "enable Read/Write in the model's import settings for a precise collision shape.", prefab);
                    warned = true;
                    AddBoxTriangles(m.bounds, toRoot * mf.transform.localToWorldMatrix, tris[st]);
                }
                else AddTriangles(m, toRoot * mf.transform.localToWorldMatrix, tris[st]);
            }
        }
        Object.Destroy(baked);
        Object.Destroy(probe);

        //one grid for all states, so their solid cells can be combined directly
        bool any = false;
        Bounds all = new Bounds();
        foreach (var list in tris)
            foreach (Vector3 p in list)
            {
                if (!any) { all = new Bounds(p, Vector3.zero); any = true; }
                else all.Encapsulate(p);
            }
        var result = new OrganCompound { stateNames = states.ToArray(), perState = new OrganBox[states.Count][] };
        if (!any)
        {
            result.union = new OrganBox[0];
            for (int st = 0; st < states.Count; st++) result.perState[st] = result.union;
            return result;
        }
        float longest = Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z));
        float cell = Mathf.Max(longest / Mathf.Max(1, resolution), 1e-5f);
        //one empty cell of padding on every side, so the outside is connected all around
        Vector3 origin = all.min - Vector3.one * cell;
        int nx = Mathf.CeilToInt(all.size.x / cell) + 3, ny = Mathf.CeilToInt(all.size.y / cell) + 3, nz = Mathf.CeilToInt(all.size.z / cell) + 3;
        var grid = new Grid(nx, ny, nz, origin, cell);

        var unionSolid = new bool[nx * ny * nz];
        for (int st = 0; st < states.Count; st++)
        {
            bool[] surface = grid.MarkSurface(tris[st]);
            bool[] solid = grid.FillEnclosed(surface);
            result.perState[st] = grid.MergeIntoBoxes(solid);
            for (int i = 0; i < solid.Length; i++) unionSolid[i] |= solid[i];
        }
        result.union = grid.MergeIntoBoxes(unionSolid);
        result.cellSize = cell;
        result.boundsCenter = all.center;
        result.boundsRadius = all.extents.magnitude;
        return result;
    }

    static void AddTriangles(Mesh m, Matrix4x4 toRoot, List<Vector3> into)
    {
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        for (int i = 0; i < t.Length; i++) into.Add(toRoot.MultiplyPoint3x4(v[t[i]]));
    }

    static void AddBoxTriangles(Bounds b, Matrix4x4 toRoot, List<Vector3> into)
    {
        //the 12 triangles of a box's surface
        Vector3 c = b.center, e = b.extents;
        Vector3 P(int x, int y, int z) => toRoot.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3(x, y, z)));
        int[,] faces = { { -1, -1, -1, 1, -1, -1, 1, 1, -1, -1, 1, -1 }, { -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1 },
                         { -1, -1, -1, -1, 1, -1, -1, 1, 1, -1, -1, 1 }, { 1, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1 },
                         { -1, -1, -1, 1, -1, -1, 1, -1, 1, -1, -1, 1 }, { -1, 1, -1, 1, 1, -1, 1, 1, 1, -1, 1, 1 } };
        for (int f = 0; f < 6; f++)
        {
            Vector3 a = P(faces[f, 0], faces[f, 1], faces[f, 2]), bb = P(faces[f, 3], faces[f, 4], faces[f, 5]);
            Vector3 cc = P(faces[f, 6], faces[f, 7], faces[f, 8]), d = P(faces[f, 9], faces[f, 10], faces[f, 11]);
            into.Add(a); into.Add(bb); into.Add(cc);
            into.Add(a); into.Add(cc); into.Add(d);
        }
    }

    /// <summary>A regular 3D grid of cells, flattened into 1D arrays (index = x + nx * (y + ny * z)).</summary>
    class Grid
    {
        readonly int nx, ny, nz;
        readonly Vector3 origin;
        readonly float cell;

        public Grid(int nx, int ny, int nz, Vector3 origin, float cell)
        {
            this.nx = nx; this.ny = ny; this.nz = nz; this.origin = origin; this.cell = cell;
        }

        int Index(int x, int y, int z) => x + nx * (y + ny * z);

        /// <summary>Marks every cell a triangle passes through, by sampling each triangle densely (half a cell apart).</summary>
        public bool[] MarkSurface(List<Vector3> tris)
        {
            var marked = new bool[nx * ny * nz];
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                float longestEdge = Mathf.Max((b - a).magnitude, Mathf.Max((c - a).magnitude, (c - b).magnitude));
                int n = Mathf.Max(1, Mathf.CeilToInt(longestEdge / (cell * 0.5f)));
                for (int u = 0; u <= n; u++)
                    for (int w = 0; w <= n - u; w++)
                    {
                        Vector3 p = a + (b - a) * ((float)u / n) + (c - a) * ((float)w / n);
                        Vector3 g = (p - origin) / cell;
                        int x = Mathf.Clamp((int)g.x, 0, nx - 1), y = Mathf.Clamp((int)g.y, 0, ny - 1), z = Mathf.Clamp((int)g.z, 0, nz - 1);
                        marked[Index(x, y, z)] = true;
                    }
            }
            return marked;
        }

        /// <summary>
        /// Solid = surface cells plus every empty cell that can't be reached from the outside
        /// (flood fill over empty cells, starting at the padded border).
        /// </summary>
        public bool[] FillEnclosed(bool[] surface)
        {
            var outside = new bool[surface.Length];
            var queue = new Queue<int>();
            outside[0] = true;//corner cell: always empty thanks to the padding
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % nx, y = (i / nx) % ny, z = i / (nx * ny);
                TryVisit(x - 1, y, z); TryVisit(x + 1, y, z);
                TryVisit(x, y - 1, z); TryVisit(x, y + 1, z);
                TryVisit(x, y, z - 1); TryVisit(x, y, z + 1);
            }
            var solid = new bool[surface.Length];
            for (int i = 0; i < solid.Length; i++) solid[i] = !outside[i];
            return solid;

            void TryVisit(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return;
                int j = Index(x, y, z);
                if (outside[j] || surface[j]) return;
                outside[j] = true;
                queue.Enqueue(j);
            }
        }

        /// <summary>
        /// Greedy merging: from each unused solid cell, grow a box as far as possible along x,
        /// then along y (whole rows), then along z (whole layers).
        /// </summary>
        public OrganBox[] MergeIntoBoxes(bool[] solid)
        {
            var used = new bool[solid.Length];
            var boxes = new List<OrganBox>();
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        if (!solid[Index(x, y, z)] || used[Index(x, y, z)]) continue;
                        int x1 = x;
                        while (x1 + 1 < nx && Free(x1 + 1, y, z)) x1++;
                        int y1 = y;
                        while (y1 + 1 < ny && RowFree(x, x1, y1 + 1, z)) y1++;
                        int z1 = z;
                        while (z1 + 1 < nz && LayerFree(x, x1, y, y1, z1 + 1)) z1++;
                        for (int zz = z; zz <= z1; zz++)
                            for (int yy = y; yy <= y1; yy++)
                                for (int xx = x; xx <= x1; xx++) used[Index(xx, yy, zz)] = true;
                        Vector3 min = origin + new Vector3(x, y, z) * cell;
                        Vector3 max = origin + new Vector3(x1 + 1, y1 + 1, z1 + 1) * cell;
                        boxes.Add(new OrganBox { center = (min + max) * 0.5f, half = (max - min) * 0.5f });
                    }
            return boxes.ToArray();

            bool Free(int x, int y, int z) => solid[Index(x, y, z)] && !used[Index(x, y, z)];
            bool RowFree(int x0, int x1, int y, int z)
            {
                for (int x = x0; x <= x1; x++) if (!Free(x, y, z)) return false;
                return true;
            }
            bool LayerFree(int x0, int x1, int y0, int y1, int z)
            {
                for (int y = y0; y <= y1; y++) if (!RowFree(x0, x1, y, z)) return false;
                return true;
            }
        }
    }
}
