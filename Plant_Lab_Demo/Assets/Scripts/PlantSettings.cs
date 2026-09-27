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
    [Range(0.005f, 0.5f)] public float deltaT = 0.05f;//step size for things to evolve

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
    [Tooltip("Time constant (sim seconds) of thickening: fast at first, then slowing down. " +
             "Fresh tissue at the tip starts at radius 0, which makes the tips pointy.")]
    [Range(0.1f, 120f)] public float radialGrowthTime = 10f;
    [Range(3, 24)] public int radialSegments = 8;

    [Header("Spawn nodes (leaf or branch)")]
    [Tooltip("First spawn check at this fraction of the branch's maximum length (from its own base).")]
    [Range(0f, 1f)] public float spawnStartFraction = 0.05f;
    [Tooltip("Fresh tissue formed between two spawn checks; the checks move apart as the segments elongate.")]
    [Range(0.01f, 0.5f)] public float spawnCheckInterval = 0.05f;
    [Tooltip("Probability that anything (leaf or branch) spawns at a check.")]
    [Range(0f, 1f)] public float spawnProbability = 0.1f;
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
