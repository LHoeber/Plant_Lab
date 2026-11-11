using UnityEngine;

/// <summary>
/// Where the substrate is, as the simulation sees it. Everything is in plant-local space (like IObstacleField).
/// Keeps PlantSimulation independent of Unity: in the scene it's backed by SoilVolume components (UnitySoilField).
/// </summary>
public interface ISoilField
{
    /// <summary>Is this point inside a substrate volume?</summary>
    bool InSoil(Vector3 p);

    /// <summary>How strongly the substrate at this point resists root spreading (0 = none, also outside any substrate).</summary>
    float Resistance(Vector3 p);
}
