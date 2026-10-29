using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug view for L-system rules: derives the rules (instantly, discrete steps like the notebook) and draws the
/// result as lines (segments) and dots (all other symbols, e.g. tips and organs) at this object's position.
/// Drawn with Gizmos: visible in the Scene view, and in the Game view when its Gizmos button is on.
/// With a cactus segment type, S(s) and ~ build segments, shown as real meshes (with tufts) in a generated child object.
/// Changes in the Inspector are shown immediately, also outside Play mode.
/// </summary>
[ExecuteAlways]
public class LSystemPreview : MonoBehaviour
{
    [Serializable]
    public class MarkStyle
    {
        [Tooltip("The symbol (one character).")]
        public string symbol = "A";
        public Color color = Color.green;
        [Tooltip("Dot radius in meters.")]
        public float size = 0.02f;
    }

    [Tooltip("Optional: show the L-system of these plant settings (axiom, rules, constants, unit, angle) instead of the fields below.")]
    public PlantSettings fromSettings;

    [Header("L-system")]
    [TextArea(1, 3)] public string axiom = "A(0, 0)";
    [Tooltip("One rule per line:  predecessor(params) : condition -> successor @ weight   ('#' starts a comment).")]
    [TextArea(6, 30)] public string rules =
        "A(s, o) : s < maxLen * ratio**o and o < maxOrder -> F(dl) [ &(40) A(0, o+1) ] /(137.5) A(s+dl, o) @ pBranch\n" +
        "A(s, o) : s < maxLen * ratio**o -> F(dl) A(s+dl, o) @ 1 - pBranch\n" +
        "A(s, o) : s >= maxLen * ratio**o -> O(o)";
    [Tooltip("Names usable in all expressions. Value is a slider between min and max.")]
    public List<LConstant> constants = new List<LConstant>
    {
        new LConstant { name = "maxLen", value = 4f, min = 0.5f, max = 10f },
        new LConstant { name = "ratio", value = 0.6f, min = 0.1f, max = 1f },
        new LConstant { name = "maxOrder", value = 3f, min = 0f, max = 6f },
        new LConstant { name = "dl", value = 0.25f, min = 0.05f, max = 1f },
        new LConstant { name = "pBranch", value = 0.3f, min = 0f, max = 1f },
    };

    [Header("Cactus")]
    [Tooltip("Segment type for S(s) and ~ (with plant settings assigned: theirs). Empty: S is drawn as a dot.")]
    public CactusSegmentSettings cactusSegment;

    [Header("Derivation")]
    [Range(0, 40)] public int steps = 30;
    public int seed = 0;
    [Tooltip("Stops deriving before a generation gets bigger than this.")]
    public int maxModules = 200000;

    [Header("Turtle")]
    [Tooltip("Length of F without a parameter.")]
    public float stepLength = 1f;
    [Tooltip("Angle of + - & ^ \\ / without a parameter (degrees).")]
    public float angle = 25f;
    [Tooltip("Turtle units -> meters (1 unit = this many meters).")]
    public float scale = 0.1f;

    [Header("Drawing")]
    [Tooltip("Segment color by branching depth (bracket nesting): from the main axis (left) to the deepest branches (right).")]
    public Gradient depthColors = DefaultGradient();
    [Tooltip("Dot color and size per symbol. Symbols not listed: small grey dots.")]
    public List<MarkStyle> markStyles = new List<MarkStyle>
    {
        new MarkStyle { symbol = "A", color = new Color(0.6f, 0.85f, 0.2f), size = 0.01f },
        new MarkStyle { symbol = "O", color = new Color(0.1f, 0.6f, 0.15f), size = 0.03f },
        new MarkStyle { symbol = "L", color = new Color(0.2f, 0.65f, 0.2f), size = 0.02f },
        new MarkStyle { symbol = "K", color = new Color(0.9f, 0.4f, 0.6f), size = 0.03f },
    };

    /// <summary>Last parse/derive error (null if fine).</summary>
    [NonSerialized] public string error;
    /// <summary>Module counts of the drawn generation, and how many generations were derived.</summary>
    [NonSerialized] public string summary;

