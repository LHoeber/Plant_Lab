using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The whole plant as pure simulation: branches, leaves, sim time, spawn decisions.
/// Plain C# class (no MonoBehaviour, no GameObjects), so it could also run without anything
/// being drawn, e.g. to simulate many plants offline.
/// Same settings + same seed + same light target => same plant.
/// </summary>
public class PlantSimulation
{
    public const float GoldenAngleDeg = 137.50776f;// 360 * (1 - 1/phi)

    public readonly PlantSettings Settings;
    public readonly Branch Root;                                  //the main stem
    public readonly List<Branch> Branches = new List<Branch>();   //all branches, in creation order (parents before children)
    public readonly List<Leaf> Leaves = new List<Leaf>();         //all leaves, in creation order
    public float SimTime { get; private set; }                   //sim seconds since the start

    /// <summary>Growth target in plant-local space; null = straight up. Set from outside (e.g. a light source).</summary>
    public Vector3? LightTarget;

    public PlantSimulation(PlantSettings settings, int seed)
    {
        Settings = settings;
        //main stem: starts at the origin, growing up; any vector perpendicular to up works as reference axis
        Root = new Branch(seed, 0, null, 0, 0f, 0f, Vector3.zero, Vector3.up, Vector3.forward, 0f);
        Root.nextSpawnCheck = Settings.spawnStartFraction * MaxFreshLengthOf(Root);
        Branches.Add(Root);
    }

    // --- growth formulas ---

    /// <summary>Current stretch of a segment of the given age: 1 when committed, approaching segmentStretchFactor.</summary>
    public float StretchScale(float age) =>
        1f + (Settings.segmentStretchFactor - 1f) * (1f - Mathf.Exp(-age / Settings.elongationTime));

    /// <summary>Distance from the plant base, fully elongated, of a point at a fresh distance from a branch's base.</summary>
    public float FinalDistance(Branch b, float arcFresh) => BaseFinalDistance(b) + arcFresh * Settings.segmentStretchFactor;

    /// <summary>Distance from the plant base, fully elongated, of a branch's base.</summary>
    public float BaseFinalDistance(Branch b) => b.parent == null ? 0f : FinalDistance(b.parent, b.attachArcFresh);

    /// <summary>Current radius of a point: approaches its maximum radius, fast at first, then slowing down.</summary>
    public float RadiusAt(Branch b, float arcFresh, float age) =>
        Settings.MaxRadiusAt(FinalDistance(b, arcFresh), b.depth) * (1f - Mathf.Exp(-age / Settings.radialGrowthTime));

    /// <summary>Maximum length of a branch, fully elongated (from the current settings, so sliders act immediately).</summary>
    public float MaxLengthOf(Branch b) => b.depth == 0 ? Settings.maxLength : Settings.SideBranchLength(BaseFinalDistance(b));

    /// <summary>How much fresh tissue a branch forms in total, so that it reaches its maximum length once fully elongated.</summary>
    public float MaxFreshLengthOf(Branch b) => MaxLengthOf(b) / Settings.segmentStretchFactor;

    /// <summary>One fixed simulation step. Returns true, because elongation and thickening change the shape every step.</summary>
    public bool Step(float dt)
    {
        SimTime += dt;//leaves keep aging and segments keep elongating even after all tips stopped
        float dL = Settings.growthSpeed * dt;
        //only branches that existed at the start of the step grow in it;
        //branches spawned during this step start growing in the next one
        int n = Branches.Count;
        for (int i = 0; i < n; i++)
            Branches[i].Grow(dL, MaxFreshLengthOf(Branches[i]), this);
        //positions and radii for the new sim time; creation order = parents before children
        foreach (Branch b in Branches) b.UpdateGeometry(this);
        return true;
    }

    /// <summary>Called by a branch when its tip passes a spawn check position (fresh distance from its base).</summary>
    public void SpawnCheck(Branch branch, float arcFresh)
    {
        PlantSettings s = Settings;
        System.Random r = branch.spawnRng;
        //always the same draws per check, whatever the outcome: then raising spawnProbability only adds
        //spawns without moving the others, and branchProbability only changes what spawns, not where
        double uSpawn = r.NextDouble();
        double uKind = r.NextDouble();
        double uVariant = r.NextDouble();
        float angleNoise = Gaussian(r);
        float sizeNoise = Gaussian(r);

        if (uSpawn >= s.spawnProbability) return;

        //golden angle between successive leaves/branches on this branch, plus noise
        branch.phyllotaxisAngle += GoldenAngleDeg + angleNoise * s.phyllotaxisNoiseDeg;
        Vector3 outward = branch.OutwardAt(branch.phyllotaxisAngle);
        //the check lies on the tip segment; attachments are stored as (segment, fresh offset in it)
        int seg = branch.SegmentCount;
        float offset = arcFresh - branch.committedLength;

        bool branchAllowed = uKind < s.branchProbability
                             && branch.depth < s.maxDepth
                             && arcFresh >= s.branchStartFraction * MaxFreshLengthOf(branch)
                             && s.SideBranchLength(FinalDistance(branch, arcFresh)) >= s.minBranchLength;
        if (branchAllowed)
            SpawnBranch(branch, seg, offset, arcFresh, outward);
        else
            SpawnLeaf(branch, seg, offset, arcFresh, outward, sizeNoise, uVariant);
    }

