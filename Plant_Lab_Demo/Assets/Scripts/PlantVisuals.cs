using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns the simulation into what you see: one mesh for all branches, and one GameObject per leaf.
/// Reads the simulation, never changes it.
/// </summary>
public class PlantVisuals
{
    readonly Transform root;                                   //the plant; leaves become its children
    readonly Mesh mesh;
    readonly List<Transform> leafVisuals = new List<Transform>(); //same order as PlantSimulation.Leaves (null = no prefab)

    //buffers, reused every rebuild to avoid newly allocating memory every frame
    readonly List<Vector3> pts = new List<Vector3>();//center line of one branch
    readonly List<float> radii = new List<float>();  //radius at each of those points
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> norms = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> tris = new List<int>();
    readonly List<float> arc = new List<float>();

    public PlantVisuals(Transform root, MeshFilter meshFilter)
    {
        this.root = root;
        mesh = new Mesh { name = "Plant" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;//allows more vertices than Int16 if needed
        mesh.MarkDynamic();//optimizes performance, because mesh gets rewritten very often
        meshFilter.sharedMesh = mesh;//hands the mesh over meshFilter to the meshRenderer for drawing it
    }

    /// <summary>Removes everything drawn (before a regrow).</summary>
    public void Clear()
    {
        foreach (var t in leafVisuals) if (t != null) Object.Destroy(t.gameObject);
        leafVisuals.Clear();
        mesh.Clear();
    }

    public void RebuildMesh(PlantSimulation sim)
    {
        verts.Clear(); norms.Clear(); uvs.Clear(); tris.Clear();
        foreach (Branch b in sim.Branches)
        {
            pts.Clear(); radii.Clear();
            pts.AddRange(b.nodes);
            radii.AddRange(b.nodeRadius);
            //all completed nodes and the still growing tip position (radius 0: brand-new tissue -> pointy)
            //right after segment was committed, the if avoids zero-vector problems
            if (b.tipSegLen > 1e-4f) { pts.Add(b.TipPosition); radii.Add(0f); }
            TubeMesh.Append(pts, radii, sim.Settings.radialSegments, arc, verts, norms, uvs, tris);
        }
        mesh.Clear();//before setting new data, in case the plant was reset or has fewer vertices now
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);//also recalculates the bounds (calculateBounds defaults to true)
    }

    public void UpdateLeaves(PlantSimulation sim)
    {
        PlantSettings s = sim.Settings;
        //create visuals for leaves spawned since the last frame
        for (int i = leafVisuals.Count; i < sim.Leaves.Count; i++)
        {
            Leaf leaf = sim.Leaves[i];
            GameObject prefab = leaf.variant >= 0 && s.leafPrefabs != null && leaf.variant < s.leafPrefabs.Length
                ? s.leafPrefabs[leaf.variant] : null;
            Transform t = null;
            if (prefab != null)//a slot in the array can still be empty
            {
                //child of the plant, so localPosition/localRotation are in the same space as the nodes
                t = Object.Instantiate(prefab, root).transform;
                t.localScale = Vector3.zero;//starting off invisible ensures it doesn't flicker up in full size before rescaled
            }
            leafVisuals.Add(t);
        }

        float elev = s.leafElevationDeg * Mathf.Deg2Rad;
        for (int i = 0; i < leafVisuals.Count; i++)
        {
            Transform t = leafVisuals[i];
            if (t == null) continue;
            Leaf leaf = sim.Leaves[i];
            //leaf base sits on its branch's surface; it moves along as the segment elongates
            //and outward as the branch thickens
            t.localPosition = sim.LeafCenter(leaf) + leaf.outward * sim.LeafBranchRadius(leaf);

            //blade direction: outward, tilted up toward the branch direction by the elevation angle
            Vector3 blade = Mathf.Cos(elev) * leaf.outward + Mathf.Sin(elev) * leaf.tangent;
            Vector3 side = Vector3.Cross(leaf.tangent, leaf.outward);
            Vector3 up = Vector3.Cross(blade, side);//upper leaf surface, facing roughly along the branch
            t.localRotation = Quaternion.LookRotation(blade, up);

            //the only place where growth turns into visuals -> replace this line for the morphing leaf later
            t.localScale = Vector3.one * (sim.LeafTargetSize(leaf) * sim.LeafGrowth(leaf));
        }
    }

    //meshes created with new Mesh() live on Unity's native side and aren't garbage collected
    public void Dispose()
    {
        if (mesh != null) Object.Destroy(mesh);
    }
}
