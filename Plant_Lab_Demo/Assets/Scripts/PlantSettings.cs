using UnityEngine;

/// <summary>
/// All parameters that define how a plant grows, stored as an asset file (ScriptableObject).
/// - Changes made during Play mode are kept (unlike values on a component).
/// - Several assets = several presets (e.g. one per experimental condition), versioned in Git.
/// Create one via: Project window -> right-click -> Create -> Plant -> Plant Settings.
/// </summary>
[CreateAssetMenu(fileName = "PlantSettings", menuName = "Plant/Plant Settings")]
public class PlantSettings : ScriptableObject
{
    public enum FalloffShape { Linear, Sqrt, Exponential }

    [Header("Simulation")]
    [Tooltip("Fixed simulation time step, in sim seconds.")]
    [NoRandomize][Range(0.005f, 0.5f)] public float deltaT = 0.05f;//step size for things to evolve

    [Header("Phases (fixed order: Growing -> Flowering -> Fruiting -> Withering)")]
    [Tooltip("Growing ends when all branches have finished, or after this many sim seconds at the latest " +
             "(unfinished branches then stop where they are and get no bud).")]
    [Range(1f, 600f)] public float growingMaxDuration = 120f;
    [Tooltip("Flowering phase: the buds that exist open. Off = skipped, buds wither unopened.")]
    public bool enableFlowering = true;
    [Range(1f, 600f)] public float floweringDuration = 30f;
    [Tooltip("Fruiting phase: open flowers develop into fruit (with fruitSetProbability), the others wither and fall. Off = skipped.")]
    public bool enableFruiting = false;
    [Range(1f, 600f)] public float fruitingDuration = 30f;

    [Header("Growth")]
    [Tooltip("Fresh tissue formed at each branch tip, units per sim second (same for all branches). " +
             "Visible growth is faster, because the segments behind the tip keep elongating.")]
    [Range(0f, 2f)] public float growthSpeed = 0.2f;
    [Tooltip("Fresh length of one segment when it's committed (before it starts elongating).")]
    [Range(0.02f, 0.5f)] public float segmentLength = 0.1f;
    [Tooltip("Final length of a segment relative to its fresh length (1 = no elongation, 3 = three times as long).")]
    [Range(1f, 5f)] public float segmentStretchFactor = 2f;
    [Tooltip("Time constant (sim seconds) of segment elongation: after this time 63% of the stretching is done, " +
             "after 3x this time 95%. Fast at first, then slowing down.")]
    [Range(0.1f, 60f)] public float elongationTime = 5f;
    [Tooltip("Maximum length of the main stem, fully elongated.")]
    [Range(0.1f, 10f)] public float maxLength = 3f;

    [Header("Direction")]
    [Tooltip("Turning rate toward the target, per unit of stem length. The remaining angle to the target " +
             "shrinks by a factor exp(-bias * distance), independent of segmentLength.")]
    [Range(0f, 10f)] public float directionBias = 1f;
    [Tooltip("Linear drop of the light attraction per branching level: bias = directionBias * max(0, 1 - slope * depth).")]
    [Range(0f, 1f)] public float directionBiasFalloffPerDepth = 0.3f;
    [Tooltip("Direction noise per sqrt(unit of stem length). Per-segment std = perturbationStd * sqrt(segmentLength), " +
             "so the wobble per unit length is independent of segmentLength (random walk of the direction).")]
    [Range(0f, 2f)] public float perturbationStd = 0.5f;

    [Header("Thickness")]
    [Tooltip("Maximum radius at the base of the main stem. Each point thickens toward its own maximum, " +
             "which gets smaller with distance from the base and with every branching level.")]
    [Range(0.001f, 0.3f)] public float maxRadius = 0.04f;
    [Tooltip("Maximum radius factor per branching level (0.6: side branch 60%, next level 36%, ...).")]
    [Range(0.1f, 1f)] public float radiusDepthFactor = 0.6f;
    [Tooltip("Shape of the maximum-radius decrease with distance d from the plant base (fully elongated): " +
             "Linear 1-k*d, Sqrt sqrt(1-k*d), Exponential exp(-k*d).")]
    public FalloffShape radiusFalloffType = FalloffShape.Exponential;
    [Tooltip("Falloff rate k per unit of distance from the plant base.")]
    [Range(0f, 5f)] public float radiusFalloff = 0.3f;
    [Tooltip("The maximum radius never drops below this fraction of maxRadius (times the level factor).")]
    [Range(0f, 1f)] public float minRadiusFraction = 0.1f;
    [Tooltip("Radius at a branch's very end, as a fraction of what it would be without the end taper.")]
    [Range(0f, 1f)] public float endRadiusFraction = 0.2f;
    [Tooltip("Length scale (fully elongated) of the taper toward each branch's end: " +
             "radius factor = f + (1-f) * (1 - exp(-remaining / endTaperLength)).")]
    [Range(0.01f, 2f)] public float endTaperLength = 0.3f;
    [Tooltip("Time constant (sim seconds) of thickening: fast at first, then slowing down. " +
             "Fresh tissue at the tip starts at radius 0, which makes the tips pointy.")]
    [Range(0.1f, 120f)] public float radialGrowthTime = 10f;
    [NoRandomize][Range(3, 24)] public int radialSegments = 8;//rendering detail, not plant shape

