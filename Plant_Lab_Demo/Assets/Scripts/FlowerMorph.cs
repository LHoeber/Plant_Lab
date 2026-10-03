using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual morph of a flower: turns one growth value (0 = bud ... open ... 1 = last state, e.g. fruit) into
/// blend-shape weights on all its parts, plus an overall scale and material colors.
/// Goes on the root of the flower prefab. Knows nothing about the plant simulation:
/// for testing, move the growth slider in Play mode; on the plant, PlantVisuals calls SetGrowth/SetWither.
///
/// The blend shapes are treated as complete states in order (basis -> stage 1 -> stage 2 -> ...),
/// and the morph cross-fades between neighboring states.
/// </summary>
public class FlowerMorph : MonoBehaviour
{
    [System.Serializable]
    public class MorphStage
    {
        [Tooltip("Blend shape (shape key) name, exactly as in Blender.")]
        public string shapeName;
        [Tooltip("Growth value at which this state is fully reached. Must increase from stage to stage.")]
        [Range(0f, 1f)] public float reachedAt;
    }

    /// <summary>
    /// Replaces a Blender driver like "material color follows shape key": the material's color
    /// goes from colorAtZero to colorAtFull as the blend shape's weight goes from 0 to 100.
    /// </summary>
    [System.Serializable]
    public class ColorLink
    {
        [Tooltip("The Unity material whose color changes (the one assigned to the part, e.g. the fruit's material).")]
        public Material material;
        [Tooltip("Blend shape that drives the color, exactly as in Blender (e.g. 'fruit').")]
        public string shapeName = "fruit";
        public Color colorAtZero = Color.green;
        public Color colorAtFull = new Color(0.9f, 0.58f, 0f);
    }

    [Header("Morph")]
    [Tooltip("States after the basis (Blender's first key, e.g. 'closed'), in order.")]
    public List<MorphStage> stages = new List<MorphStage>
    {
        new MorphStage { shapeName = "mid", reachedAt = 0.33f },
        new MorphStage { shapeName = "open", reachedAt = 0.66f },
        new MorphStage { shapeName = "fruit", reachedAt = 1f },
    };
    [Tooltip("Ease in and out within each stage, so the motion doesn't jerk when passing through a state.")]
    public bool smoothStages = true;
    [Tooltip("Which state means 'fully open flower' (end of the Flowering phase on the plant).")]
    public string openStateName = "open";
    [Tooltip("Which state means 'ripe fruit' (end of fruit development on the plant). Leave empty if this flower has no fruit.")]
    public string fruitStateName = "fruit";

    [Header("Colors")]
    [Tooltip("Material colors driven by blend shapes (the Unity replacement for Blender drivers).")]
    public List<ColorLink> colorLinks = new List<ColorLink>();

    [Header("Size")]
    [Tooltip("Overall scale at growth 0, as a fraction of the full size (reached when fully open).")]
    [Range(0f, 1f)] public float startScale = 0.3f;
    [Tooltip("Shape of the scale increase: 1 = linear, 0.5 = fast at first, 2 = slow at first.")]
    [Range(0.1f, 3f)] public float scaleExponent = 1f;

    [Header("Current state (slider for testing in Play mode)")]
    [Range(0f, 1f)] public float growth = 0f;
    [Tooltip("Full size multiplier (on top of the prefab's own scale). Set by the plant.")]
    [Range(0.01f, 5f)] public float size = 1f;

    SkinnedMeshRenderer[] renderers;
    int[][] shapeIndex;          //[renderer][stage] -> blend shape index in that renderer's mesh, -1 if missing
    float[] stageWeight;         //current weight (0..1) of each stage, from the cross-fade
    Color[][] freshColors;       //[renderer][material slot] -> the material's own color
    Vector3 baseScale;           //the prefab's own scale
    float appliedGrowth = -1f, appliedSize = -1f, appliedWither = -1f;
    float wither;                //0 = fresh, 1 = fully withered (set by the plant)
    PlantSettings witherSettings;//how withering changes colors (null = no withering, e.g. in the isolated test)
    bool needsSetup = true;

