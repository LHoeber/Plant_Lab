using UnityEngine;

/// <summary>
/// One root system type as an asset, plugged into a plant via PlantSettings.roots (several plants can share one).
/// Roots are branches (same growth, segments and tube mesh as the shoot) that grow only in the Growing phase,
/// stay rigid (no bending), and are steered by gravity instead of light: in substrate (SoilVolume) they turn down
/// gradually, in open air they fall almost straight down. Container walls: they slide along them like shoot tips do.
/// Create: Assets > Create > Botanic Lab > Root Settings.
/// </summary>
[CreateAssetMenu(fileName = "RootSettings", menuName = "Botanic Lab/Root Settings")]
public class RootSettings : ScriptableObject
{
    [Header("Main roots (start at the plant base)")]
    [Tooltip("Number of main roots, spread evenly around the base (golden angle).")]
    [Range(0, 8)] public int mainRootCount = 5;
    [Tooltip("Length a main root grows to (m).")]
    [Range(0.02f, 2f)] public float mainRootLength = 0.5f;
    [Tooltip("Start direction of the main roots: degrees below horizontal (0 = sideways, 90 = straight down).")]
    [Range(0f, 90f)] public float startAngleDeg = 35f;
    [Tooltip("Random variation of each main root's start direction (degrees).")]
    [Range(0f, 45f)] public float startAngleNoiseDeg = 10f;
    [Tooltip("How far below the plant origin the roots start (m), so they don't poke out of the stem base.")]
    [Range(0f, 0.1f)] public float startDepth = 0.01f;

    [Header("Side roots")]
    [Tooltip("Levels of side roots: 0 = only main roots, 1 = side roots on main roots, 2 = also side roots on those.")]
    [Range(0, 2)] public int sideRootLevels = 1;
    [Tooltip("Distance along a root between two chances for a side root (m).")]
    [Range(0.01f, 0.2f)] public float sideRootSpacing = 0.05f;
    [Tooltip("Probability of a side root at each chance.")]
    [Range(0f, 1f)] public float sideRootProbability = 0.6f;
    [Tooltip("No side roots on the first part of a root (fraction of its length).")]
    [Range(0f, 0.9f)] public float sideRootStartFraction = 0.15f;
    [Tooltip("Angle between a side root and its parent root (degrees).")]
    [Range(10f, 90f)] public float sideRootAngleDeg = 65f;
    [Tooltip("Length of a side root as a fraction of its parent's length (per level).")]
    [Range(0.05f, 1f)] public float sideRootLengthFraction = 0.4f;

    [Header("Growth and direction")]
    [Tooltip("How fast root tips grow (m per sim second).")]
    [Range(0.005f, 0.5f)] public float growthSpeed = 0.08f;
    [Tooltip("In substrate: how strongly roots turn downward (per m of growth; like the shoot's directionBias toward light). " +
             "Side roots use a fraction of it per level (gravitropismSideFactor).")]
    [Range(0f, 20f)] public float gravitropism = 2.5f;
    [Tooltip("Side roots: gravitropism x this per level (side roots grow more sideways than main roots).")]
    [Range(0f, 1f)] public float gravitropismSideFactor = 0.3f;
    [Tooltip("In open air (outside any substrate, nothing touched): how strongly roots turn downward (per m). High = they hang.")]
    [Range(0f, 200f)] public float airGravitropism = 60f;
    [Tooltip("Random wobble of the growth direction (per sqrt(m), like the shoot's perturbationStd).")]
    [Range(0f, 1.5f)] public float perturbationStd = 0.5f;

    [Header("Spreading (root density)")]
    [Tooltip("Root density (m of root per liter) at which roots stop growing. Growth slows down on the way there. " +
             "For scale: one root through a 4 cm grid cell is about 0.6 m/l there.")]
    [Range(0.1f, 20f)] public float maxDensity = 2f;
    [Tooltip("How late growth slows down: speed x (1 - (density / maxDensity)^this). 1 = steadily, high = only close to the maximum.")]
    [Range(0.5f, 8f)] public float slowdownExponent = 2f;
    [Tooltip("In substrate: how strongly roots turn away from denser regions (toward emptier ones). " +
             "Weakened by the substrate's resistance (SoilVolume): x (1 - resistance).")]
    [Range(0f, 5f)] public float spreadStrength = 1f;
    [Tooltip("Grid spacing of the density field (m). Smaller = finer, but a single root then counts as denser.")]
    [Range(0.01f, 0.2f)] public float densityCellSize = 0.04f;

    [Header("Thickness and look")]
    [Tooltip("Radius of a main root at its base when fully thickened (m).")]
    [Range(0.001f, 0.05f)] public float baseRadius = 0.008f;
    [Tooltip("Radius at the root's end as a fraction of its base radius.")]
    [Range(0.05f, 1f)] public float tipRadiusFraction = 0.25f;
    [Tooltip("Side roots: radius x this per level.")]
    [Range(0.1f, 1f)] public float sideRootRadiusFactor = 0.5f;
    [Tooltip("Sim seconds for new root tissue to thicken (fast at first, then slowing down).")]
    [Range(0.5f, 60f)] public float thickenTime = 6f;
    [Tooltip("Material of the root tubes. Empty: the plant's stem material.")]
    public Material material;

    [Header("Limits (performance)")]
    [Tooltip("At most this many roots in total (main + side roots).")]
    [Range(1, 300)] public int maxRoots = 60;
}
