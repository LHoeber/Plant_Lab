using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>How a phase ends: on its own (duration, or for Growing also when all tips are finished), or only when triggered.</summary>
public enum PhaseEnd { Automatic, OnTrigger }

/// <summary>What decides where branches, leaves and flowers appear.</summary>
public enum DevelopmentMode
{
    /// <summary>The original spawn logic (spawn checks along the branches, probabilities, length limits).</summary>
    Classic,
    /// <summary>L-system rules (see the L-system section).</summary>
    LSystem,
}

/// <summary>
/// All parameters that define how a plant grows, stored as an asset file (ScriptableObject).
/// - Changes made during Play mode are kept (unlike values on a component).
/// - Several assets = several presets (e.g. one per experimental condition), versioned in Git.
/// Create one via: Project window -> right-click -> Create -> Plant -> Plant Settings.
/// </summary>
[CreateAssetMenu(fileName = "PlantSettings", menuName = "Plant/Plant Settings")]
public class PlantSettings : ScriptableObject
{
    /// <summary>
    /// Fixed simulation time step, in sim seconds. Not a setting: coarser steps only make growth look jumpy,
    /// finer ones only cost performance. (How fast things happen is controlled by the durations and speeds below,
    /// and how fast you watch it by simSpeed on the Plant Growth component.)
    /// </summary>
    public const float DeltaT = 0.02f;

    /// <summary>
    /// Length of one branch segment (a node every this many units). Not a setting: it's only the resolution of the
    /// branch skeleton. Shorter = smoother but more nodes to compute; longer = visible kinks and coarser bending.
    /// Bending toward the light and the random wobble per unit length don't depend on it.
    /// </summary>
    public const float SegmentLength = 0.025f;

    /// <summary>Thickness never drops below this fraction of maxRadius (times the level factor) with height. Not a setting.</summary>
    public const float MinRadiusFraction = 0.1f;
    /// <summary>Radius at a finished branch's very end, as a fraction of what it would be without the end taper. Not a setting.</summary>
    public const float EndRadiusFraction = 0.2f;
    /// <summary>Corners of each branch's cross-section (rendering detail). Not a setting.</summary>
    public const int RadialSegments = 6;

    /// <summary>Shape of the extra-leaf rise toward branch ends (1 = linear, higher = mostly near the end). Not a setting.</summary>
    public const float LeafEndBoostExponent = 1.5f;
    /// <summary>A side branch whose planned length would be shorter than this isn't created (a leaf appears instead). Not a setting.</summary>
    public const float MinBranchLength = 0.02f;
    /// <summary>Leaf growth curve: size ~ (age / leafGrowthDuration)^this (fast at first, then slowing down). Not a setting.</summary>
    public const float LeafGrowthExponent = 0.7f;
    /// <summary>Leaves never get smaller than this fraction of maxLeafSize because of leafSizeFalloff. Not a setting.</summary>
    public const float MinLeafSizeFraction = 0.3f;

    [Header("Phases (fixed order: Growing -> Flowering -> Fruiting -> Withering)")]
    [Tooltip("Automatic: Growing ends when all branches have finished, or after growingDuration. " +
             "On Trigger: it ends only when triggered (PlantGrowth.TriggerNextPhase, e.g. from an intervention, " +
             "or the 'Skip To Next Phase' box), however long that takes.")]
    [NoRandomize] public PhaseEnd growingEnd = PhaseEnd.Automatic;
    [Tooltip("Sim seconds of growing at most (Automatic): Growing ends when all branches have finished, or after this time " +
             "(unfinished branches then stop where they are and get no bud).")]
    [FormerlySerializedAs("growingMaxDuration")]//keeps the value of presets saved under the old name
    [NoRandomize][Range(1f, 600f)] public float growingDuration = 20f;
    [Tooltip("Flowering phase: the buds that exist open. Off = skipped, buds wither unopened.")]
    public bool enableFlowering = true;
    [Tooltip("Automatic: Flowering ends after floweringDuration. On Trigger: only when triggered.")]
    [NoRandomize] public PhaseEnd floweringEnd = PhaseEnd.Automatic;
    [Tooltip("Sim seconds the Flowering phase lasts (Automatic).")]
    [NoRandomize][Range(1f, 60f)] public float floweringDuration = 10f;
    [Tooltip("Fruiting phase: open flowers develop into fruit (with fruitSetProbability), the others wither and fall. Off = skipped.")]
    public bool enableFruiting = true;
    [Tooltip("Automatic: Fruiting ends after fruitingDuration. On Trigger: only when triggered.")]
    [NoRandomize] public PhaseEnd fruitingEnd = PhaseEnd.Automatic;
    [Tooltip("Sim seconds the Fruiting phase lasts (Automatic).")]
    [NoRandomize][Range(1f, 60f)] public float fruitingDuration = 10f;