    [NonSerialized] bool dirty = true;
    [NonSerialized] int settingsVersion = -1;
    float drawScale = 0.1f;
    LTurtle.Geometry geometry;
    int maxDepth;
    readonly List<Vector3>[] linesByColor = new List<Vector3>[8];
    CactusSegmentSettings shownSegment;
    int segmentVersion = -1;
    [NonSerialized] bool meshDirty;
    Mesh segmentMesh, bodyMesh, tuftMesh;
    MeshFilter bodyFilter, tuftFilter;
    MeshRenderer bodyRenderer, tuftRenderer;

    static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.45f, 0.3f, 0.15f), 0f), new GradientColorKey(new Color(0.55f, 0.75f, 0.25f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    void OnValidate() => dirty = true;

    /// <summary>Derives and interprets the rules again if anything changed.</summary>
    public void Refresh()
    {
        //the plant settings were edited (sliders, rules): draw again
        if (fromSettings != null && fromSettings.Version != settingsVersion) { settingsVersion = fromSettings.Version; dirty = true; }
        CactusSegmentSettings seg = fromSettings != null ? fromSettings.cactusSegment : cactusSegment;
        if (seg != shownSegment || (seg != null && seg.Version != segmentVersion))
        {
            shownSegment = seg;
            segmentVersion = seg != null ? seg.Version : -1;
            dirty = true;
        }
        if (!dirty) return;
        dirty = false;
        meshDirty = true;
        geometry = null;
        error = null;
        summary = null;
        try
        {
            bool useSettings = fromSettings != null;
            var c = new List<(string, double)>();
            foreach (LConstant k in useSettings ? fromSettings.lsConstants : constants)
                if (!string.IsNullOrWhiteSpace(k.name)) c.Add((k.name, k.value));
            var system = useSettings ? new LSystem(fromSettings.lsAxiom, fromSettings.lsRules, c) : new LSystem(axiom, rules, c);
            List<List<LModule>> gens = system.Derive(steps, seed, maxModules, out bool stoppedEarly);
            List<LModule> last = gens[gens.Count - 1];
            drawScale = useSettings ? fromSettings.lsUnit : scale;
            geometry = LTurtle.Interpret(last, stepLength, useSettings ? fromSettings.lsAngle : angle, 1, shownSegment, drawScale, seed);
            summary = $"step {gens.Count - 1}{(stoppedEarly ? $" (stopped: step {gens.Count} would exceed {maxModules} modules)" : "")}: " +
                      $"{last.Count} modules — {LSystem.Summary(last)}" +
                      (shownSegment != null ? $" — {geometry.CactusSegments.Count} cactus segments" : "");
        }
        catch (Exception e)
        {
            error = e.Message;
            return;
        }

        maxDepth = 0;
        foreach (var s in geometry.Segments) maxDepth = Mathf.Max(maxDepth, s.depth);
        for (int i = 0; i < linesByColor.Length; i++) (linesByColor[i] ??= new List<Vector3>()).Clear();
        foreach (var s in geometry.Segments)
        {
            int bucket = maxDepth == 0 ? 0 : Mathf.RoundToInt((float)s.depth / maxDepth * (linesByColor.Length - 1));
            linesByColor[bucket].Add(LTurtle.ToUnity(s.start));
            linesByColor[bucket].Add(LTurtle.ToUnity(s.end));
        }
    }

    void Update()
    {
        Refresh();
        if (meshDirty) BuildCactus();
    }

    /// <summary>The cactus segments as two combined meshes (bodies, tufts) in generated child objects.</summary>
    void BuildCactus()
    {
        meshDirty = false;
        bool any = geometry != null && shownSegment != null && geometry.CactusSegments.Count > 0;
        if (!any) { RemoveCactus(); return; }
        if (bodyFilter == null) (bodyFilter, bodyRenderer) = CactusSegmentSettings.GeneratedChild(transform, "Cactus segments (generated)");
        if (tuftFilter == null) (tuftFilter, tuftRenderer) = CactusSegmentSettings.GeneratedChild(transform, "Cactus tufts (generated)");
        segmentMesh = shownSegment.shape.BuildMesh(segmentMesh, shownSegment.rings, shownSegment.columns);
        segmentMesh.hideFlags = HideFlags.DontSave;
        var bodies = new List<CombineInstance>();
        var tufts = new List<CombineInstance>();
        foreach (LTurtle.CactusSegment c in geometry.CactusSegments)
        {
            //segment space -> this object: base at the turtle's position, x = L, y = H (axis), z = U, scaled by the size
            Matrix4x4 m = Matrix4x4.TRS(LTurtle.ToUnity(c.position) * drawScale,
                                        Quaternion.LookRotation(LTurtle.ToUnity(c.U), LTurtle.ToUnity(c.H)),
                                        Vector3.one * (float)c.size);
            bodies.Add(new CombineInstance { mesh = segmentMesh, transform = m });
            shownSegment.AddTufts(tufts, c.areoles, m, c.seed, c.usedAreoles);
        }
        bodyMesh = Combine(bodyMesh, bodies, "Cactus preview segments");
        tuftMesh = Combine(tuftMesh, tufts, "Cactus preview tufts");
        bodyFilter.sharedMesh = bodyMesh;
        bodyRenderer.sharedMaterial = shownSegment.BodyMaterial;
        bodyRenderer.enabled = tuftRenderer.enabled = true;
        tuftFilter.sharedMesh = tuftMesh;
        tuftRenderer.sharedMaterial = shownSegment.TuftSource().material;
    }

    static Mesh Combine(Mesh mesh, List<CombineInstance> parts, string name)
    {
        if (mesh == null) mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        if (parts.Count > 0) mesh.CombineMeshes(parts.ToArray(), true, true);
        return mesh;
    }

    void RemoveCactus()
    {
        if (bodyFilter != null) DestroySafe(bodyFilter.gameObject);
        if (tuftFilter != null) DestroySafe(tuftFilter.gameObject);
        bodyFilter = tuftFilter = null;
        //after a script reload the fields are empty, but the generated children may still be there
        foreach (string n in new[] { "Cactus segments (generated)", "Cactus tufts (generated)" })
        {
            Transform t = transform.Find(n);
            if (t != null) DestroySafe(t.gameObject);
        }
    }

    //(not destroyed here: destroying children while this object is being deactivated isn't allowed)
    void OnDisable() { if (bodyRenderer != null) bodyRenderer.enabled = false; if (tuftRenderer != null) tuftRenderer.enabled = false; }
    void OnEnable() => meshDirty = true;

    void OnDestroy()
    {
        DestroySafe(segmentMesh); DestroySafe(bodyMesh); DestroySafe(tuftMesh);
    }

    static void DestroySafe(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    void OnDrawGizmos()
    {
        Refresh();
#if UNITY_EDITOR
        //something changed (e.g. the plant settings or segment asset in their own Inspector): Update doesn't run by
        //itself outside Play mode, so ask for one to rebuild the meshes
        if (meshDirty && !Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        if (geometry == null) return;
        Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * drawScale);
        for (int i = 0; i < linesByColor.Length; i++)
        {
            if (linesByColor[i] == null || linesByColor[i].Count == 0) continue;
            Gizmos.color = depthColors.Evaluate(i / (float)(linesByColor.Length - 1));
            Gizmos.DrawLineList(linesByColor[i].ToArray());
        }
        float inv = 1f / Mathf.Max(1e-6f, drawScale);//dot sizes are in meters, the matrix is in turtle units
        foreach (var m in geometry.Marks)
        {
            MarkStyle style = null;
            foreach (MarkStyle s in markStyles)
                if (!string.IsNullOrEmpty(s.symbol) && s.symbol[0] == m.symbol) { style = s; break; }
            Gizmos.color = style != null ? style.color : Color.grey;
            Gizmos.DrawSphere(LTurtle.ToUnity(m.position), (style != null ? style.size : 0.005f) * inv);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
