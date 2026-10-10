using UnityEngine;

/// <summary>
/// Optional, on the root of a leaf or flower prefab: how finely its collision shape is computed.
/// Prefabs without it use PlantSettings.DefaultVoxelResolution (10).
/// </summary>
public class OrganCollisionShape : MonoBehaviour
{
    [Tooltip("Number of grid cells along the organ's longest side. Higher = closer to the real shape, but more boxes to check.")]
    [Range(2, 32)] public int resolution = 10;
}