    [Header("Development")]
    [Tooltip("Classic: the original spawn settings (Spawn nodes, Branching, flowerProbability, ...). " +
             "LSystem: the rules in the L-system section decide where branches, leaves and flowers appear; " +
             "growth, thickness, light, collisions, organ sizes and withering still use the settings below.")]
    [NoRandomize] public DevelopmentMode development = DevelopmentMode.Classic;

    [Header("L-system (only used with development = LSystem)")]
    [Tooltip("Start of the derivation, e.g. A(0, 0).")]
    [LSystemOnly][TextArea(1, 3)] public string lsAxiom = "A(0, 0)";
    [Tooltip("One rule per line:  predecessor(params) : condition -> successor @ weight   ('#' starts a comment). " +
             "Growth happens at the tips: a module is replaced when the growing tip reaches it. " +
             "F(len) grows the branch, + - & ^ \\ / | turn, [ ] starts a branch, leaf and flower symbols place organs " +
             "(a parameter scales them, e.g. L(0.5)). A tip whose module has no applicable rule is finished.")]
    [LSystemOnly][TextArea(6, 30)] public string lsRules =
        "# tip A(s, o): has grown s units on an axis of branching order o (0 = main stem)\n" +
        "A(s, o) : s < maxLen * ratio**o and o < maxOrder -> F(dl) /(137.5) [ &(branchAngle) A(0, o+1) ] A(s+dl, o) @ pBranch\n" +
        "A(s, o) : s < maxLen * ratio**o -> F(dl) /(137.5) [ &(leafAngle) L ] A(s+dl, o) @ pLeaf\n" +
        "A(s, o) : s < maxLen * ratio**o -> F(dl) A(s+dl, o) @ max(0, 1 - pBranch - pLeaf)\n" +
        "# at its length limit the tip ends: with a flower bud, or without\n" +
        "A(s, o) : s >= maxLen * ratio**o -> K @ pFlower\n" +
        "A(s, o) : s >= maxLen * ratio**o -> @ 1 - pFlower";
    [Tooltip("Names usable in the rules. Each has a slider between its min and max; Randomize varies the values within them.")]
    [LSystemOnly]public List<LConstant> lsConstants = new List<LConstant>
    {
        new LConstant { name = "maxLen", value = 15f, min = 1f, max = 40f },
        new LConstant { name = "ratio", value = 0.5f, min = 0.1f, max = 1f },
        new LConstant { name = "maxOrder", value = 3f, min = 0f, max = 6f },
        new LConstant { name = "dl", value = 0.4f, min = 0.1f, max = 2f },
        new LConstant { name = "pBranch", value = 0.15f, min = 0f, max = 1f },
        new LConstant { name = "pLeaf", value = 0.6f, min = 0f, max = 1f },
        new LConstant { name = "pFlower", value = 0.3f, min = 0f, max = 1f },
        new LConstant { name = "branchAngle", value = 40f, min = 0f, max = 90f },
        new LConstant { name = "leafAngle", value = 60f, min = 0f, max = 90f },
    };
    [Tooltip("Meters per turtle unit: F(1) grows this long.")]
    [LSystemOnly][Length][NoRandomize][Range(0.005f, 0.5f)] public float lsUnit = 0.1f;
    [Tooltip("Angle of + - & ^ \\ / without a parameter (degrees).")]
    [LSystemOnly][NoRandomize][Range(0f, 180f)] public float lsAngle = 25f;
    [Tooltip("Symbols that place a leaf (one or more characters, e.g. \"L\").")]
    [LSystemOnly]public string leafSymbols = "L";
    [Tooltip("Symbols that place a flower bud (one or more characters, e.g. \"K\").")]
    [LSystemOnly]public string flowerSymbols = "K";
    [Tooltip("Cactus segment type for S(s) and ~ in the rules (a Cactus Segment asset). Empty: no cactus segments.")]
    [LSystemOnly][NoRandomize] public CactusSegmentSettings cactusSegment;

