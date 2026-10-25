using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The plant as an obstacle for its own leaves and flowers (plant-local space, plain math, no Unity physics).
///   - leaves and flowers keep out of stems (except the stem they grow on)
///   - leaves give way to flowers, and a flower to the flowers that appeared before it
/// Stems never give way to organs, and stems don't avoid each other.
///
/// Rebuilt every step: all stem segments and flowers are sorted into a grid of cubes, so an organ only checks what's
/// in the cubes around it. Every stem segment is treated as a box around its capsule, every organ as its voxel boxes;
/// two boxes overlap if no axis separates them (separating axis test), which also gives the shortest way out.
/// </summary>
public class PlantSelfCollision
{
    const float Cell = 0.1f;//grid cube edge length (plant-local units); about the size of a leaf

    //stem segments of this step: box around the capsule
    readonly List<Vector3> segCenter = new List<Vector3>();
    readonly List<Quaternion> segRot = new List<Quaternion>();
    readonly List<Vector3> segHalf = new List<Vector3>();
    readonly List<Vector3> segA = new List<Vector3>(), segB = new List<Vector3>();
    readonly List<float> segRadius = new List<float>();

    //flowers of this step: their current boxes, pose and size
    readonly List<int> flowerIndex = new List<int>();
    readonly List<OrganBox[]> flowerBoxes = new List<OrganBox[]>();
    readonly List<Vector3> flowerPos = new List<Vector3>(), flowerSphere = new List<Vector3>();
    readonly List<OrganBox> flowerHull = new List<OrganBox>();
    readonly List<Quaternion> flowerRot = new List<Quaternion>();
    readonly List<float> flowerScale = new List<float>(), flowerRadius = new List<float>();

    readonly Dictionary<long, List<int>> segGrid = new Dictionary<long, List<int>>();
    readonly Dictionary<long, List<int>> flowerGrid = new Dictionary<long, List<int>>();
    readonly Stack<List<int>> spare = new Stack<List<int>>();
    int[] seenSeg = new int[0], seenFlower = new int[0];
    int stamp;

    /// <summary>Collects all stem segments and flowers with their current positions and sorts them into the grid.</summary>
    public void Rebuild(PlantSimulation sim)
    {
        Recycle(segGrid);
        Recycle(flowerGrid);
        segCenter.Clear(); segRot.Clear(); segHalf.Clear(); segA.Clear(); segB.Clear(); segRadius.Clear();
        foreach (Branch b in sim.Branches)
            for (int k = 0; k < b.SegmentCount; k++)
            {
                Vector3 a = b.nodes[k], c = b.nodes[k + 1];
                float r = Mathf.Max(b.nodeRadius[k], b.nodeRadius[k + 1]);
                Vector3 d = c - a;
                float len = d.magnitude;
                if (r < 1e-4f || len < 1e-6f) continue;
                int id = segCenter.Count;
                segA.Add(a); segB.Add(c); segRadius.Add(r);
                segCenter.Add((a + c) * 0.5f);
                segRot.Add(Quaternion.LookRotation(d / len, Mathf.Abs(d.y) < 0.99f * len ? Vector3.up : Vector3.right));
                segHalf.Add(new Vector3(r, r, 0.5f * len));
                Insert(segGrid, id, Vector3.Min(a, c) - Vector3.one * r, Vector3.Max(a, c) + Vector3.one * r);
            }

        flowerIndex.Clear(); flowerBoxes.Clear(); flowerPos.Clear(); flowerSphere.Clear(); flowerRot.Clear(); flowerHull.Clear();
        flowerScale.Clear(); flowerRadius.Clear();
        if (sim.FlowerShapes != null)
            for (int i = 0; i < sim.Flowers.Count; i++)
            {
                Flower f = sim.Flowers[i];
                if (sim.FlowerWither(f) >= 1f || !sim.FlowerShape(f, out OrganCompound shape, out OrganBox[] boxes, out float scale)) continue;
                Vector3 pos = sim.FlowerPosition(f);
                Quaternion rot = sim.FlowerRotation(f);
                Vector3 center = pos + rot * (shape.boundsCenter * scale);
                float radius = shape.boundsRadius * scale;
                int id = flowerIndex.Count;
                flowerIndex.Add(i); flowerBoxes.Add(boxes); flowerPos.Add(pos); flowerRot.Add(rot); flowerHull.Add(shape.Hull);
                flowerScale.Add(scale); flowerSphere.Add(center); flowerRadius.Add(radius);
                Insert(flowerGrid, id, center - Vector3.one * radius, center + Vector3.one * radius);
            }
        if (seenSeg.Length < segCenter.Count) seenSeg = new int[segCenter.Count * 2];
        if (seenFlower.Length < flowerIndex.Count) seenFlower = new int[flowerIndex.Count * 2 + 1];
    }

