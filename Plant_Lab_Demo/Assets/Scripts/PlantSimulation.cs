using System;
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

    /// <summary>What growing tips can run into; null = nothing. Set from outside (e.g. UnityObstacleField).</summary>
    public IObstacleField Obstacles;

    /// <summary>
    /// Collision shape of each leaf / flower variant (index = variant), built from the prefabs.
    /// Null = organs aren't checked against obstacles.
    /// </summary>
    public OrganCompound[] LeafShapes, FlowerShapes;

    /// <summary>What decides where branches, leaves and flowers appear (classic spawn logic or L-system rules).</summary>
    public readonly IPlantDevelopment Development;

    /// <summary>Called whenever a new phase starts (e.g. for logging).</summary>
    public event Action<PlantPhase> PhaseChanged;

    public PlantSimulation(PlantSettings settings, int seed)
    {
        Settings = settings;
        //main stem: starts at the origin, growing up; any vector perpendicular to up works as reference axis
        Root = new Branch(seed, 0, null, 0, 0f, 0f, Vector3.zero, Vector3.up, Vector3.forward, 0f);
        Branches.Add(Root);
        Development = settings.development == DevelopmentMode.LSystem
            ? new LSystemDevelopment(settings) : (IPlantDevelopment)new ClassicDevelopment();
        Development.Begin(this);
    }

    // --- growth formulas ---

    /// <summary>Distance from the plant base (along the plant) of a point at a distance arcPos from a branch's base.</summary>
    public float DistanceFromBase(Branch b, float arcPos) => BaseDistance(b) + arcPos;

    /// <summary>Distance from the plant base (along the plant) of a branch's base.</summary>
    public float BaseDistance(Branch b) => b.parent == null ? 0f : DistanceFromBase(b.parent, b.attachArc);

    /// <summary>Current radius of a point: approaches its target radius, fast at first, then slowing down.</summary>
    public float RadiusAt(Branch b, float arcPos, float age) =>
        TargetRadius(b, arcPos) * (1f - Mathf.Exp(-age / Settings.radialGrowthTime))
        * Mathf.Lerp(1f, Settings.stemWitheredRadiusFraction, StemWither(b, arcPos));//thinner while withering

    /// <summary>
    /// Radius a point eventually reaches: maximum radius from distance to base and branching level,
    /// narrowed smoothly toward the branch's end, and never thicker than the parent where the branch is attached.
    /// </summary>
    public float TargetRadius(Branch b, float arcPos)
    {
        PlantSettings s = Settings;
        float remaining = Mathf.Max(0f, MaxLengthOf(b) - arcPos);//length left to the branch's end
        float f = PlantSettings.EndRadiusFraction;
        float endTaper = f + (1f - f) * (1f - Mathf.Exp(-remaining / s.endTaperLength));
        float r = s.MaxRadiusAt(DistanceFromBase(b, arcPos), b.depth) * endTaper;
        return b.parent == null ? r : Mathf.Min(r, TargetRadius(b.parent, b.attachArc));
    }

    /// <summary>
    /// Length a branch grows to (classic: from the current settings, so sliders act immediately;
    /// L-system: not known in advance, its current length).
    /// </summary>
    public float MaxLengthOf(Branch b) => Development.MaxLengthOf(this, b);

    /// <summary>One fixed simulation step. Returns true, because thickening and bending change the shape every step.</summary>
    public bool Step(float dt)
    {
        SimTime += dt;//leaves keep aging and stems keep thickening even after all tips stopped
        //new tissue is only formed in the Growing phase
        if (Phase == PlantPhase.Growing) Development.Grow(this, Settings.growthSpeed * dt);
        //positions and radii for the new sim time; creation order = parents before children
        foreach (Branch b in Branches) b.UpdateGeometry(this);
        //anything that now touches an obstacle turns/bends away, before this state is ever drawn
        ResolveContacts(dt);
        Development.AfterStep(this);
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
            //phases set to OnTrigger only end through EnterNextPhase / AdvanceTo (called from outside)
            case PlantPhase.Growing:
                if (s.growingEnd != PhaseEnd.Automatic) break;
                bool allFinished = true;
                foreach (Branch b in Branches) if (b.finishTime < 0f && !b.stopped) { allFinished = false; break; }
                if (allFinished || inPhase >= s.growingDuration) EnterNextPhase();
                break;
            case PlantPhase.Flowering:
                if (s.floweringEnd == PhaseEnd.Automatic && inPhase >= s.floweringDuration) EnterNextPhase();
                break;
            case PlantPhase.Fruiting:
                if (s.fruitingEnd == PhaseEnd.Automatic && inPhase >= s.fruitingDuration) EnterNextPhase();
                break;
            case PlantPhase.Withering:
                //over once the wave has reached the base and the last organs/stems there have withered
                float longest = Mathf.Max(s.organWitherTime, s.stemWitherTime);
                if (inPhase >= s.witherWaveDuration + longest + 3f * s.witherJitter) EnterPhase(PlantPhase.Dead);
                break;
        }
    }

    /// <summary>Moves on through the phases until the given one (or a later one) is reached. Skipped phases still start and end.</summary>
    public void AdvanceTo(PlantPhase target)
    {
        while (Phase < target && Phase < PlantPhase.Dead) EnterNextPhase();
    }

    /// <summary>
    /// Ends the current phase and starts the next enabled one: the trigger for phases set to OnTrigger,
    /// and usable from outside to skip ahead (e.g. for testing).
    /// </summary>
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
        PhaseChanged?.Invoke(next);
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
            var r = new System.Random(unchecked(f.branch.seed * 486187739 + 4 + 7919 * f.indexOnBranch));
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
        foreach (Branch b in Branches) witherMaxDistance = Mathf.Max(witherMaxDistance, DistanceFromBase(b, b.committedLength));
        //own stream for the jitter, so withering never shifts any other random decision
        var r = new System.Random(unchecked(Root.seed * 486187739 + 3));
        //always the same draws per organ; an earlier wither start (e.g. flowers without fruit) is kept
        foreach (Leaf leaf in Leaves)
            leaf.witherStart = Mathf.Min(leaf.witherStart,
                WaveArrival(DistanceFromBase(leaf.branch, leaf.arcPos)) + Mathf.Abs(Gaussian(r)) * s.witherJitter);
        foreach (Flower f in Flowers)
            f.witherStart = Mathf.Min(f.witherStart,
                WaveArrival(DistanceFromBase(f.branch, f.arcPos)) + Mathf.Abs(Gaussian(r)) * s.witherJitter);
    }

    /// <summary>Sim time the withering wave reaches a point at the given distance from the base.</summary>
    float WaveArrival(float distFromBase) =>
        WitherStartTime + (1f - Mathf.Clamp01(distFromBase / witherMaxDistance)) * Settings.witherWaveDuration;

    /// <summary>How withered a point of stem is: 0 = fresh, 1 = fully withered (discolored and thinned).</summary>
    public float StemWither(Branch b, float arcPos)
    {
        if (SimTime < WitherStartTime) return 0f;
        float start = WaveArrival(DistanceFromBase(b, arcPos));
        return Mathf.Clamp01((SimTime - start) / Settings.stemWitherTime);
    }

    /// <summary>How withered a leaf is: 0 = fresh, 1 = fully withered. At 1 it falls off.</summary>
    public float LeafWither(Leaf leaf) => Mathf.Clamp01((SimTime - leaf.witherStart) / Settings.organWitherTime);

    /// <summary>How withered a flower is: 0 = fresh, 1 = fully withered. At 1 it falls off.</summary>
    public float FlowerWither(Flower f) => Mathf.Clamp01((SimTime - f.witherStart) / Settings.organWitherTime);

    /// <summary>Classic: decides once whether a finished branch gets a flower at its tip, and creates it.</summary>
    public void FlowerCheck(Branch b)
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
        if (uFlower >= Settings.flowerProbability || Flowers.Count >= Settings.maxFlowers) return;//(safety limit)

        int count = Settings.flowerPrefabs != null ? Settings.flowerPrefabs.Length : 0;
        int last = b.SegmentCount - 1;
        var flower = new Flower
        {
            branch = b,
            seg = last,
            offset = b.segLength[last],//end of the last segment = the branch's end point
            arcPos = b.committedLength,
            indexOnBranch = b.flowerCount++,
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.flowerSizeNoise),
            rollDeg = (float)(uRoll * 360.0),
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
            dir = b.segRest[last],//as grown; bending and tilting are applied on top
        };
        b.flower = flower;
        Flowers.Add(flower);
    }

    /// <summary>Called by a branch when its tip passes a spawn check position (distance from its base).</summary>
    public void SpawnCheck(Branch branch, float arcPos)
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
        float u = Mathf.Clamp01(arcPos / Mathf.Max(1e-6f, MaxLengthOf(branch)));//position as fraction of the branch's maximum length
        float pLeafBoosted = Mathf.Lerp(s.spawnProbability, s.leafProbabilityAtEnd, Mathf.Pow(u, PlantSettings.LeafEndBoostExponent));
        bool normalSpawn = uSpawn < s.spawnProbability;               //leaf or branch, as before
        bool extraLeaf = !normalSpawn && uSpawn < pLeafBoosted;        //only possible toward the end, always a leaf
        if (!normalSpawn && !extraLeaf) return;

        //golden angle between successive leaves/branches on this branch, plus noise
        branch.phyllotaxisAngle += GoldenAngleDeg + angleNoise * s.phyllotaxisNoiseDeg;
        Vector3 outward = branch.OutwardAt(branch.phyllotaxisAngle);
        //the check lies on the tip segment; attachments are stored as (segment, offset in it)
        int seg = branch.SegmentCount;
        float offset = arcPos - branch.committedLength;

        bool branchAllowed = normalSpawn
                             && uKind < s.branchProbability
                             && branch.depth < s.maxDepth
                             && arcPos >= s.branchStartFraction * MaxLengthOf(branch)
                             && s.SideBranchLength(DistanceFromBase(branch, arcPos)) >= PlantSettings.MinBranchLength
                             && Branches.Count < s.maxBranches;//safety limit
        if (branchAllowed)
            SpawnBranch(branch, seg, offset, arcPos, outward);
        else if (Leaves.Count < s.maxLeaves)//safety limit
            SpawnLeaf(branch, seg, offset, arcPos, outward, sizeNoise, uVariant);
    }

    void SpawnBranch(Branch parent, int seg, float offset, float arcPos, Vector3 outward)
    {
        float elev = Settings.branchElevationDeg * Mathf.Deg2Rad;
        //initial direction: outward, tilted toward the parent's direction by the elevation angle
        Vector3 dir = Mathf.Cos(elev) * outward + Mathf.Sin(elev) * parent.tipDir;
        //perpendicular to both parent direction and outward -> always perpendicular to dir as well
        Vector3 normal = Vector3.Cross(parent.tipDir, outward).normalized;
        int childSeed = DeriveSeed(parent.seed, parent.children.Count);
        var child = new Branch(childSeed, parent.depth + 1, parent, seg, offset, arcPos,
                               parent.PointAt(seg, offset), dir, normal, SimTime);
        child.nextSpawnCheck = Settings.spawnStartFraction * MaxLengthOf(child);
        parent.children.Add(child);
        Branches.Add(child);
    }

    void SpawnLeaf(Branch branch, int seg, float offset, float arcPos, Vector3 outward,
                   float sizeNoise, double uVariant)
    {
        int count = Settings.leafPrefabs != null ? Settings.leafPrefabs.Length : 0;
        var leaf = new Leaf
        {
            branch = branch,
            seg = seg,
            offset = offset,
            arcPos = arcPos,
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

    /// <summary>
    /// Orientation of a leaf (same as in the visuals): blade (+Z) outward, tilted toward the branch by the
    /// elevation angle; upper surface (+Y) facing roughly along the branch.
    /// </summary>
    public Quaternion LeafRotation(Vector3 tangent, Vector3 outward)
    {
        float elev = Settings.leafElevationDeg * Mathf.Deg2Rad;
        Vector3 blade = Mathf.Cos(elev) * outward + Mathf.Sin(elev) * tangent;
        Vector3 side = Vector3.Cross(tangent, outward);
        return Quaternion.LookRotation(blade, Vector3.Cross(blade, side));
    }

    /// <summary>Current orientation of a leaf: as grown, tilted at its base, then carried by its branch's bend.</summary>
    public Quaternion LeafRotationNow(Leaf leaf) =>
        Quaternion.Normalize(leaf.branch.RotAt(leaf.seg) * leaf.tilt *
                             (leaf.hasFrame ? leaf.frame : LeafRotation(leaf.tangent, leaf.outward)));

    /// <summary>Current direction from the branch's center line toward the leaf's base.</summary>
    public Vector3 LeafOutwardNow(Leaf leaf) => leaf.branch.RotAt(leaf.seg) * leaf.outward;

    /// <summary>Current position of a leaf's base: on its branch's surface (it tilts around this point).</summary>
    public Vector3 LeafBasePos(Leaf leaf) => LeafCenter(leaf) + LeafOutwardNow(leaf) * LeafBranchRadius(leaf);

    /// <summary>Orientation of a flower: its +Y along its current direction, plus its own turn around that axis.</summary>
    public Quaternion FlowerRotation(Flower f) =>
        Quaternion.FromToRotation(Vector3.up, FlowerDirection(f)) * Quaternion.AngleAxis(f.rollDeg, Vector3.up);

    /// <summary>Direction for the next segment, in the current (bent) shape: toward the light, plus noise.</summary>
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
        float turnFraction = 1f - Mathf.Exp(-s.DirectionBiasAt(b.depth) * PlantSettings.SegmentLength);
        Vector3 biased = Vector3.Slerp(b.TipDirNow, toTarget, turnFraction);
        //variances of independent steps add up, so std per segment scales with sqrt(segmentLength)
        //total variance is independent from L this way, because: (D/L)*sigma^2*sqrt(L)^2 = D*sigma^2
        float segmentStd = s.perturbationStd * Mathf.Sqrt(PlantSettings.SegmentLength);
        System.Random r = b.dirRng;
        Vector3 noise = new Vector3(Gaussian(r), Gaussian(r), Gaussian(r)) * segmentStd;
        Vector3 result = biased + noise;
        return result.sqrMagnitude > 1e-6f ? result.normalized : b.TipDirNow;//check if noise canceled out the direction
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
        return Mathf.Pow(t, PlantSettings.LeafGrowthExponent);
    }

    /// <summary>Full-grown size of a leaf: linear decrease with distance from the plant base, times its random factor.</summary>
    public float LeafTargetSize(Leaf leaf)
    {
        float d = DistanceFromBase(leaf.branch, leaf.arcPos);
        float lengthFactor = Mathf.Max(PlantSettings.MinLeafSizeFraction, 1f - Settings.leafSizeFalloff * Settings.Relative(d));
        return Settings.maxLeafSize * lengthFactor * leaf.sizeFactor;
    }

    /// <summary>Current size of a leaf: grown so far, but never beyond the size it had when it got stuck.</summary>
    public float LeafSize(Leaf leaf) => Mathf.Min(LeafTargetSize(leaf) * LeafGrowth(leaf), leaf.sizeCap);

    // --- flowers ---

    /// <summary>Current position of a flower's base: the end point of its branch (moves when the branch bends).</summary>
    public Vector3 FlowerPosition(Flower f) => f.branch.PointAt(f.seg, f.offset);

    /// <summary>Direction the flower faces now: its branch's last direction, with bends and its own tilt applied.</summary>
    public Vector3 FlowerDirection(Flower f) => f.branch.RotAt(f.seg + 1) * (f.tilt * f.dir);

    /// <summary>Opening progress of a flower, 0 = bud to 1 = fully open (input for FlowerMorph).</summary>
    public float FlowerGrowth(Flower f)
    {
        //closed until the Flowering phase starts (or until the bud appears, if that's later); never opens if Flowering is skipped
        float openStart = Mathf.Max(FloweringStartTime, f.birthTime);
        if (SimTime <= openStart) return 0f;
        float t = Mathf.Clamp01((SimTime - openStart) / Settings.flowerOpenDuration);
        return t;
    }

    /// <summary>Fruit development of a flower: 0 = (still) a flower, 1 = ripe fruit. Always 0 for flowers that didn't set fruit.</summary>
    public float FruitProgress(Flower f)
    {
        if (!f.setsFruit || SimTime <= FruitingStartTime) return 0f;
        float t = Mathf.Clamp01((SimTime - FruitingStartTime) / Settings.fruitDevelopDuration);
        return t;
    }

    /// <summary>How far a new bud has emerged: 0 = just appeared (size 0), 1 = full closed-bud size.</summary>
    public float BudEmergence(Flower f) => Mathf.Clamp01((SimTime - f.birthTime) / Settings.budGrowDuration);

    /// <summary>Full size of a flower: maxFlowerSize times its random factor.</summary>
    public float FlowerTargetSize(Flower f) => Settings.maxFlowerSize * f.sizeFactor;

    /// <summary>
    /// Size passed to FlowerMorph: full size times bud emergence, but never beyond the size it had when it got stuck.
    /// </summary>
    public float FlowerSize(Flower f) => Mathf.Min(FlowerTargetSize(f) * BudEmergence(f), f.sizeCap);

    // --- contacts: turning and bending away from obstacles ---

    /// <summary>
    /// Every step, after growing: everything touching an obstacle is moved out of it before the step is drawn.
    /// Organs first turn (leaves around their branch, flowers tilt), the rest is passed on to the branches,
    /// which bend at their nodes, soft (thin) ones much more than stiff (thick) ones, up into parent branches.
    /// Several passes, since moving one part can make another touch. What still can't be resolved stops growing.
    /// </summary>
    void ResolveContacts(float dt)
    {
        PlantSettings s = Settings;
        bool anyBend = false;
        foreach (Branch b in Branches)
        {
            b.RelaxBends(dt, PlantSettings.BendRelaxTime, s.bendSettleTime, SimTime - b.lastContactTime < 1f);
            anyBend |= b.HasBends;
        }
        if (anyBend) foreach (Branch b in Branches) b.UpdateGeometry(this);
        if (Obstacles == null) return;

        bool stillTouching = false;
        //speed limits: how far each node / organ may turn in this whole step, shared by all contacts and passes
        float nodeStep = PlantSettings.BendSpeed * dt;
        float organStep = 1.5f * nodeStep;
        moveStep = s.bendMoveSpeed * dt;
        foreach (Branch b in Branches) b.ResetBendBudget();
        foreach (Leaf leaf in Leaves) { leaf.movedThisStep = false; leaf.tiltUsed = 0f; }
        foreach (Flower f in Flowers) { f.movedThisStep = false; f.tiltUsed = 0f; }
        for (int pass = 0; pass < PlantSettings.ContactIterations; pass++)
        {
            bool any = false;
            foreach (Branch b in Branches) b.movedThisPass = false;

            //branches: each segment as a capsule of its current thickness
            foreach (Branch b in Branches)
            {
                if (pass > 0 && !b.affectedLastPass) continue;
                if (!b.NearObstacle(Obstacles)) continue;
                for (int k = 0; k < b.SegmentCount; k++)
                {
                    float r = Mathf.Max(b.nodeRadius[k], b.nodeRadius[k + 1]);
                    if (r < 1e-4f) continue;
                    if (Obstacles.CapsulePenetration(b.nodes[k], b.nodes[k + 1], r + 0.5f * s.obstacleClearance, out Vector3 push))
                    {
                        //first a local sideways shift (the stem keeps thickening and moves away, like a dent),
                        //only what that can't do goes to bending
                        Vector3 rest = b.ShiftAway(k, push * 1.02f, moveStep, MaxShiftRadii, s.obstacleClearance);
                        b.lastContactTime = SimTime;//pressed: no elastic springback for now
                        b.movedThisPass = true;
                        if (rest.sqrMagnitude > 1e-12f) PushBranch(b, k, (b.nodes[k] + b.nodes[k + 1]) * 0.5f, rest, nodeStep);
                        any = true;
                    }
                }
            }
            //leaves: tilt at their base first, the rest bends the branch
            foreach (Leaf leaf in Leaves)
            {
                if (pass > 0 && !leaf.branch.affectedLastPass && !leaf.movedThisStep) continue;
                if (LeafWither(leaf) >= 1f || !LeafPush(leaf, out Vector3 push, out Vector3 at)) continue;
                any = true;
                push = TiltOrgan(ref leaf.tilt, ref leaf.tiltUsed, leaf.branch.RotAt(leaf.seg), LeafBasePos(leaf), push, at, s.leafMaxTiltDeg, organStep);
                leaf.movedThisStep = true;
                if (push.sqrMagnitude > 1e-12f) PushBranch(leaf.branch, leaf.seg, at, push, nodeStep);
            }
            //flowers/fruits: tilt first, the rest bends the branch
            foreach (Flower f in Flowers)
            {
                if (pass > 0 && !f.branch.affectedLastPass && !f.movedThisStep) continue;
                if (FlowerWither(f) >= 1f || !FlowerPush(f, out Vector3 push, out Vector3 at)) continue;
                any = true;
                push = TiltOrgan(ref f.tilt, ref f.tiltUsed, f.branch.RotAt(f.seg + 1), FlowerPosition(f), push, at, s.flowerMaxTiltDeg, organStep);
                f.movedThisStep = true;
                if (push.sqrMagnitude > 1e-12f) PushBranch(f.branch, f.seg, at, push, nodeStep);
            }

            stillTouching = any;
            if (!any) break;
            //bent branches and everything on them (descendants included) have moved: recompute, check them again
            foreach (Branch b in Branches)//creation order: parents before children
            {
                b.affectedLastPass = b.movedThisPass || (b.parent != null && b.parent.affectedLastPass);
                if (b.affectedLastPass) b.UpdateGeometry(this);
            }
        }
        if (stillTouching) StopStuckOrgans();
    }

    /// <summary>
    /// Whatever still touches after all passes stops growing at its current size: organs stop growing, branch pieces
    /// stop thickening. While it still touches, it shrinks very slowly (0.5% per step) to get free.
    /// </summary>
    void StopStuckOrgans()
    {
        foreach (Branch b in Branches)
        {
            if (!b.NearObstacle(Obstacles)) continue;
            for (int k = 0; k < b.SegmentCount; k++)
            {
                float r = Mathf.Max(b.nodeRadius[k], b.nodeRadius[k + 1]);
                if (r < 1e-4f) continue;
                if (Obstacles.CapsulePenetration(b.nodes[k], b.nodes[k + 1], r + 0.5f * Settings.obstacleClearance, out _))
                {
                    b.nodeRadiusCap[k] = b.nodeRadius[k] * StuckShrink;
                    b.nodeRadiusCap[k + 1] = b.nodeRadius[k + 1] * StuckShrink;
                }
            }
        }
        foreach (Leaf leaf in Leaves)
            if (LeafWither(leaf) < 1f && LeafPush(leaf, out _, out _)) leaf.sizeCap = LeafSize(leaf) * StuckShrink;
        foreach (Flower f in Flowers)
            if (FlowerWither(f) < 1f && FlowerPush(f, out _, out _)) f.sizeCap = FlowerSize(f) * StuckShrink;
    }
    const float StuckShrink = 0.995f;

    /// <summary>
    /// Bends a branch so that point X moves by `push`: nodes from the touching segment back to the plant's base
    /// (through parent branches) turn a little. Each node's flexibility is (bendFlexibleRadius / radius)^3, like a
    /// real beam, and its leverage is how much turning there moves X in the push direction. Regularized least squares:
    /// soft nodes with good leverage do the work; if only stiff nodes are available, the push is mostly NOT resolved
    /// (the organ stops growing instead of the trunk swinging). Nodes with poor leverage (turning would mostly move X
    /// sideways) don't take part; nodes turn at most maxStepDeg per step, and X moves at most bendMoveSpeed.
    /// </summary>
    void PushBranch(Branch b, int seg, Vector3 X, Vector3 push, float maxStepDeg)
    {
        float mag = push.magnitude * 1.02f;//a hair more, so it ends up just clear instead of just touching
        if (mag < 1e-7f) return;
        Vector3 dirP = push / push.magnitude;
        float r0 = Settings.bendFlexibleRadius;
        //regularization: the "price" of not resolving the push. A node of radius r0 with a lever of 10 x r0
        //resolves about half of the push per pass; thick nodes almost nothing
        float leverScale = 10f * r0;//scales with the plant (0.1 units for a flexible radius of 0.01)
        float lambda = leverScale * leverScale;

        chainBranch.Clear(); chainNode.Clear(); chainWeight.Clear(); chainLever.Clear(); chainAxis.Clear();
        Branch cur = b;
        int node = Mathf.Min(seg, b.nodes.Count - 1);
        float sum = lambda;
        while (cur != null)
        {
            cur.lastContactTime = SimTime;//pressed: no elastic springback for now
            for (int i = node; i >= 0; i--)
            {
                Vector3 arm = X - cur.nodes[i];
                Vector3 axis = Vector3.Cross(arm, dirP);
                float lever = axis.magnitude;//how far X moves in the push direction per radian of turning here
                //poor leverage: turning here would mostly move X sideways, not away from the obstacle
                if (lever < 1e-6f || lever < 0.2f * arm.magnitude) continue;
                float r = Mathf.Max(cur.nodeRadius[i], 0.25f * r0);//the very thinnest parts aren't infinitely soft
                float ratio = r0 / r;
                float flexibility = ratio * ratio * ratio;
                chainBranch.Add(cur); chainNode.Add(i); chainWeight.Add(flexibility); chainLever.Add(lever); chainAxis.Add(axis / lever);
                sum += flexibility * lever * lever;
            }
            if (cur.parent == null) break;
            node = Mathf.Min(cur.attachSeg, cur.parent.nodes.Count - 1);
            cur = cur.parent;
        }
        if (chainBranch.Count == 0) return;
        //how far X would move with these angles; never faster than bendMoveSpeed
        float moved = 0f;
        for (int j = 0; j < chainBranch.Count; j++) moved += chainWeight[j] * chainLever[j] * chainLever[j] * mag / sum;
        float slow = moved > moveStep ? moveStep / moved : 1f;
        for (int j = 0; j < chainBranch.Count; j++)
        {
            float angle = chainWeight[j] * chainLever[j] * mag / sum * slow;//radians
            float applied = chainBranch[j].BendAtNode(chainNode[j], Quaternion.AngleAxis(angle * Mathf.Rad2Deg, chainAxis[j]),
                                                      PlantSettings.MaxJointBendDeg, maxStepDeg);
            if (applied > 0f) chainBranch[j].movedThisPass = true;
        }
    }
    float moveStep;//how far a touching point may be moved by bending in this step
    const float MaxShiftRadii = 2f;//a node shifts sideways by at most twice its own radius; more is left to bending
    readonly List<Branch> chainBranch = new List<Branch>();
    readonly List<int> chainNode = new List<int>();
    readonly List<float> chainWeight = new List<float>(), chainLever = new List<float>();
    readonly List<Vector3> chainAxis = new List<Vector3>();

    /// <summary>
    /// Tilts an organ around its base (pivot) so its contact point moves by `push`: total tilt within maxDeg,
    /// at most stepBudgetDeg per step (`used` counts what this step has used already).
    /// The tilt is stored in the rest shape, so later bends of the branch carry it along.
    /// Returns the part of the push that's left for the branch.
    /// </summary>
    Vector3 TiltOrgan(ref Quaternion tilt, ref float used, Quaternion branchBend, Vector3 pivot, Vector3 push, Vector3 at,
                      float maxDeg, float stepBudgetDeg)
    {
        Vector3 lever = at - pivot;
        float l2 = lever.sqrMagnitude;
        Vector3 cross = Vector3.Cross(lever, push);
        float left = stepBudgetDeg - used;
        if (l2 < 1e-10f || cross.sqrMagnitude < 1e-16f || left <= 1e-5f) return push;
        float angle = Mathf.Min(cross.magnitude / l2 * 1.02f, left * Mathf.Deg2Rad);//radians, speed-limited
        Vector3 axis = cross.normalized;
        Quaternion restTurn = Quaternion.Inverse(branchBend) * Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis) * branchBend;
        Quaternion wanted = Quaternion.Normalize(restTurn * tilt);
        float fraction = 1f;
        float total = Quaternion.Angle(Quaternion.identity, wanted);
        if (total > maxDeg)
        {
            float current = Quaternion.Angle(Quaternion.identity, tilt);
            fraction = current >= maxDeg - 1e-3f ? 0f : Mathf.Clamp01((maxDeg - current) / Mathf.Max(1e-4f, total - current));
            wanted = Quaternion.Normalize(Quaternion.Slerp(tilt, wanted, fraction));
        }
        if (float.IsNaN(wanted.x)) return push;
        tilt = wanted;
        used += angle * fraction * Mathf.Rad2Deg;
        Vector3 rest = push - Vector3.Cross(axis * (angle * fraction), lever);
        return Vector3.Dot(rest, push) > 0f ? rest : Vector3.zero;
    }

    /// <summary>How far a leaf reaches into obstacles right now (combined push out) and where (average contact point).</summary>
    bool LeafPush(Leaf leaf, out Vector3 push, out Vector3 at)
    {
        push = at = Vector3.zero;
        if (LeafShapes == null || leaf.variant < 0 || leaf.variant >= LeafShapes.Length || LeafShapes[leaf.variant] == null) return false;
        OrganCompound shape = LeafShapes[leaf.variant];
        return OrganPush(shape, shape.union, LeafBasePos(leaf), LeafRotationNow(leaf), LeafSize(leaf) * shape.rootScale, out push, out at);
    }

    /// <summary>Same for a flower, in the morph state it's currently closest to and at its current size.</summary>
    bool FlowerPush(Flower f, out Vector3 push, out Vector3 at)
    {
        push = at = Vector3.zero;
        if (FlowerShapes == null || f.variant < 0 || f.variant >= FlowerShapes.Length || FlowerShapes[f.variant] == null) return false;
        OrganCompound shape = FlowerShapes[f.variant];
        OrganBox[] boxes = shape.BoxesFor(FlowerGrowth(f), FruitProgress(f), FlowerWither(f), out float morphScale);
        return OrganPush(shape, boxes, FlowerPosition(f), FlowerRotation(f), FlowerSize(f) * morphScale * shape.rootScale, out push, out at);
    }

    /// <summary>
    /// Checks an organ's boxes against obstacles. The push combines all touching boxes without counting the same
    /// wall twice: for each box, only what's still missing in its direction is added.
    /// </summary>
    bool OrganPush(OrganCompound shape, OrganBox[] boxes, Vector3 pos, Quaternion rot, float scale, out Vector3 push, out Vector3 at)
    {
        push = at = Vector3.zero;
        if (scale < 1e-4f || boxes == null) return false;
        float gap = 0.5f * Settings.obstacleClearance;
        if (!Obstacles.AnyWithin(pos + rot * (shape.boundsCenter * scale), shape.boundsRadius * scale + gap)) return false;
        int n = 0;
        foreach (OrganBox box in boxes)
        {
            Vector3 c = pos + rot * (box.center * scale);
            if (!Obstacles.BoxPenetration(c, box.half * scale + Vector3.one * gap, rot, out Vector3 v)) continue;
            float d = v.magnitude;
            if (d < 1e-7f) continue;
            Vector3 dir = v / d;
            float already = Vector3.Dot(push, dir);
            if (already < d) push += dir * (d - already);
            at += c;
            n++;
        }
        if (n == 0) return false;
        at /= n;
        return true;
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

    // --- creating branches and organs at a branch's tip (L-system development) ---

    /// <summary>
    /// A new branch starting at the parent's current tip, growing in direction dir (rest shape of the parent),
    /// with normal as its reference axis (perpendicular to dir). Null if the branch limit is reached.
    /// </summary>
    public Branch AddBranchAtTip(Branch parent, Vector3 dir, Vector3 normal)
    {
        if (Branches.Count >= Settings.maxBranches) { LimitReached("branches", "maxBranches", Settings.maxBranches); return null; }//safety limit
        int seg = parent.SegmentCount;
        float offset = parent.tipSegLen;
        var child = new Branch(DeriveSeed(parent.seed, parent.children.Count), parent.depth + 1, parent, seg, offset,
                               parent.TotalLength, parent.PointAt(seg, offset), dir.normalized, normal.normalized, SimTime);
        child.nextSpawnCheck = float.PositiveInfinity;//no classic spawn checks
        parent.children.Add(child);
        Branches.Add(child);
        return child;
    }

    /// <summary>
    /// A leaf at a branch's current tip, oriented by the turtle (rest shape): blade along `heading`, upper surface toward `up`.
    /// sizeFactor multiplies its random size factor. Null if the leaf limit is reached.
    /// </summary>
    public Leaf AddLeafAtTip(Branch branch, Vector3 heading, Vector3 up, float sizeFactor)
    {
        if (Leaves.Count >= Settings.maxLeaves) { LimitReached("leaves", "maxLeaves", Settings.maxLeaves); return null; }//safety limit
        System.Random r = branch.spawnRng;
        double uVariant = r.NextDouble();
        float sizeNoise = Gaussian(r);
        Vector3 tangent = branch.tipDir;
        Vector3 outward = Vector3.ProjectOnPlane(heading, tangent);
        if (outward.sqrMagnitude < 1e-4f) outward = Vector3.ProjectOnPlane(up, tangent);//along the branch: use the turtle's up side
        if (outward.sqrMagnitude < 1e-8f) outward = branch.tipNormal;
        outward.Normalize();
        int count = Settings.leafPrefabs != null ? Settings.leafPrefabs.Length : 0;
        var leaf = new Leaf
        {
            branch = branch,
            seg = branch.SegmentCount,
            offset = branch.tipSegLen,
            arcPos = branch.TotalLength,
            tangent = tangent,
            outward = outward,
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.leafSizeNoise) * Mathf.Max(0f, sizeFactor),
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
            hasFrame = true,
            frame = Quaternion.LookRotation(heading, up),
        };
        branch.leaves.Add(leaf);
        Leaves.Add(leaf);
        return leaf;
    }

    /// <summary>A flower bud at a branch's current tip, facing `heading` (rest shape). Null if the flower limit is reached.</summary>
    public Flower AddFlowerAtTip(Branch branch, Vector3 heading, float sizeFactor)
    {
        if (Flowers.Count >= Settings.maxFlowers) { LimitReached("flowers", "maxFlowers", Settings.maxFlowers); return null; }//safety limit
        System.Random r = branch.spawnRng;
        double uRoll = r.NextDouble();
        double uVariant = r.NextDouble();
        float sizeNoise = Gaussian(r);
        //right at the end of a committed segment (e.g. after an F): attach there, like a classic flower
        bool atNode = branch.tipSegLen < 1e-5f && branch.SegmentCount > 0;
        int seg = atNode ? branch.SegmentCount - 1 : branch.SegmentCount;
        int count = Settings.flowerPrefabs != null ? Settings.flowerPrefabs.Length : 0;
        var flower = new Flower
        {
            branch = branch,
            seg = seg,
            offset = atNode ? branch.segLength[seg] : branch.tipSegLen,
            arcPos = branch.TotalLength,
            indexOnBranch = branch.flowerCount++,
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.flowerSizeNoise) * Mathf.Max(0f, sizeFactor),
            rollDeg = (float)(uRoll * 360.0),
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
            dir = heading.normalized,
        };
        if (branch.flower == null) branch.flower = flower;
        Flowers.Add(flower);
        return flower;
    }

    readonly HashSet<string> limitsReported = new HashSet<string>();

    /// <summary>
    /// Says once per plant that a safety limit was reached: everything of that kind the rules still produce is left
    /// out (with brackets, including what grows inside them), so the plant can look quite different from the preview.
    /// </summary>
    void LimitReached(string what, string setting, int limit)
    {
        if (!limitsReported.Add(what)) return;
        Debug.LogWarning($"Plant ({Settings.name}): {limit} {what} reached ({setting}). The rules produce more: " +
                         $"further {what} are left out from here on. Raise {setting} or make the rules produce fewer " +
                         "(e.g. fewer steps).", Settings);
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
