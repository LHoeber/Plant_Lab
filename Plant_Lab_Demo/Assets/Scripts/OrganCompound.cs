using UnityEngine;

/// <summary>One box of an organ's collision shape, in the organ's own space at size 1.</summary>
public struct OrganBox
{
    public Vector3 center;
    public Vector3 half;   //half the box's size along each axis
}

/// <summary>
/// Collision shape of a leaf or flower variant: its mesh divided into grid cells, merged into a few boxes
/// (a "voxel compound"). Plain data for the simulation; built once on the Unity side by OrganVoxelizer.
/// </summary>
public class OrganCompound
{
    public OrganBox[] union;        //all morph states together (does it fit in every state it will ever take?)
    public string[] stateNames;     //"" = basis (e.g. closed), then one entry per blend shape (mid, open, fruit, ...)
    public OrganBox[][] perState;   //boxes for each state, same order as stateNames
    public Vector3 boundsCenter;    //sphere around everything, for a quick "anything near?" check
    public float boundsRadius;
    public float cellSize;
    public float rootScale = 1f;    //the prefab root's own scale, if the visuals keep it (flowers); 1 for leaves
    public MorphInfo morph;         //flowers: how growth maps to morph states and scale (copied from FlowerMorph); null for leaves

    /// <summary>Boxes of a given state ("" = basis), or the union if the state isn't known.</summary>
    public OrganBox[] BoxesFor(string state)
    {
        if (stateNames != null)
            for (int i = 0; i < stateNames.Length; i++)
                if (stateNames[i] == state) return perState[i];
        return union;
    }
}

/// <summary>
/// A copy of a FlowerMorph's settings, so the simulation can tell (without Unity) which morph state a flower
/// is in and how large it is, from the same progress values the visuals use.
/// </summary>
public class MorphInfo
{
    public string[] stageNames;
    public float[] stageReachedAt;
    public string openState, fruitState, wiltFlowerState, wiltFruitState;
    public float startScale = 1f, scaleExponent = 1f;

    float ReachedAt(string state, float fallback)
    {
        for (int i = 0; i < stageNames.Length; i++) if (stageNames[i] == state) return stageReachedAt[i];
        return fallback;
    }

    /// <summary>
    /// The state a flower currently resembles most ("" = basis) and its scale factor, from opening progress,
    /// fruit progress and withering (same rules as FlowerMorph).
    /// </summary>
    public string StateFor(float openProgress, float fruitProgress, float wither, out float scale)
    {
        float openAt = ReachedAt(openState, 1f);
        float fruitAt = string.IsNullOrEmpty(fruitState) ? openAt : ReachedAt(fruitState, openAt);
        float growth = Mathf.Clamp01(openProgress * openAt + fruitProgress * (fruitAt - openAt));
        float openness = openAt > 1e-6f ? Mathf.Clamp01(growth / openAt) : 1f;
        float fruitness = fruitAt - openAt > 1e-6f ? Mathf.Clamp01((growth - openAt) / (fruitAt - openAt)) : 0f;
        scale = Mathf.Lerp(startScale, 1f, Mathf.Pow(openness, scaleExponent));
        if (wither * openness > 0.5f) return fruitness > 0.5f ? wiltFruitState : wiltFlowerState;
        string best = "";
        float bestDist = growth;//distance to the basis (at 0)
        for (int i = 0; i < stageNames.Length; i++)
        {
            float d = Mathf.Abs(growth - stageReachedAt[i]);
            if (d < bestDist) { bestDist = d; best = stageNames[i]; }
        }
        return best;
    }
}