    /// <summary>
    /// How far an organ (shape `shape`, boxes `boxes`, at pos/rot/scale) reaches into the plant's own stems and flowers,
    /// as the combined shortest way out (push) and the average contact point (at). Stems within `baseMargin` of the
    /// organ's base (the one it grows on) are ignored. Flowers are only obstacles if their index is below flowersBefore
    /// (leaves: all flowers; a flower: the ones that appeared before it).
    /// Three stages, each only for what passed the one before: grid cubes around the organ, then one box around the
    /// whole organ, then its detailed boxes.
    /// </summary>
    public bool Push(OrganCompound shape, OrganBox[] boxes, Vector3 pos, Quaternion rot, float scale, Vector3 basePos,
                     float baseMargin, int flowersBefore, float gap, out Vector3 push, out Vector3 at)
    {
        push = at = Vector3.zero;
        if (shape == null || boxes == null || scale < 1e-4f) return false;
        Vector3 sphereCenter = pos + rot * (shape.boundsCenter * scale);
        float sphereRadius = shape.boundsRadius * scale;
        OrganBox hull = shape.Hull;
        Vector3 hullCenter = pos + rot * (hull.center * scale), hullHalf = hull.half * scale + Vector3.one * gap;
        int n = 0;
        stamp++;
        Vector3 lo = sphereCenter - Vector3.one * (sphereRadius + gap), hi = sphereCenter + Vector3.one * (sphereRadius + gap);

        //stems
        Candidates(segGrid, lo, hi, found);
        foreach (int s in found)
        {
            if (seenSeg[s] == stamp) continue;
            seenSeg[s] = stamp;
            float r = segRadius[s];
            Vector3 onSeg = ClosestOnSegment(segA[s], segB[s], sphereCenter);
            if ((onSeg - sphereCenter).sqrMagnitude > Sq(sphereRadius + r + gap)) continue;//not even near
            //the stem the organ grows on (it touches the organ's base): ignore
            if ((ClosestOnSegment(segA[s], segB[s], basePos) - basePos).sqrMagnitude < Sq(r + baseMargin)) continue;
            if (!ObbPenetration(hullCenter, rot, hullHalf, segCenter[s], segRot[s], segHalf[s], out _)) continue;//whole organ clear
            foreach (OrganBox box in boxes)
            {
                Vector3 c = pos + rot * (box.center * scale);
                if (ObbPenetration(c, rot, box.half * scale + Vector3.one * gap, segCenter[s], segRot[s], segHalf[s], out Vector3 v))
                    Add(ref push, ref at, ref n, v, c);
            }
        }

        //flowers
        Candidates(flowerGrid, lo, hi, found);
        foreach (int f in found)
        {
            if (seenFlower[f] == stamp || flowerIndex[f] >= flowersBefore) continue;
            seenFlower[f] = stamp;
            if ((flowerSphere[f] - sphereCenter).sqrMagnitude > Sq(sphereRadius + flowerRadius[f] + gap)) continue;
            float fs = flowerScale[f];
            OrganBox oh0 = flowerHull[f];
            if (!ObbPenetration(hullCenter, rot, hullHalf, flowerPos[f] + flowerRot[f] * (oh0.center * fs), flowerRot[f], oh0.half * fs, out _))
                continue;//the two organs' boxes don't overlap: no detailed test needed
            foreach (OrganBox other in flowerBoxes[f])
            {
                Vector3 oc = flowerPos[f] + flowerRot[f] * (other.center * fs);
                Vector3 oh = other.half * fs;
                foreach (OrganBox box in boxes)
                {
                    Vector3 c = pos + rot * (box.center * scale);
                    if (ObbPenetration(c, rot, box.half * scale + Vector3.one * gap, oc, flowerRot[f], oh, out Vector3 v))
                        Add(ref push, ref at, ref n, v, c);
                }
            }
        }
        if (n == 0) return false;
        at /= n;
        return true;
    }