    [Header("Spawn nodes (leaf or branch)")]
    [Tooltip("First spawn check at this fraction of the branch's maximum length (from its own base).")]
    [Range(0f, 1f)] public float spawnStartFraction = 0.05f;
    [Tooltip("Fresh tissue formed between two spawn checks; the checks move apart as the segments elongate.")]
    [Range(0.01f, 0.5f)] public float spawnCheckInterval = 0.05f;
    [Tooltip("Probability that anything (leaf or branch) spawns at a check.")]
    [Range(0f, 1f)] public float spawnProbability = 0.1f;
    [Tooltip("Leaf probability at a branch's end. Between start and end it rises from spawnProbability to this value; " +
             "the extra spawns are always leaves (branches are unaffected). Values below spawnProbability have no effect.")]
    [Range(0f, 1f)] public float leafProbabilityAtEnd = 0.3f;
    [Tooltip("Shape of that rise along the branch: 1 = linear, 2 = mostly near the end, 0.5 = already early on.")]
    [Range(0.1f, 5f)] public float leafEndBoostExponent = 2f;
    [Tooltip("Std (degrees) of the noise added to the golden angle between successive leaves/branches.")]
    [Range(0f, 90f)] public float phyllotaxisNoiseDeg = 10f;

    [Header("Branching")]
    [Tooltip("If something spawns: probability that it's a branch rather than a leaf.")]
    [Range(0f, 1f)] public float branchProbability = 0.3f;
    [Tooltip("No side branches below this fraction of the branch's maximum length (a leaf spawns instead).")]
    [Range(0f, 1f)] public float branchStartFraction = 0.1f;
    [Tooltip("Maximum branching level (0 = main stem only, 1 = side branches, 2 = side branches of side branches, ...).")]
    [Range(0, 6)] public int maxDepth = 2;
    [Tooltip("Angle of a new branch toward its parent's growth direction (0 = straight out, 90 = along the parent).")]
    [Range(0f, 90f)] public float branchElevationDeg = 45f;
    [Tooltip("Maximum length of a side branch starting right at the plant base; decreases with distance from the base.")]
    [Range(0.05f, 5f)] public float sideBranchMaxLength = 1f;
    [Tooltip("Shape of the length decrease with distance d from the plant base (fully elongated): Linear 1-k*d, " +
             "Sqrt sqrt(1-k*d) (stays long, then drops), Exponential exp(-k*d) (drops quickly).")]
    public FalloffShape lengthFalloffType = FalloffShape.Sqrt;
    [Tooltip("Falloff rate k per unit of distance from the plant base.")]
    [Range(0f, 5f)] public float lengthFalloff = 0.3f;
    [Tooltip("A branch whose maximum length would be shorter than this isn't created (a leaf spawns instead).")]
    [Range(0f, 1f)] public float minBranchLength = 0.05f;

    [Header("Leaves")]
    [Tooltip("Leaf variants; each new leaf picks one at random (equal chance). Each prefab's origin must be the " +
             "leaf's attachment point, blade pointing along local +Z, upper leaf surface facing local +Y.")]
    public GameObject[] leafPrefabs;
    [Tooltip("Sim seconds until a leaf reaches its full size.")]
    [Range(0.1f, 60f)] public float leafGrowthDuration = 5f;
    [Tooltip("Growth curve exponent: size ~ (age/duration)^exponent. 0.5 = sqrt (fast start, slowing down), 1 = linear.")]
    [Range(0.1f, 2f)] public float leafGrowthExponent = 0.5f;
    [Tooltip("Full size of a leaf at the plant base (scale factor on the prefab).")]
    [Range(0.01f, 2f)] public float maxLeafSize = 0.3f;
    [Tooltip("Fraction of maxLeafSize lost per unit of distance from the plant base (linear decrease).")]
    [Range(0f, 2f)] public float leafSizeFalloff = 0.2f;
    [Tooltip("Leaves never get smaller than this fraction of maxLeafSize because of the falloff.")]
    [Range(0f, 1f)] public float minLeafSizeFraction = 0.3f;
    [Tooltip("Relative std of the random per-leaf size factor (0.15 = +-15%).")]
    [Range(0f, 0.5f)] public float leafSizeNoise = 0.15f;
    [Tooltip("Angle of the blade above the plane perpendicular to the branch (0 = sticking straight out, 90 = along the branch).")]
    [Range(-45f, 90f)] public float leafElevationDeg = 35f;