    [Header("Growth")]
    [Tooltip("How fast branch tips grow, units per sim second (all branches). Changes the plant's proportions: " +
             "leaves and thickening have their own durations, so slow growth gives a short, dense plant. " +
             "Final main stem height is roughly growthSpeed x growingDuration (at most maxLength). " +
             "To just watch faster, use simSpeed on the Plant Growth component instead.")]
    [Length][NoRandomize][Range(0.005f, 0.5f)] public float growthSpeed = 0.125f;
    [Tooltip("Maximum length of the main stem (along the stem, so a winding stem can be longer than the terrarium is high). " +
             "Also the reference length of the falloffs (thickness, leaf size). L-system: only that reference; " +
             "set it to about the length the main axis reaches.")]
    [Length][Range(0.05f, 5f)] public float maxLength = 2f;

    [Header("Direction")]
    [Tooltip("How strongly branches turn toward the light (per unit of stem length; independent of segment length). " +
             "0 = ignore the light, 10 = turn toward it almost immediately. Side branches react a bit less (20% less per level).")]
    [PerLength][Range(0f, 10f)] public float directionBias = 2f;
    [Tooltip("Random wobble of the growth direction (per sqrt of stem length; independent of segment length). " +
             "0 = smooth curves, 1.5 = very irregular.")]
    [PerSqrtLength][Range(0f, 1.5f)] public float perturbationStd = 0.7f;

    [Header("Thickness")]
    [Tooltip("Radius at the base of the main stem. Each point thickens toward its own maximum, " +
             "which gets smaller with height and with every branching level.")]
    [Length][Range(0.005f, 0.15f)] public float maxRadius = 0.08f;
    [Tooltip("Each branching level is this fraction as thick as the one before (0.65: side branch 65%, next level 42%, ...).")]
    [Range(0.3f, 1f)] public float radiusDepthFactor = 0.65f;
    [Tooltip("How much thinner the stem gets toward the top, relative to the main stem's length (exponential): " +
             "0 = same thickness all the way up, 1 = 37% of the base thickness at the top, 2 = 14%. " +
             "It never drops below 10% of maxRadius. Same shape whatever maxLength is.")]
    [Range(0f, 3f)] public float radiusFalloff = 1f;
    [Tooltip("Length of the narrowing toward each branch's end: short = blunt ends, long = finely tapered.")]
    [Length][Range(0.025f, 0.5f)] public float endTaperLength = 0.3f;
    [Tooltip("How fast stems thicken (sim seconds; fast at first, then slowing down). " +
             "New tissue at the tip starts at radius 0, so this also sets how long and pointed growing tips look.")]
    [Range(1f, 60f)] public float radialGrowthTime = 8f;

    [Header("Spawn nodes (leaf or branch)")]
    [Tooltip("Bare section at the start of every branch: the first leaf/branch can appear after this fraction of the branch's maximum length.")]
    [ClassicOnly][Range(0f, 0.5f)] public float spawnStartFraction = 0.1f;
    [Tooltip("Distance along a branch between two possible leaf/branch positions. Smaller = denser plant " +
             "(and many more organs: the main driver of how many leaves a plant gets).")]
    [ClassicOnly][Length][Range(0.01f, 0.1f)] public float spawnCheckInterval = 0.04f;
    [Tooltip("Probability that something (leaf or branch) appears at each possible position.")]
    [ClassicOnly][Range(0f, 1f)] public float spawnProbability = 0.8f;
    [Tooltip("Leaf probability at a branch's end: toward the end it rises from spawnProbability to this value " +
             "(the extra spawns are always leaves). Values below spawnProbability have no effect.")]
    [ClassicOnly][Range(0f, 1f)] public float leafProbabilityAtEnd = 0.4f;
    [Tooltip("Irregularity of the leaf spiral: std (degrees) of the noise added to the golden angle between successive leaves/branches.")]
    [ClassicOnly][Range(0f, 45f)] public float phyllotaxisNoiseDeg = 10f;

