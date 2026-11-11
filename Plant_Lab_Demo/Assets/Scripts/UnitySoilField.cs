using UnityEngine;

/// <summary>
/// ISoilField backed by all active SoilVolume components in the scene.
/// Converts the plant's local space (simulation) to world space. Static meshes, so the answers are deterministic.
/// </summary>
public class UnitySoilField : ISoilField
{
    readonly Transform plant;

    public UnitySoilField(Transform plant) { this.plant = plant; }

    public bool InSoil(Vector3 p) => Containing(p) != null;

    public float Resistance(Vector3 p)
    {
        SoilVolume v = Containing(p);
        return v != null ? v.resistance : 0f;
    }

    SoilVolume Containing(Vector3 p)
    {
        if (SoilVolume.All.Count == 0) return null;
        Vector3 w = plant.TransformPoint(p);
        foreach (SoilVolume v in SoilVolume.All)
            if (v.ContainsWorld(w)) return v;
        return null;
    }
}
