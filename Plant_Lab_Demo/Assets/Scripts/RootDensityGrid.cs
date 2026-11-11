using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How much root there is where: root length per volume (m of root per liter), as a smooth field.
/// Every root piece adds its length to the 8 grid points around its middle (weighted by closeness), and a lookup
/// interpolates between the 8 grid points around the asked position. So adding and asking cost the same, however
/// many roots there are. Only grid points that got any root are stored (dictionary), so the grid has no bounds.
/// Plain C#, deterministic: same additions in the same order = same field.
/// Per plant for now (plant-local space); can later be shared by all plants in one container.
/// </summary>
public class RootDensityGrid
{
    public readonly float CellSize;
    readonly float cellLiters;
    readonly Dictionary<Vector3Int, float> points = new Dictionary<Vector3Int, float>();

    public RootDensityGrid(float cellSize)
    {
        CellSize = Mathf.Max(1e-3f, cellSize);
        cellLiters = CellSize * CellSize * CellSize * 1000f;
    }

    /// <summary>Grid points with any root and their density (e.g. for drawing).</summary>
    public IEnumerable<KeyValuePair<Vector3Int, float>> Points => points;

    /// <summary>Position of a grid point.</summary>
    public Vector3 PointPosition(Vector3Int k) => (Vector3)k * CellSize;

    /// <summary>Adds a piece of root of the given length at a position.</summary>
    public void Add(Vector3 p, float length)
    {
        Vector3 f = p / CellSize;
        var i0 = new Vector3Int(Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.y), Mathf.FloorToInt(f.z));
        Vector3 t = f - (Vector3)i0;
        float amount = length / cellLiters;
        for (int c = 0; c < 8; c++)
        {
            int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
            float w = (dx == 1 ? t.x : 1f - t.x) * (dy == 1 ? t.y : 1f - t.y) * (dz == 1 ? t.z : 1f - t.z);
            if (w <= 0f) continue;
            var k = new Vector3Int(i0.x + dx, i0.y + dy, i0.z + dz);
            points.TryGetValue(k, out float v);
            points[k] = v + w * amount;
        }
    }

    /// <summary>Root density at a position (m of root per liter), interpolated between the grid points around it.</summary>
    public float Density(Vector3 p)
    {
        if (points.Count == 0) return 0f;
        Vector3 f = p / CellSize;
        var i0 = new Vector3Int(Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.y), Mathf.FloorToInt(f.z));
        Vector3 t = f - (Vector3)i0;
        float d = 0f;
        for (int c = 0; c < 8; c++)
        {
            int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
            if (!points.TryGetValue(new Vector3Int(i0.x + dx, i0.y + dy, i0.z + dz), out float v)) continue;
            d += v * (dx == 1 ? t.x : 1f - t.x) * (dy == 1 ? t.y : 1f - t.y) * (dz == 1 ? t.z : 1f - t.z);
        }
        return d;
    }

    /// <summary>Direction and steepness of the density increase (density per m), from differences half a cell to each side.</summary>
    public Vector3 Gradient(Vector3 p)
    {
        if (points.Count == 0) return Vector3.zero;
        float h = 0.5f * CellSize;
        return new Vector3(
            Density(p + new Vector3(h, 0f, 0f)) - Density(p - new Vector3(h, 0f, 0f)),
            Density(p + new Vector3(0f, h, 0f)) - Density(p - new Vector3(0f, h, 0f)),
            Density(p + new Vector3(0f, 0f, h)) - Density(p - new Vector3(0f, 0f, h))) / (2f * h);
    }
}
