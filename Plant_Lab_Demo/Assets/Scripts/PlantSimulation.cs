using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The whole plant as pure simulation: branches, leaves, sim time, spawn decisions.
/// Plain C# class (no MonoBehaviour, no GameObjects), so it could also run without anything
/// being drawn, e.g. to simulate many plants offline.
/// Same settings + same seed + same light target => same plant.
/// </summary>
/// <summary>Global life phases of a plant, always in this order (Flowering and Fruiting can be skipped).</summary>
public enum PlantPhase { Growing, Flowering, Fruiting, Withering, Dead }

public class PlantSimulation
{
    public const float GoldenAngleDeg = 137.50776f;// 360 * (1 - 1/phi)

    public readonly PlantSettings Settings;
    public readonly Branch Root;                                  //the main stem
    public readonly List<Branch> Branches = new List<Branch>();   //all branches, in creation order (parents before children)
    public readonly List<Leaf> Leaves = new List<Leaf>();         //all leaves, in creation order
    public readonly List<Flower> Flowers = new List<Flower>();    //all flowers, in creation order
    public float SimTime { get; private set; }                   //sim seconds since the start
    public PlantPhase Phase { get; private set; } = PlantPhase.Growing;
    public float PhaseStartTime { get; private set; }            //sim time the current phase began
    public float FloweringStartTime { get; private set; } = float.PositiveInfinity; //buds open from here on
    public float FruitingStartTime { get; private set; } = float.PositiveInfinity;  //fruits develop from here on
    public float WitherStartTime { get; private set; } = float.PositiveInfinity;    //the withering wave starts here
    float witherMaxDistance = 1f;                                 //distance from the base of the farthest organ (wave start)

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

    /// <summary>Current radius of a point: approaches its target radius, fast at first, then slowing down.</summary>
    public float RadiusAt(Branch b, float arcFresh, float age) =>
        TargetRadius(b, arcFresh) * (1f - Mathf.Exp(-age / Settings.radialGrowthTime))
        * Mathf.Lerp(1f, Settings.stemWitheredRadiusFraction, StemWither(b, arcFresh));//thinner while withering

    /// <summary>
    /// Radius a point eventually reaches: maximum radius from distance to base and branching level,
    /// narrowed smoothly toward the branch's end, and never thicker than the parent where the branch is attached.
    /// </summary>
    public float TargetRadius(Branch b, float arcFresh)
    {
        PlantSettings s = Settings;
        float remaining = Mathf.Max(0f, MaxLengthOf(b) - arcFresh * s.segmentStretchFactor);//final length left to the end
        float f = s.endRadiusFraction;
        float endTaper = f + (1f - f) * (1f - Mathf.Exp(-remaining / s.endTaperLength));
        float r = s.MaxRadiusAt(FinalDistance(b, arcFresh), b.depth) * endTaper;
        return b.parent == null ? r : Mathf.Min(r, TargetRadius(b.parent, b.attachArcFresh));
    }

    /// <summary>Maximum length of a branch, fully elongated (from the current settings, so sliders act immediately).</summary>
    public float MaxLengthOf(Branch b) => b.depth == 0 ? Settings.maxLength : Settings.SideBranchLength(BaseFinalDistance(b));

    /// <summary>How much fresh tissue a branch forms in total, so that it reaches its maximum length once fully elongated.</summary>
    public float MaxFreshLengthOf(Branch b) => MaxLengthOf(b) / Settings.segmentStretchFactor;

    /// <summary>One fixed simulation step. Returns true, because elongation and thickening change the shape every step.</summary>
    public bool Step(float dt)
    {
        SimTime += dt;//leaves keep aging and segments keep elongating even after all tips stopped
        if (Phase == PlantPhase.Growing)
        {
            //new tissue is only formed in the Growing phase
            float dL = Settings.growthSpeed * dt;
            //only branches that existed at the start of the step grow in it;
            //branches spawned during this step start growing in the next one
            int n = Branches.Count;
            for (int i = 0; i < n; i++)
                Branches[i].Grow(dL, MaxFreshLengthOf(Branches[i]), this);
        }
        //positions and radii for the new sim time; creation order = parents before children
        foreach (Branch b in Branches) b.UpdateGeometry(this);
        //finished tips whose bud delay has passed get their (one) flower check;
        //no new buds after Flowering (they'd have no chance to open and set fruit)
        if (Phase <= PlantPhase.Flowering)
            foreach (Branch b in Branches)
                if (b.finishTime >= 0f && !b.flowerDecided && SimTime - b.finishTime >= Settings.flowerDelay)
                    FlowerCheck(b);
        UpdatePhase();
        return true;
    }

    // --- phases ---