    [Header("Branching")]
    [Tooltip("If something appears: probability that it's a branch rather than a leaf.")]
    [ClassicOnly][Range(0f, 1f)] public float branchProbability = 0.2f;
    [Tooltip("No side branches below this fraction of a branch's maximum length (a leaf appears instead).")]
    [ClassicOnly][Range(0f, 0.5f)] public float branchStartFraction = 0.2f;
    [Tooltip("Maximum branching level (0 = main stem only, 1 = side branches, 2 = their side branches, ...). " +
             "The number of branches multiplies with every level: keep it low with dense spawning.")]
    [ClassicOnly][Range(0, 6)] public int maxDepth = 3;
    [Tooltip("Angle of a new branch toward its parent's growth direction (0 = straight out, 90 = along the parent).")]
    [ClassicOnly][Range(0f, 90f)] public float branchElevationDeg = 33f;
    [Tooltip("Length of a side branch growing right at the plant base, as a fraction of the main stem's maxLength. " +
             "Side branches higher up are shorter (see lengthFalloff).")]
    [ClassicOnly][Range(0.1f, 1.5f)] public float sideBranchLengthRatio = 0.5f;
    [Tooltip("How much shorter side branches get toward the top, relative to the main stem's length: " +
             "0 = all equally long (column), 0.6 = the topmost still about 60% of the lowest (rounded crown), " +
             "1 = zero length right at the top (pointed crown). Same crown shape whatever maxLength is.")]
    [ClassicOnly][Range(0f, 1.5f)] public float lengthFalloff = 0.8f;

    [Header("Leaves")]
    [Tooltip("Leaf variants; each new leaf picks one at random (equal chance). Each prefab's origin must be the " +
             "leaf's attachment point, blade pointing along local +Z, upper leaf surface facing local +Y.")]
    public GameObject[] leafPrefabs;
    [Tooltip("Sim seconds until a leaf reaches its full size (fast at first, then slowing down).")]
    [Range(1f, 60f)] public float leafGrowthDuration = 5f;
    [Tooltip("Full size of a leaf at the plant base (scale factor on the prefab).")]
    [Length][Range(0.02f, 0.5f)] public float maxLeafSize = 0.125f;
    [Tooltip("How much smaller leaves get toward the top, relative to the main stem's length: " +
             "0 = all the same size, 0.5 = half size at the top (never below 30%). Same proportions whatever maxLength is.")]
    [Range(0f, 1f)] public float leafSizeFalloff = 0.5f;
    [Tooltip("Random size variation per leaf (relative std: 0.2 = typically +-20%).")]
    [Range(0f, 0.5f)] public float leafSizeNoise = 0.2f;
    [Tooltip("Angle of the blade relative to the plane perpendicular to the branch (0 = sticking straight out, " +
             "90 = pointing along the branch, negative = hanging down).")]
    [ClassicOnly][Range(-90f, 90f)] public float leafElevationDeg = -20f;

    [Header("Flowers")]
    [Tooltip("Flower variants; each flower picks one at random. Each prefab's origin must be the flower's base, " +
             "with the flower facing along local +Y. A FlowerMorph on the prefab root does the opening.")]
    public GameObject[] flowerPrefabs;
    [Tooltip("Probability that a branch tip gets a flower once that branch has finished growing (main stem included).")]
    [ClassicOnly][Range(0f, 1f)] public float flowerProbability = 0.3f;
    [Tooltip("Sim seconds between a branch finishing and its bud appearing.")]
    [ClassicOnly][Range(0f, 30f)] public float flowerDelay = 5f;
    [Tooltip("Sim seconds for a new bud to grow from nothing to its closed bud size. It stays closed until the Flowering phase.")]
    [Range(0.5f, 10f)] public float budGrowDuration = 3f;
    [Tooltip("Sim seconds from closed bud to fully open, counted from the start of the Flowering phase " +
             "(or from the bud's appearance, if that's later).")]
    [Range(1f, 30f)] public float flowerOpenDuration = 8f;
    [Tooltip("Full size of a flower (size multiplier passed to FlowerMorph, on top of the prefab's own scale).")]
    [Length][Range(0.05f, 1f)] public float maxFlowerSize = 0.5f;
    [Tooltip("Random size variation per flower (relative std: 0.1 = typically +-10%).")]
    [Range(0f, 0.5f)] public float flowerSizeNoise = 0.1f;