    static MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        baseScale = transform.localScale;
        Setup();
        Apply();
    }

    /// <summary>Called by the plant every frame: sets and immediately shows the growth state.</summary>
    public void SetGrowth(float newGrowth, float newSize)
    {
        growth = Mathf.Clamp01(newGrowth);
        size = newSize;
        Apply();
    }

    /// <summary>Called by the plant: how withered the flower/fruit is. Applied on top of the (ripening) colors.</summary>
    public void SetWither(float w, PlantSettings s)
    {
        wither = Mathf.Clamp01(w);
        witherSettings = s;
        Apply();
    }

    /// <summary>
    /// Translates the plant's two progress values into this flower's growth value:
    /// opening (0 = bud, 1 = fully open) covers 0..openAt, fruit development (0..1) covers openAt..fruitAt.
    /// </summary>
    public float GrowthFor(float openProgress, float fruitProgress)
    {
        float openAt = ReachedAt(openStateName, 1f);
        float fruitAt = string.IsNullOrEmpty(fruitStateName) ? openAt : ReachedAt(fruitStateName, openAt);
        return Mathf.Clamp01(openProgress * openAt + fruitProgress * (fruitAt - openAt));
    }

    float ReachedAt(string state, float fallback)
    {
        foreach (MorphStage st in stages) if (st.shapeName == state) return st.reachedAt;
        return fallback;
    }

    //Inspector change (e.g. stage names or reachedAt edited during Play mode) -> look up the shapes again
    void OnValidate()
    {
        needsSetup = true;
        appliedGrowth = -1f;
    }

    void Update()
    {
        if (needsSetup) Setup();
        Apply();//only does work if growth, size or wither changed
    }

    void Setup()
    {
        needsSetup = false;
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        shapeIndex = new int[renderers.Length][];
        stageWeight = new float[stages.Count];
        freshColors = new Color[renderers.Length][];
        for (int r = 0; r < renderers.Length; r++)
        {
            Mesh m = renderers[r].sharedMesh;
            shapeIndex[r] = new int[stages.Count];
            for (int k = 0; k < stages.Count; k++)
            {   //check if the subcomponent has a shape key with one of the predefined names
                int idx = m != null ? m.GetBlendShapeIndex(stages[k].shapeName) : -1;
                shapeIndex[r][k] = idx;
                if (idx < 0)
                    Debug.LogWarning($"FlowerMorph: '{renderers[r].name}' has no blend shape '{stages[k].shapeName}'.", this);
            }
            //remember the materials' own colors, as starting point for color links and withering
            Material[] mats = renderers[r].sharedMaterials;
            freshColors[r] = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                freshColors[r][i] = mats[i] != null && mats[i].HasProperty(BaseColorId) ? mats[i].GetColor(BaseColorId) : Color.white;
        }
        for (int k = 1; k < stages.Count; k++)
            //check if morph stages were set in right order
            if (stages[k].reachedAt < stages[k - 1].reachedAt)
                Debug.LogWarning("FlowerMorph: reachedAt must increase from stage to stage.", this);
        //make sure the bounding box is large enough to encapsulate all possible stages
        EnlargeBounds();
    }

    void Apply()
    {
        if (renderers == null) return;
        if (Mathf.Approximately(growth, appliedGrowth) && Mathf.Approximately(size, appliedSize)
            && Mathf.Approximately(wither, appliedWither)) return;
        appliedGrowth = growth;
        appliedSize = size;
        appliedWither = wither;

        //find the stage the growth value is in: between the previous state (or the basis) and state k
        int prev = -1;              //-1 = basis
        float prevAt = 0f;
        int cur = stages.Count - 1;
        float t = 1f;               //progress within the stage, 0..1
        for (int k = 0; k < stages.Count; k++)
        {
            if (growth <= stages[k].reachedAt || k == stages.Count - 1)
            {
                cur = k;
                float span = stages[k].reachedAt - prevAt;
                t = span > 1e-6f ? Mathf.Clamp01((growth - prevAt) / span) : 1f;
                break;
            }
            prev = k;
            prevAt = stages[k].reachedAt;
        }
        if (smoothStages) t = t * t * (3f - 2f * t);//smoothstep: slow start, slow end

        //cross-fade: the previous state fades out while the current one fades in; all others are 0
        for (int k = 0; k < stages.Count; k++)
            stageWeight[k] = k == cur ? t : (k == prev ? 1f - t : 0f);

        //Unity's weights go from 0 to 100 (Blender's 0..1 times 100)
        for (int r = 0; r < renderers.Length; r++)
        {
            for (int k = 0; k < stages.Count; k++)
            {
                int idx = shapeIndex[r][k];
                if (idx < 0) continue;
                renderers[r].SetBlendShapeWeight(idx, stageWeight[k] * 100f);
            }
        }

        ApplyColors();

        //full size is reached when the flower is fully open; the fruit's growth is in its blend shape
        float openAt = ReachedAt(openStateName, 1f);
        float scaleProgress = openAt > 1e-6f ? Mathf.Clamp01(growth / openAt) : 1f;
        float scale = Mathf.Lerp(startScale, 1f, Mathf.Pow(scaleProgress, scaleExponent)) * size;
        transform.localScale = baseScale * scale;
    }

    /// <summary>Material colors: color links (driven by blend shapes) first, then withering on top.</summary>
    void ApplyColors()
    {
        if (colorLinks.Count == 0 && witherSettings == null) return;//nothing changes colors: keep the materials as they are
        if (block == null) block = new MaterialPropertyBlock();
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] mats = renderers[r].sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Color c = freshColors[r][i];
                foreach (ColorLink link in colorLinks)
                    if (link.material != null && link.material == mats[i])
                        c = Color.Lerp(link.colorAtZero, link.colorAtFull, StateWeight(link.shapeName));
                if (witherSettings != null) c = witherSettings.WitherColor(c, wither);
                block.Clear();
                block.SetColor(BaseColorId, c);
                renderers[r].SetPropertyBlock(block, i);//per material slot
            }
        }
    }

    /// <summary>How much a state is currently shown (0..1); a state counts as fully shown once growth has passed it.</summary>
    float StateWeight(string shapeName)
    {
        for (int k = 0; k < stages.Count; k++)
            if (stages[k].shapeName == shapeName)
                return growth >= stages[k].reachedAt ? 1f : stageWeight[k];
        return 0f;
    }

    /// <summary>
    /// Unity culls a renderer by its bounding box, which is computed from the basis (the small closed bud).
    /// Here each renderer's box is enlarged once to contain all states, so the open flower isn't culled.
    /// </summary>
    void EnlargeBounds()
    {
        var baked = new Mesh();
        foreach (SkinnedMeshRenderer smr in renderers)
        {
            Mesh m = smr.sharedMesh;
            if (m == null) continue;
            var saved = new float[m.blendShapeCount];
            //reset all weights to 0, so no two shape key transformations accumulate
            for (int i = 0; i < saved.Length; i++) { saved[i] = smr.GetBlendShapeWeight(i); smr.SetBlendShapeWeight(i, 0f); }

            Bounds b = m.bounds;//basis
            for (int i = 0; i < m.blendShapeCount; i++)
            {
                smr.SetBlendShapeWeight(i, 100f);
                smr.BakeMesh(baked);//mesh as it currently looks, in the renderer's local space
                //takes the union of saved bounding box b and the new one baked.bounds
                b.Encapsulate(baked.bounds);
                //reset to the starting shape
                smr.SetBlendShapeWeight(i, 0f);
            }
            //restore the original shape weights, because they might not have been 0
            for (int i = 0; i < saved.Length; i++) smr.SetBlendShapeWeight(i, saved[i]);
            smr.localBounds = b;
        }
        Destroy(baked);//meshes created with new Mesh() aren't garbage collected
    }
}
