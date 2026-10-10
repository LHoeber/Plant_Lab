using System.Collections.Generic;
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
/// Flowers have one compound per pose: their states and in-between steps (so the shape changes gradually).
/// </summary>
public class OrganCompound
{
    public OrganBox[] union;        //all poses together
    public OrganBox[][] poses;      //boxes per pose (leaves: just one, the basis)
    public Vector3 boundsCenter;    //sphere around everything, for a quick "anything near?" check
    public float boundsRadius;
    public float cellSize;
    public float rootScale = 1f;    //the prefab root's own scale, if the visuals keep it (flowers); 1 for leaves
    public MorphInfo morph;         //flowers: which pose belongs to which growth/withering state; null for leaves

    /// <summary>Boxes for a flower's current progress values (or the union, if it has no morph info).</summary>
    public OrganBox[] BoxesFor(float openProgress, float fruitProgress, float wither, out float morphScale)
    {
        morphScale = 1f;
        if (morph == null || poses == null) return union;
        int i = morph.PoseFor(openProgress, fruitProgress, wither, out morphScale);
        return i >= 0 && i < poses.Length ? poses[i] : union;
    }
}

/// <summary>
/// A copy of a FlowerMorph's settings, so the simulation can tell (without Unity) which pose a flower is in
/// and how large it is, from the same progress values the visuals use. Also generates the poses to voxelize.
/// </summary>
public class MorphInfo
{
    public string[] stageNames;
    public float[] stageReachedAt;
    public string openState, fruitState, wiltFlowerState, wiltFruitState;
    public float startScale = 1f, scaleExponent = 1f;
    public bool smooth = true;

    //pose table, filled by BuildPoses
    float[] chainGrowth;                         //growth value of each pose along bud -> open -> fruit (pose index = position)
    int wiltFlowerFirst = -1, wiltFruitFirst = -1;
    static readonly float[] WiltLevels = { 0.25f, 0.5f, 0.75f, 1f };
    static readonly float[] Steps = { 0.25f, 0.5f, 0.75f, 1f };

    float ReachedAt(string state, float fallback)
    {
        for (int i = 0; i < stageNames.Length; i++) if (stageNames[i] == state) return stageReachedAt[i];
        return fallback;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// The poses to voxelize, as blend shape weights (0..1, by name): the basis, then for every transition
    /// between neighboring states the steps at 25/50/75/100%, then the wilted states in four steps each.
    /// </summary>
    public List<Dictionary<string, float>> BuildPoses()
    {
        var poses = new List<Dictionary<string, float>> { new Dictionary<string, float>() };//basis
        var growth = new List<float> { 0f };
        string prev = null;
        float prevAt = 0f;
        for (int k = 0; k < stageNames.Length; k++)
        {
            foreach (float u in Steps)
            {
                float t = smooth ? Smooth(u) : u;//same easing as FlowerMorph
                var w = new Dictionary<string, float>();
                if (prev != null) w[prev] = 1f - t;
                w[stageNames[k]] = t;
                poses.Add(w);
                growth.Add(prevAt + u * (stageReachedAt[k] - prevAt));
            }
            prev = stageNames[k];
            prevAt = stageReachedAt[k];
        }
        chainGrowth = growth.ToArray();
        if (!string.IsNullOrEmpty(wiltFlowerState) && !string.IsNullOrEmpty(openState))
        {
            wiltFlowerFirst = poses.Count;
            foreach (float w in WiltLevels) poses.Add(new Dictionary<string, float> { [openState] = 1f - w, [wiltFlowerState] = w });
        }
        if (!string.IsNullOrEmpty(wiltFruitState) && !string.IsNullOrEmpty(fruitState))
        {
            wiltFruitFirst = poses.Count;
            foreach (float w in WiltLevels) poses.Add(new Dictionary<string, float> { [fruitState] = 1f - w, [wiltFruitState] = w });
        }
        return poses;
    }

    /// <summary>
    /// Index of the pose closest to a flower's current look, and its scale factor, from opening progress,
    /// fruit progress and withering (same rules as FlowerMorph).
    /// </summary>
    public int PoseFor(float openProgress, float fruitProgress, float wither, out float scale)
    {
        float openAt = ReachedAt(openState, 1f);
        float fruitAt = string.IsNullOrEmpty(fruitState) ? openAt : ReachedAt(fruitState, openAt);
        float growth = Mathf.Clamp01(openProgress * openAt + fruitProgress * (fruitAt - openAt));
        float openness = openAt > 1e-6f ? Mathf.Clamp01(growth / openAt) : 1f;
        float fruitness = fruitAt - openAt > 1e-6f ? Mathf.Clamp01((growth - openAt) / (fruitAt - openAt)) : 0f;
        scale = Mathf.Lerp(startScale, 1f, Mathf.Pow(openness, scaleExponent));

        float wilt = wither * openness;
        if (smooth) wilt = Smooth(wilt);
        int wiltFirst = fruitness > 0.5f ? wiltFruitFirst : wiltFlowerFirst;
        if (wilt > 0.125f && wiltFirst >= 0)
        {
            int best = 0;
            for (int j = 1; j < WiltLevels.Length; j++)
                if (Mathf.Abs(wilt - WiltLevels[j]) < Mathf.Abs(wilt - WiltLevels[best])) best = j;
            return wiltFirst + best;
        }
        if (chainGrowth == null) return 0;
        int nearest = 0;
        for (int i = 1; i < chainGrowth.Length; i++)
            if (Mathf.Abs(growth - chainGrowth[i]) < Mathf.Abs(growth - chainGrowth[nearest])) nearest = i;
        return nearest;
    }
}