    [Header("Fruits")]
    [Tooltip("Probability that an open flower develops into a fruit when the Fruiting phase starts; the others wither and fall. " +
             "Flowers that never opened (Flowering skipped) don't set fruit.")]
    [Range(0f, 1f)] public float fruitSetProbability = 0.25f;
    [Tooltip("Sim seconds from the start of Fruiting until a fruit is ripe. If shorter than the phase, ripe fruits wait on the plant.")]
    [Range(1f, 60f)] public float fruitDevelopDuration = 6f;

    [Header("Limits (safety: nothing more of that kind appears once reached)")]
    [Tooltip("At most this many branches (main stem included). Branch counts multiply with every branching level.")]
    [NoRandomize][Range(10, 1000)] public int maxBranches = 200;
    [Tooltip("At most this many leaves. Every leaf is an object checked against obstacles every step: thousands make Unity slow.")]
    [NoRandomize][Range(50, 5000)] public int maxLeaves = 1500;
    [Tooltip("At most this many flowers/fruits.")]
    [NoRandomize][Range(5, 1000)] public int maxFlowers = 200;

    [Header("Collisions")]
    [Tooltip("Leaves and flowers keep out of the plant's own stems (except the one they grow on); leaves give way to " +
             "flowers, and flowers to the flowers that appeared before them. Stems don't avoid each other. " +
             "Costs performance with many leaves.")]
    [NoRandomize] public bool selfCollision = true;
    [Tooltip("How often per sim second leaves and flowers are checked against the plant itself (the simulation runs at 50). " +
             "Lower = faster; organs only move a few millimeters in between.")]
    [NoRandomize][Range(5f, 50f)] public float selfCollisionRate = 20f;
    [Tooltip("Gap a growing tip keeps from obstacles; it slides along them at this distance.")]
    [Length][NoRandomize][Range(0.001f, 0.02f)] public float obstacleClearance = 0.005f;
    [Tooltip("A leaf touching an obstacle first turns at its base (like a bending leaf stalk), by up to this angle (degrees) in total.")]
    [NoRandomize][Range(0f, 90f)] public float leafMaxTiltDeg = 45f;
    [Tooltip("A flower/fruit touching an obstacle first tilts away, by up to this angle (degrees) in total.")]
    [NoRandomize][Range(0f, 90f)] public float flowerMaxTiltDeg = 30f;
    [Tooltip("How fast a touching point may be moved away by bending or shifting (units per sim second).")]
    [Length][NoRandomize][Range(0.005f, 0.2f)] public float bendMoveSpeed = 0.025f;
    [Tooltip("Branch radius that still bends easily. Stiffness grows with (radius / this)^3: a node twice as thick " +
             "is 8x stiffer. Much thicker parts barely give way; the touching organ stops growing instead.")]
    [Length][NoRandomize][Range(0.001f, 0.05f)] public float bendFlexibleRadius = 0.005f;
    [Tooltip("Sim seconds for a bend or dent to become permanent (the branch 'remembers' it, like growth adapting). " +
             "Shorter = obstacles that move away leave a lasting shape.")]
    [NoRandomize][Range(1f, 120f)] public float bendSettleTime = 15f;

    [Header("Withering")]
    [Tooltip("Sim seconds for the withering wave to travel from the organ farthest from the base down to the base.")]
    [NoRandomize][Range(1f, 120f)] public float witherWaveDuration = 30f;
    [Tooltip("Std (sim seconds) of a random offset per leaf/flower, so the wave isn't perfectly regular.")]
    [NoRandomize][Range(0f, 10f)] public float witherJitter = 2f;
    [Tooltip("Sim seconds a leaf/flower/fruit discolors once the wave reaches it, before it falls off.")]
    [NoRandomize][Range(1f, 30f)] public float organWitherTime = 6f;
    [Tooltip("Sim seconds a piece of stem takes to discolor and thin once the wave reaches it.")]
    [NoRandomize][Range(1f, 60f)] public float stemWitherTime = 20f;
    [Tooltip("Radius of fully withered stems, as a fraction of their radius before withering.")]
    [NoRandomize][Range(0.3f, 1f)] public float stemWitheredRadiusFraction = 0.7f;
    [Tooltip("Color that withering leaves, flowers, fruits and stems turn into.")]
    public Color witheredColor = new Color(0.42f, 0.35f, 0.2f);
    [Tooltip("How far colors move toward witheredColor when fully withered (1 = completely, lower = a hint of the original color stays).")]
    [NoRandomize][Range(0f, 1f)] public float witherColorAmount = 0.85f;