    [Header("Flowers")]
    [Tooltip("Flower variants; each flower picks one at random. Each prefab's origin must be the flower's base, " +
             "with the flower facing along local +Y. A FlowerMorph on the prefab root does the opening.")]
    public GameObject[] flowerPrefabs;
    [Tooltip("Probability that a branch tip gets a flower once that branch has finished growing (main stem included).")]
    [Range(0f, 1f)] public float flowerProbability = 0.7f;
    [Tooltip("Sim seconds between a branch finishing and its bud appearing.")]
    [Range(0f, 60f)] public float flowerDelay = 2f;
    [Tooltip("Sim seconds for a new bud to grow from nothing to its closed bud size. It stays closed until the Flowering phase.")]
    [Range(0.1f, 30f)] public float budGrowDuration = 3f;
    [Tooltip("Sim seconds from closed bud to fully open, counted from the start of the Flowering phase " +
             "(or from the bud's appearance, if that's later).")]
    [Range(0.1f, 60f)] public float flowerOpenDuration = 8f;
    [Tooltip("Shape of the opening over time: growth = (age/duration)^exponent. 1 = even, 0.5 = fast at first.")]
    [Range(0.1f, 3f)] public float flowerOpenExponent = 1f;
    [Tooltip("Full size of a flower (size multiplier passed to FlowerMorph, on top of the prefab's own scale).")]
    [Range(0.01f, 5f)] public float maxFlowerSize = 1f;
    [Tooltip("Relative std of the random per-flower size factor (0.1 = +-10%).")]
    [Range(0f, 0.5f)] public float flowerSizeNoise = 0.1f;

    [Header("Fruits")]
    [Tooltip("Probability that an open flower develops into a fruit when the Fruiting phase starts; the others wither and fall. " +
             "Flowers that never opened (Flowering skipped) don't set fruit.")]
    [Range(0f, 1f)] public float fruitSetProbability = 0.6f;
    [Tooltip("Sim seconds from the start of Fruiting until a fruit is ripe. If shorter than the phase, ripe fruits wait on the plant.")]
    [Range(0.1f, 120f)] public float fruitDevelopDuration = 15f;
    [Tooltip("Shape of fruit development over time: progress = (age/duration)^exponent. 1 = even, 0.5 = fast at first.")]
    [Range(0.1f, 3f)] public float fruitDevelopExponent = 1f;

    [Header("Collisions")]
    [Tooltip("Gap (in units) a growing tip keeps from obstacles; it slides along them at this distance.")]
    [Range(0.001f, 0.1f)] public float obstacleClearance = 0.01f;
    [Tooltip("A leaf touching an obstacle first turns at its base (like a bending leaf stalk), by up to this angle (degrees) in total.")]
    [Range(0f, 90f)] public float leafMaxTiltDeg = 45f;
    [Tooltip("A flower/fruit touching an obstacle first tilts away, by up to this angle (degrees) in total.")]
    [Range(0f, 90f)] public float flowerMaxTiltDeg = 30f;
    [Tooltip("Maximum bend at a single node (degrees). If all nodes are at their limit, the touching organ stops growing.")]
    [Range(1f, 90f)] public float maxJointBendDeg = 35f;
    [Tooltip("How fast a node may bend (degrees per sim second). Organs may tilt 3x as fast. If that can't keep up " +
             "with growth, the touching part stops growing instead of passing through.")]
    [Range(0.5f, 90f)] public float bendSpeed = 15f;
    [Tooltip("Sim seconds for a bend to spring back halfway once nothing pushes anymore (elastic part).")]
    [Range(0.1f, 60f)] public float bendRelaxTime = 3f;
    [Tooltip("Sim seconds for a bend to become permanent (the branch 'remembers' it, like growth adapting).")]
    [Range(0.1f, 300f)] public float bendSettleTime = 15f;
    [Tooltip("Passes per step to resolve contacts (moving one part can make another touch).")]
    [NoRandomize][Range(1, 10)] public int contactIterations = 4;
    [Tooltip("Grid cells along an organ's longest side for its collision shape, for prefabs without an OrganCollisionShape component.")]
    [NoRandomize][Range(2, 32)] public int defaultVoxelResolution = 10;