    void SpawnBranch(Branch parent, int seg, float offset, float arcFresh, Vector3 outward)
    {
        float elev = Settings.branchElevationDeg * Mathf.Deg2Rad;
        //initial direction: outward, tilted toward the parent's direction by the elevation angle
        Vector3 dir = Mathf.Cos(elev) * outward + Mathf.Sin(elev) * parent.tipDir;
        //perpendicular to both parent direction and outward -> always perpendicular to dir as well
        Vector3 normal = Vector3.Cross(parent.tipDir, outward).normalized;
        int childSeed = DeriveSeed(parent.seed, parent.children.Count);
        var child = new Branch(childSeed, parent.depth + 1, parent, seg, offset, arcFresh,
                               parent.PointAt(seg, offset), dir, normal, SimTime);
        child.nextSpawnCheck = Settings.spawnStartFraction * MaxFreshLengthOf(child);
        parent.children.Add(child);
        Branches.Add(child);
    }

    void SpawnLeaf(Branch branch, int seg, float offset, float arcFresh, Vector3 outward,
                   float sizeNoise, double uVariant)
    {
        int count = Settings.leafPrefabs != null ? Settings.leafPrefabs.Length : 0;
        var leaf = new Leaf
        {
            branch = branch,
            seg = seg,
            offset = offset,
            arcFresh = arcFresh,
            tangent = branch.tipDir,
            outward = outward,
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.leafSizeNoise),
            //uniform choice out of all provided prefabs, u in [0,1) -> index 0..count-1
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
        };
        branch.leaves.Add(leaf);
        Leaves.Add(leaf);
    }

    public Vector3 NextDirection(Branch b)
    {
        PlantSettings s = Settings;
        Vector3 toTarget = Vector3.up;
        if (LightTarget.HasValue)
        {
            Vector3 d = LightTarget.Value - b.LastNode;
            //prevent normalization if branch reached almost exact light position
            if (d.sqrMagnitude > 1e-6f) toTarget = d.normalized;
        }
        //spherical interpolation toward the light. The per-segment fraction 1 - exp(-k*L) compounds to
        //exp(-k*distance) over any distance, so the bending per unit length doesn't depend on segmentLength.
        //k gets smaller with every branching level
        float turnFraction = 1f - Mathf.Exp(-s.DirectionBiasAt(b.depth) * s.segmentLength);
        Vector3 biased = Vector3.Slerp(b.tipDir, toTarget, turnFraction);
        //variances of independent steps add up, so std per segment scales with sqrt(segmentLength)
        //total variance is independent from L this way, because: (D/L)*sigma^2*sqrt(L)^2 = D*sigma^2
        float segmentStd = s.perturbationStd * Mathf.Sqrt(s.segmentLength);
        System.Random r = b.dirRng;
        Vector3 noise = new Vector3(Gaussian(r), Gaussian(r), Gaussian(r)) * segmentStd;
        Vector3 result = biased + noise;
        return result.sqrMagnitude > 1e-6f ? result.normalized : b.tipDir;//check if noise canceled out the direction
    }

    // --- leaves (used by the visuals, but it's simulation state, so it lives here) ---

    /// <summary>Current position of a leaf's attachment point on its branch's center line.</summary>
    public Vector3 LeafCenter(Leaf leaf) => leaf.branch.PointAt(leaf.seg, leaf.offset);

    /// <summary>Current radius of the branch where the leaf is attached.</summary>
    public float LeafBranchRadius(Leaf leaf) => leaf.branch.RadiusAt(leaf.seg, leaf.offset);

    /// <summary>Growth progress of a leaf, 0 at birth to 1 when fully grown.</summary>
    public float LeafGrowth(Leaf leaf)
    {
        float t = Mathf.Clamp01((SimTime - leaf.birthTime) / Settings.leafGrowthDuration);
        return Mathf.Pow(t, Settings.leafGrowthExponent);
    }

    /// <summary>Full-grown size of a leaf: linear decrease with distance from the plant base, times its random factor.</summary>
    public float LeafTargetSize(Leaf leaf)
    {
        float d = FinalDistance(leaf.branch, leaf.arcFresh);
        float lengthFactor = Mathf.Max(Settings.minLeafSizeFraction, 1f - Settings.leafSizeFalloff * d);
        return Settings.maxLeafSize * lengthFactor * leaf.sizeFactor;
    }

    // --- random helpers ---

    /// <summary>Standard normal sample via Box-Muller, from the given stream.</summary>
    /// necessary, because no inherent function is available in c# to draw standard normal values
    public static float Gaussian(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble(); // (0,1] to avoid log(0)
        double u2 = r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }

    /// <summary>Seed for the n-th child of a branch: deterministic, but different for every child.</summary>
    static int DeriveSeed(int parentSeed, int childIndex)
    {
        unchecked
        {
            int h = parentSeed * 16777619 ^ (childIndex + 1) * 486187739;
            h ^= h >> 15;
            return h * 73244475;
        }
    }
}