    // --- fixed values (not settings) ---
    /// <summary>Maximum bend at a single node (degrees). If all nodes are at their limit, the touching organ stops growing.</summary>
    public const float MaxJointBendDeg = 35f;
    /// <summary>How fast a node may bend (degrees per sim second); organs may tilt 1.5x as fast.</summary>
    public const float BendSpeed = 15f;
    /// <summary>Sim seconds for a bend/dent to spring back once nothing pushes anymore (elastic part).</summary>
    public const float BendRelaxTime = 3f;
    /// <summary>Passes per step to resolve contacts (moving one part can make another touch).</summary>
    public const int ContactIterations = 4;
    /// <summary>Grid cells along an organ's longest side for its collision shape (prefabs can override it with OrganCollisionShape).</summary>
    public const int DefaultVoxelResolution = 10;
    /// <summary>Air resistance of falling leaves/flowers (higher = floatier) and their rotational air resistance (lower = more tumbling).</summary>
    public const float FallDamping = 4f, FallAngularDamping = 1f;

    /// <summary>A color after withering to a given degree (0 = fresh, 1 = fully withered): moves toward witheredColor.</summary>
    public Color WitherColor(Color fresh, float w)
    {
        if (w <= 0f) return fresh;
        Color c = Color.Lerp(fresh, witheredColor, w * witherColorAmount);
        c.a = fresh.a;
        return c;
    }
    // --- rescaling the whole plant ---

    /// <summary>
    /// Scales the plant by a factor while keeping its shape and behavior: every [Length] parameter x factor,
    /// every [PerLength] rate / factor, every [PerSqrtLength] wobble / sqrt(factor). Fractions, probabilities,
    /// angles and durations stay. (SegmentLength is a code constant: change it by the same factor for an exact match.)
    /// </summary>
    public void RescaleSize(float factor)
    {
        foreach (var field in typeof(PlantSettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (field.FieldType != typeof(float)) continue;
            float v = (float)field.GetValue(this);
            if (System.Attribute.IsDefined(field, typeof(LengthAttribute))) field.SetValue(this, v * factor);
            else if (System.Attribute.IsDefined(field, typeof(PerLengthAttribute))) field.SetValue(this, v / factor);
            else if (System.Attribute.IsDefined(field, typeof(PerSqrtLengthAttribute))) field.SetValue(this, v / Mathf.Sqrt(factor));
        }
        Version++;
    }

    //rescaling in the editor: see the "Rescale plant size" box at the bottom of this asset's Inspector (Editor/PlantSettingsEditor.cs)

    /// <summary>Increases whenever a value is changed in the Inspector, so plants know to redraw.</summary>
    public int Version { get; private set; }
    void OnValidate() => Version++;

    // --- formulas shared by simulation and visuals (single source of truth) ---

    /// <summary>Maximum length of a side branch that starts at distance d from the plant base.</summary>
    public float SideBranchLength(float distFromBase) =>
        sideBranchLengthRatio * maxLength * Mathf.Sqrt(Mathf.Max(0f, 1f - lengthFalloff * Relative(distFromBase)));

    /// <summary>Radius a point eventually reaches, given its distance from the plant base and its branching level.</summary>
    public float MaxRadiusAt(float distFromBase, int depth) =>
        maxRadius * Mathf.Pow(radiusDepthFactor, depth)
                  * Mathf.Max(MinRadiusFraction, Mathf.Exp(-radiusFalloff * Relative(distFromBase)));

    /// <summary>
    /// A distance from the plant base as a fraction of the main stem's maxLength (1 = the height of the main stem's tip,
    /// measured along the plant). All falloffs use this, so the plant's proportions don't change with maxLength.
    /// </summary>
    public float Relative(float distFromBase) => distFromBase / Mathf.Max(1e-4f, maxLength);

    /// <summary>Light attraction of a branch at a given branching level.</summary>
    public float DirectionBiasAt(int depth) => directionBias * Mathf.Max(0f, 1f - DirectionBiasFalloffPerDepth * depth);

    /// <summary>Light attraction drops by this fraction per branching level (side branches react less). Not a setting.</summary>
    public const float DirectionBiasFalloffPerDepth = 0.2f;
}
