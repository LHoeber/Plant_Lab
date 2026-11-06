using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the plant's cactus segments: one child object per segment (body mesh + its tufts in a second child), placed and
/// scaled from the simulation every frame. A segment's mesh is only rebuilt when its growth or wither changed visibly,
/// and only within a time budget per frame (rebuilding is the expensive part). Reads the simulation, never changes it.
/// </summary>
public class CactusVisuals
{
    readonly Transform root;
    readonly List<Entry> entries = new List<Entry>();//same order as PlantSimulation.CactusSegments

    class Entry
    {
        public Transform t;
        public MeshFilter body, tufts;
        public MeshRenderer bodyRenderer, tuftRenderer;
        public Mesh bodyMesh, tuftMesh;
        public float shownGrowth = -1f, shownWither = -1f;
        public int shownOccupied = -1, shownVersion = -1;
        public MaterialPropertyBlock block;
    }

    /// <summary>
    /// Growth / wither change that makes a segment's mesh be rebuilt. In between, the old mesh is stretched to the current
    /// state (see Update), so this only sets how often the details (column length, new areoles and tufts) catch up.
    /// </summary>
    const float RebuildStep = 0.01f;
    /// <summary>
    /// Milliseconds per frame for rebuilding segment meshes (the most outdated first; at least one per frame). The rest
    /// waits for the next frame: with many segments growing at once they update less smoothly instead of the frame rate dropping.
    /// </summary>
    const float RebuildBudgetMs = 4f;

    readonly CactusSegmentSettings.TuftBuilder tufts = new CactusSegmentSettings.TuftBuilder();
    readonly List<int> skip = new List<int>();
    readonly List<int> outdated = new List<int>();

    public CactusVisuals(Transform root) { this.root = root; }

    public void Update(PlantSimulation sim)
    {
        CactusSegmentSettings type = sim.Settings.cactusSegment;
        if (type == null) return;
        for (int i = entries.Count; i < sim.CactusSegments.Count; i++) entries.Add(Create(i));

        outdated.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            CactusSegment c = sim.CactusSegments[i];
            Entry e = entries[i];
            e.t.localPosition = c.position;
            e.t.localRotation = c.rotation;
            float g = c.ShownGrowth, w = sim.SegmentWither(c);
            //between two rebuilds the mesh is stretched to the current state, so growth looks smooth every frame
            Vector2 stretch = e.shownGrowth >= 0f ? type.shape.StretchBetween(e.shownGrowth, e.shownWither, g, w) : Vector2.one;
            e.t.localScale = new Vector3(stretch.x, stretch.y, stretch.x) * c.size;
            bool changed = e.shownVersion != type.Version || e.shownOccupied != c.occupied.Count
                           || (g >= 1f && e.shownGrowth < 1f) || (w >= 1f && e.shownWither < 1f);//the last step always shows
            if (changed || Mathf.Abs(g - e.shownGrowth) >= RebuildStep || Mathf.Abs(w - e.shownWither) >= RebuildStep) outdated.Add(i);
        }
        //never-built ones first, then the most outdated
        outdated.Sort((a, b) => Outdatedness(sim, b).CompareTo(Outdatedness(sim, a)));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < outdated.Count && (k == 0 || clock.Elapsed.TotalMilliseconds < RebuildBudgetMs); k++)
            Rebuild(sim, type, outdated[k]);
    }

    float Outdatedness(PlantSimulation sim, int i)
    {
        Entry e = entries[i];
        if (e.shownGrowth < 0f) return float.PositiveInfinity;
        CactusSegment c = sim.CactusSegments[i];
        return Mathf.Abs(c.ShownGrowth - e.shownGrowth) + Mathf.Abs(sim.SegmentWither(c) - e.shownWither) + 0.001f;
    }

    Entry Create(int i)
    {
        var go = new GameObject($"Cactus segment {i}", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        var e = new Entry { t = go.transform, block = new MaterialPropertyBlock() };
        e.body = go.GetComponent<MeshFilter>();
        e.bodyRenderer = go.GetComponent<MeshRenderer>();
        (e.tufts, e.tuftRenderer) = CactusSegmentSettings.GeneratedChild(go.transform, "Tufts");
        e.bodyMesh = new Mesh { name = "Cactus segment" };
        e.bodyMesh.MarkDynamic();
        e.tuftMesh = new Mesh { name = "Cactus tufts", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        e.body.sharedMesh = e.bodyMesh;
        e.tufts.sharedMesh = e.tuftMesh;
        return e;
    }

    void Rebuild(PlantSimulation sim, CactusSegmentSettings type, int i)
    {
        CactusSegment c = sim.CactusSegments[i];
        Entry e = entries[i];
        float g = c.ShownGrowth, w = sim.SegmentWither(c);
        type.shape.BuildMesh(e.bodyMesh, type.rings, type.columns, g, w);
        Material body = type.BodyMaterial;
        e.bodyRenderer.sharedMaterial = body;
        //withering: the body's color moves toward the withered color, like stems and leaves
        Color fresh = body.HasProperty("_BaseColor") ? body.GetColor("_BaseColor") : Color.white;
        e.block.SetColor("_BaseColor", sim.Settings.WitherColor(fresh, w));
        e.bodyRenderer.SetPropertyBlock(e.block);

        //tufts: all areoles that exist now, except where a child or a flower grows out of
        List<CactusShape.SurfacePoint> areoles = type.shape.Areoles(c.seed, g, w);
        skip.Clear();
        for (int k = 0; k < areoles.Count; k++)
            if (c.occupied.Contains(CactusSegment.Key(areoles[k].rib, areoles[k].index))) skip.Add(k);
        tufts.Clear();
        type.AddTufts(tufts, areoles, Matrix4x4.identity, c.seed, skip, type.shape.BallScale(g));
        tufts.Build(e.tuftMesh);
        e.tuftRenderer.sharedMaterials = type.TuftMaterials();

        e.shownGrowth = g;
        e.shownWither = w;
        e.t.localScale = Vector3.one * c.size;//the new mesh is the current state: no stretch
        e.shownOccupied = c.occupied.Count;
        e.shownVersion = type.Version;
    }

    /// <summary>Removes all segment objects (before a regrow).</summary>
    public void Clear()
    {
        foreach (Entry e in entries)
        {
            if (e.t != null) Object.Destroy(e.t.gameObject);
            Object.Destroy(e.bodyMesh);
            Object.Destroy(e.tuftMesh);
        }
        entries.Clear();
        tufts.Release();
    }
}