    [Header("Withering")]
    [Tooltip("Sim seconds for the withering wave to travel from the organ farthest from the base down to the base.")]
    [Range(1f, 300f)] public float witherWaveDuration = 30f;
    [Tooltip("Std (sim seconds) of a random offset per leaf/flower, so the wave isn't perfectly regular.")]
    [Range(0f, 20f)] public float witherJitter = 2f;
    [Tooltip("Sim seconds a leaf discolors once the wave reaches it, before it falls off.")]
    [Range(0.1f, 60f)] public float leafWitherTime = 6f;
    [Tooltip("Sim seconds a flower discolors once the wave reaches it, before it falls off.")]
    [Range(0.1f, 60f)] public float flowerWitherTime = 6f;
    [Tooltip("Sim seconds a piece of stem takes to discolor and thin once the wave reaches it.")]
    [Range(0.1f, 120f)] public float stemWitherTime = 20f;
    [Tooltip("Radius of fully withered stems, as a fraction of their radius before withering.")]
    [Range(0.1f, 1f)] public float stemWitheredRadiusFraction = 0.7f;
    [Tooltip("Saturation kept when fully withered (0 = grey, 1 = unchanged).")]
    [Range(0f, 1f)] public float witheredSaturation = 0.35f;
    [Tooltip("Hue that withered colors shift toward (0.08 = brownish orange, 0.15 = yellow).")]
    [Range(0f, 1f)] public float witheredHue = 0.08f;
    [Tooltip("How far the hue shifts toward witheredHue (0 = not at all, 1 = completely).")]
    [Range(0f, 1f)] public float witherHueShift = 0.7f;
    [Tooltip("Brightness kept when fully withered.")]
    [Range(0f, 1f)] public float witheredBrightness = 0.6f;
    [Tooltip("Air resistance of falling leaves/flowers: higher = slower, floatier fall.")]
    [Range(0f, 20f)] public float fallDamping = 4f;
    [Tooltip("Rotational air resistance of falling leaves/flowers: lower = more tumbling.")]
    [Range(0f, 20f)] public float fallAngularDamping = 1f;

    /// <summary>A color after withering to a given degree (0 = fresh, 1 = fully withered): less saturated, browner, darker.</summary>
    public Color WitherColor(Color fresh, float w)
    {
        if (w <= 0f) return fresh;
        Color.RGBToHSV(fresh, out float h, out float sat, out float v);
        float dh = Mathf.Repeat(witheredHue - h + 0.5f, 1f) - 0.5f;//shortest way around the hue circle
        h = Mathf.Repeat(h + dh * witherHueShift * w, 1f);
        sat *= Mathf.Lerp(1f, witheredSaturation, w);
        v *= Mathf.Lerp(1f, witheredBrightness, w);
        Color c = Color.HSVToRGB(h, sat, v);
        c.a = fresh.a;
        return c;
    }

    /// <summary>Increases whenever a value is changed in the Inspector, so plants know to redraw.</summary>
    public int Version { get; private set; }
    void OnValidate() => Version++;

    // --- formulas shared by simulation and visuals (single source of truth) ---

    /// <summary>Falloff curve from 1 at d = 0: Linear 1-k*d, Sqrt sqrt(1-k*d) (both clamped at 0), Exponential exp(-k*d).</summary>
    public static float Falloff(FalloffShape shape, float k, float d)
    {
        float x = 1f - k * d;
        return shape switch
        {
            FalloffShape.Linear => Mathf.Max(0f, x),
            FalloffShape.Sqrt => Mathf.Sqrt(Mathf.Max(0f, x)),
            _ => Mathf.Exp(-k * d),
        };
    }

    /// <summary>Maximum length of a side branch that starts at distance d from the plant base.</summary>
    public float SideBranchLength(float distFromBase) =>
        sideBranchMaxLength * Falloff(lengthFalloffType, lengthFalloff, distFromBase);

    /// <summary>Radius a point eventually reaches, given its distance from the plant base and its branching level.</summary>
    public float MaxRadiusAt(float distFromBase, int depth) =>
        maxRadius * Mathf.Pow(radiusDepthFactor, depth)
                  * Mathf.Max(minRadiusFraction, Falloff(radiusFalloffType, radiusFalloff, distFromBase));

    /// <summary>Light attraction of a branch at a given branching level.</summary>
    public float DirectionBiasAt(int depth) => directionBias * Mathf.Max(0f, 1f - directionBiasFalloffPerDepth * depth);
}