    /// <summary>Moves on to the next phase when the current one is over.</summary>
    void UpdatePhase()
    {
        PlantSettings s = Settings;
        float inPhase = SimTime - PhaseStartTime;
        switch (Phase)
        {
            case PlantPhase.Growing:
                bool allFinished = true;
                foreach (Branch b in Branches) if (b.finishTime < 0f && !b.stopped) { allFinished = false; break; }
                if (allFinished || inPhase >= s.growingMaxDuration) EnterNextPhase();
                break;
            case PlantPhase.Flowering:
                if (inPhase >= s.floweringDuration) EnterNextPhase();
                break;
            case PlantPhase.Fruiting:
                if (inPhase >= s.fruitingDuration) EnterNextPhase();
                break;
            case PlantPhase.Withering:
                //over once the wave has reached the base and the last organs/stems there have withered
                float longest = Mathf.Max(s.leafWitherTime, s.flowerWitherTime, s.stemWitherTime);
                if (inPhase >= s.witherWaveDuration + longest + 3f * s.witherJitter) EnterPhase(PlantPhase.Dead);
                break;
        }
    }

    /// <summary>Ends the current phase and starts the next enabled one (also usable from outside to skip ahead, e.g. for testing).</summary>
    public void EnterNextPhase()
    {
        PlantPhase next = Phase;
        do next = next + 1;
        while ((next == PlantPhase.Flowering && !Settings.enableFlowering) ||
               (next == PlantPhase.Fruiting && !Settings.enableFruiting));
        if (next > PlantPhase.Dead) return;
        EnterPhase(next);
    }

    void EnterPhase(PlantPhase next)
    {
        //leaving Growing early: unfinished branches stop where they are
        if (Phase == PlantPhase.Growing)
            foreach (Branch b in Branches) b.StopGrowth(this);
        Phase = next;
        PhaseStartTime = SimTime;
        if (next == PlantPhase.Flowering) FloweringStartTime = SimTime;
        if (next == PlantPhase.Fruiting) StartFruiting();
        if (next == PlantPhase.Withering) StartWithering();
    }

    /// <summary>Decides for every flower whether it develops into a fruit; the others start withering now.</summary>
    void StartFruiting()
    {
        FruitingStartTime = SimTime;
        bool opened = FloweringStartTime < SimTime;//without a Flowering phase no flower has opened, so none sets fruit
        foreach (Flower f in Flowers)
        {
            //own stream per flower (from its branch's seed): fruit set never shifts any other random decision.
            //always the same draws, whatever the outcome
            var r = new System.Random(unchecked(f.branch.seed * 486187739 + 4));
            double uFruit = r.NextDouble();
            float jitter = Mathf.Abs(Gaussian(r)) * Settings.witherJitter;
            f.setsFruit = opened && uFruit < Settings.fruitSetProbability;
            if (!f.setsFruit) f.witherStart = Mathf.Min(f.witherStart, SimTime + jitter);//discolors and falls off
        }
    }

    /// <summary>Fixes when the withering wave reaches each leaf and flower: farthest from the base first.</summary>
    void StartWithering()
    {
        PlantSettings s = Settings;
        WitherStartTime = SimTime;
        witherMaxDistance = 1e-3f;
        foreach (Branch b in Branches) witherMaxDistance = Mathf.Max(witherMaxDistance, FinalDistance(b, b.committedLength));
        //own stream for the jitter, so withering never shifts any other random decision
        var r = new System.Random(unchecked(Root.seed * 486187739 + 3));
        //always the same draws per organ; an earlier wither start (e.g. flowers without fruit) is kept
        foreach (Leaf leaf in Leaves)
            leaf.witherStart = Mathf.Min(leaf.witherStart,
                WaveArrival(FinalDistance(leaf.branch, leaf.arcFresh)) + Mathf.Abs(Gaussian(r)) * s.witherJitter);
        foreach (Flower f in Flowers)
            f.witherStart = Mathf.Min(f.witherStart,
                WaveArrival(FinalDistance(f.branch, f.branch.committedLength)) + Mathf.Abs(Gaussian(r)) * s.witherJitter);
    }

    /// <summary>Sim time the withering wave reaches a point at the given distance from the base.</summary>
    float WaveArrival(float distFromBase) =>
        WitherStartTime + (1f - Mathf.Clamp01(distFromBase / witherMaxDistance)) * Settings.witherWaveDuration;

    /// <summary>How withered a point of stem is: 0 = fresh, 1 = fully withered (discolored and thinned).</summary>
    public float StemWither(Branch b, float arcFresh)
    {
        if (SimTime < WitherStartTime) return 0f;
        float start = WaveArrival(FinalDistance(b, arcFresh));
        return Mathf.Clamp01((SimTime - start) / Settings.stemWitherTime);
    }

