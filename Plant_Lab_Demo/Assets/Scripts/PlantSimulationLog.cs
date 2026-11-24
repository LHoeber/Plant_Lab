using UnityEngine;

/// <summary>
/// Summaries of the simulation state for logging: a few readable numbers plus an exact fingerprint (hash).
/// </summary>
public partial class PlantSimulation
{
    /// <summary>Simulation steps done since the start (SimTime / DeltaT).</summary>
    public int StepCount => Mathf.RoundToInt(SimTime / PlantSettings.DeltaT);

    /// <summary>Highest point of the shoot (plant-local y of the highest branch node), m.</summary>
    public float Height()
    {
        float h = 0f;
        foreach (Branch b in Branches)
            foreach (Vector3 n in b.nodes) if (n.y > h) h = n.y;
        return h;
    }

    /// <summary>Total length of all shoot branches, m.</summary>
    public float ShootLength()
    {
        float sum = 0f;
        foreach (Branch b in Branches) sum += b.TotalLength;
        return sum;
    }

    /// <summary>Total length of all roots, m.</summary>
    public float RootLength()
    {
        float sum = 0f;
        foreach (Branch b in Roots) sum += b.TotalLength;
        return sum;
    }

    /// <summary>
    /// Exact fingerprint of the plant: phase, sim time, every node position and radius (shoot and roots), every
    /// leaf/flower's current size and wither, and every cactus segment's pose and growth.
    /// </summary>
    public ulong Hash()
    {
        ulong h = StateHash.Start;
        StateHash.Add(ref h, (int)Phase);
        StateHash.Add(ref h, SimTime);
        foreach (var list in new[] { Branches, Roots })
            foreach (Branch b in list)
            {
                StateHash.Add(ref h, b.nodes.Count);
                for (int k = 0; k < b.nodes.Count; k++) { StateHash.Add(ref h, b.nodes[k]); StateHash.Add(ref h, b.nodeRadius[k]); }
                StateHash.Add(ref h, b.tipSegLen);
            }
        StateHash.Add(ref h, Leaves.Count);
        foreach (Leaf leaf in Leaves) { StateHash.Add(ref h, LeafSize(leaf)); StateHash.Add(ref h, LeafWither(leaf)); }
        StateHash.Add(ref h, Flowers.Count);
        foreach (Flower f in Flowers) { StateHash.Add(ref h, FlowerSize(f)); StateHash.Add(ref h, FlowerWither(f)); }
        StateHash.Add(ref h, CactusSegments.Count);
        foreach (CactusSegment c in CactusSegments)
        {
            StateHash.Add(ref h, c.position);
            StateHash.Add(ref h, c.rotation.x); StateHash.Add(ref h, c.rotation.y); StateHash.Add(ref h, c.rotation.z); StateHash.Add(ref h, c.rotation.w);
            StateHash.Add(ref h, c.growth);
        }
        return h;
    }
}