    /// <summary>
    /// Adds a box's way out to the combined push without counting the same direction twice
    /// (only what's still missing in this direction is added), and its position to the contact point.
    /// </summary>
    static void Add(ref Vector3 push, ref Vector3 at, ref int n, Vector3 v, Vector3 c)
    {
        float d = v.magnitude;
        if (d < 1e-7f) return;
        Vector3 dir = v / d;
        float already = Vector3.Dot(push, dir);
        if (already < d) push += dir * (d - already);
        at += c;
        n++;
    }

    // --- grid ---

    static long Key(int x, int y, int z) =>
        ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

    static int CellOf(float v) => Mathf.FloorToInt(v / Cell);

    void Insert(Dictionary<long, List<int>> grid, int id, Vector3 lo, Vector3 hi)
    {
        int x0 = CellOf(lo.x), x1 = CellOf(hi.x), y0 = CellOf(lo.y), y1 = CellOf(hi.y), z0 = CellOf(lo.z), z1 = CellOf(hi.z);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = Key(x, y, z);
                    if (!grid.TryGetValue(key, out List<int> list))
                        grid[key] = list = spare.Count > 0 ? spare.Pop() : new List<int>();
                    list.Add(id);
                }
    }

    /// <summary>Everything in the grid cubes overlapping the box lo..hi (may contain the same id more than once).</summary>
    static void Candidates(Dictionary<long, List<int>> grid, Vector3 lo, Vector3 hi, List<int> result)
    {
        result.Clear();
        int x0 = CellOf(lo.x), x1 = CellOf(hi.x), y0 = CellOf(lo.y), y1 = CellOf(hi.y), z0 = CellOf(lo.z), z1 = CellOf(hi.z);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (grid.TryGetValue(Key(x, y, z), out List<int> list)) result.AddRange(list);
    }
    readonly List<int> found = new List<int>();

    void Recycle(Dictionary<long, List<int>> grid)
    {
        foreach (List<int> list in grid.Values) { list.Clear(); spare.Push(list); }
        grid.Clear();
    }

    // --- geometry ---

    static float Sq(float v) => v * v;

    static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude);
        return a + ab * Mathf.Clamp01(t);
    }

    static readonly Vector3[] axes = new Vector3[15], aa = new Vector3[3], bb = new Vector3[3];

    /// <summary>
    /// Do two rotated boxes overlap? If so, the shortest move of box A that separates them (separating axis test:
    /// the 3 face directions of each box and the 9 cross products of their edges).
    /// </summary>
    public static bool ObbPenetration(Vector3 ca, Quaternion ra, Vector3 ha, Vector3 cb, Quaternion rb, Vector3 hb, out Vector3 push)
    {
        push = Vector3.zero;
        Vector3 a0 = ra * Vector3.right, a1 = ra * Vector3.up, a2 = ra * Vector3.forward;
        Vector3 b0 = rb * Vector3.right, b1 = rb * Vector3.up, b2 = rb * Vector3.forward;
        axes[0] = a0; axes[1] = a1; axes[2] = a2; axes[3] = b0; axes[4] = b1; axes[5] = b2;
        int n = 6;
        aa[0] = a0; aa[1] = a1; aa[2] = a2; bb[0] = b0; bb[1] = b1; bb[2] = b2;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                Vector3 c = Vector3.Cross(aa[i], bb[j]);
                if (c.sqrMagnitude > 1e-6f) axes[n++] = c.normalized;//parallel edges give no new axis
            }
        Vector3 d = cb - ca;
        float best = float.PositiveInfinity;
        Vector3 bestAxis = Vector3.zero;
        for (int k = 0; k < n; k++)
        {
            Vector3 L = axes[k];
            float pa = ha.x * Mathf.Abs(Vector3.Dot(a0, L)) + ha.y * Mathf.Abs(Vector3.Dot(a1, L)) + ha.z * Mathf.Abs(Vector3.Dot(a2, L));
            float pb = hb.x * Mathf.Abs(Vector3.Dot(b0, L)) + hb.y * Mathf.Abs(Vector3.Dot(b1, L)) + hb.z * Mathf.Abs(Vector3.Dot(b2, L));
            float dist = Vector3.Dot(d, L);
            float overlap = pa + pb - Mathf.Abs(dist);
            if (overlap <= 0f) return false;//this axis separates them
            if (overlap < best) { best = overlap; bestAxis = dist > 0f ? -L : L; }//away from B
        }
        push = bestAxis * best;
        return true;
    }
}