    /// <summary>How withered a leaf is: 0 = fresh, 1 = fully withered. At 1 it falls off.</summary>
    public float LeafWither(Leaf leaf) => Mathf.Clamp01((SimTime - leaf.witherStart) / Settings.leafWitherTime);

    /// <summary>How withered a flower is: 0 = fresh, 1 = fully withered. At 1 it falls off.</summary>
    public float FlowerWither(Flower f) => Mathf.Clamp01((SimTime - f.witherStart) / Settings.flowerWitherTime);

    /// <summary>Decides once whether a finished branch gets a flower at its tip, and creates it.</summary>
    void FlowerCheck(Branch b)
    {
        b.flowerDecided = true;
        if (b.SegmentCount == 0) return;//nothing to sit on (branch with practically no length)
        //own stream per branch, separate from its spawn stream: flowers never shift leaf/branch decisions.
        //always the same draws, whatever the outcome
        var r = new System.Random(unchecked(b.seed * 486187739 + 2));
        double uFlower = r.NextDouble();
        double uRoll = r.NextDouble();
        double uVariant = r.NextDouble();
        float sizeNoise = Gaussian(r);
        if (uFlower >= Settings.flowerProbability) return;

        int count = Settings.flowerPrefabs != null ? Settings.flowerPrefabs.Length : 0;
        int last = b.SegmentCount - 1;
        var flower = new Flower
        {
            branch = b,
            seg = last,
            offset = b.segFreshLength[last],//end of the last segment = the branch's end point
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.flowerSizeNoise),
            rollDeg = (float)(uRoll * 360.0),
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
        };
        b.flower = flower;
        Flowers.Add(flower);
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

        //extra leaf probability toward the branch's end: rises from spawnProbability (start) to leafProbabilityAtEnd (end)
        float u = Mathf.Clamp01(arcFresh * s.segmentStretchFactor / Mathf.Max(1e-6f, MaxLengthOf(branch)));//position as fraction of final length
        float pLeafBoosted = Mathf.Lerp(s.spawnProbability, s.leafProbabilityAtEnd, Mathf.Pow(u, s.leafEndBoostExponent));
        bool normalSpawn = uSpawn < s.spawnProbability;               //leaf or branch, as before
        bool extraLeaf = !normalSpawn && uSpawn < pLeafBoosted;        //only possible toward the end, always a leaf
        if (!normalSpawn && !extraLeaf) return;

        //golden angle between successive leaves/branches on this branch, plus noise
        branch.phyllotaxisAngle += GoldenAngleDeg + angleNoise * s.phyllotaxisNoiseDeg;
        Vector3 outward = branch.OutwardAt(branch.phyllotaxisAngle);
        //the check lies on the tip segment; attachments are stored as (segment, fresh offset in it)
        int seg = branch.SegmentCount;
        float offset = arcFresh - branch.committedLength;

        bool branchAllowed = normalSpawn
                             && uKind < s.branchProbability
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

    // --- flowers ---

    /// <summary>Current position of a flower's base: the end point of its branch (moves as the branch elongates).</summary>
    public Vector3 FlowerPosition(Flower f) => f.branch.PointAt(f.seg, f.offset);

    /// <summary>Direction the flower faces: the direction its branch's last segment was growing.</summary>
    public Vector3 FlowerDirection(Flower f) => f.branch.segDir[f.seg];

    /// <summary>Opening progress of a flower, 0 = bud to 1 = fully open (input for FlowerMorph).</summary>
    public float FlowerGrowth(Flower f)
    {
        //closed until the Flowering phase starts (or until the bud appears, if that's later); never opens if Flowering is skipped
        float openStart = Mathf.Max(FloweringStartTime, f.birthTime);
        if (SimTime <= openStart) return 0f;
        float t = Mathf.Clamp01((SimTime - openStart) / Settings.flowerOpenDuration);
        return Mathf.Pow(t, Settings.flowerOpenExponent);
    }

    /// <summary>Fruit development of a flower: 0 = (still) a flower, 1 = ripe fruit. Always 0 for flowers that didn't set fruit.</summary>
    public float FruitProgress(Flower f)
    {
        if (!f.setsFruit || SimTime <= FruitingStartTime) return 0f;
        float t = Mathf.Clamp01((SimTime - FruitingStartTime) / Settings.fruitDevelopDuration);
        return Mathf.Pow(t, Settings.fruitDevelopExponent);
    }

    /// <summary>How far a new bud has emerged: 0 = just appeared (size 0), 1 = full closed-bud size.</summary>
    public float BudEmergence(Flower f) => Mathf.Clamp01((SimTime - f.birthTime) / Settings.budGrowDuration);

    /// <summary>Full size of a flower: maxFlowerSize times its random factor.</summary>
    public float FlowerTargetSize(Flower f) => Settings.maxFlowerSize * f.sizeFactor;

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
