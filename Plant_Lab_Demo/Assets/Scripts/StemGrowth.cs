using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal procedurally growing stem.
/// - Fixed simulation time step (deltaT), decoupled from render framerate.
/// - Seeded RNG: same seed + same parameters => same plant.
/// - Node-based skeleton; a tapered tube mesh is rebuilt from the nodes.
/// Radius depends on distance from the tip, so the tip stays pointy and
/// older parts of the stem thicken as the plant grows.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class StemGrowth : MonoBehaviour
{
    [Header("Simulation")]
    public int seed = 42;
    [Tooltip("Fixed simulation time step, in sim seconds.")]
    [Range(0.005f, 0.5f)] public float deltaT = 0.05f;
    [Tooltip("Sim seconds per real second (0 = paused).")]
    [Range(0f, 20f)] public float simSpeed = 1f;
    [Tooltip("Tick to restart growth from scratch with the current seed.")]
    public bool regrow;
    const int MaxStepsPerFrame = 200;

    [Header("Growth")]
    [Tooltip("Stem elongation, units per sim second.")]
    [Range(0f, 2f)] public float growthSpeed = 0.2f;
    [Tooltip("Length of one skeleton segment (a new node is committed each time).")]
    [Range(0.02f, 0.5f)] public float segmentLength = 0.1f;
    [Range(0.1f, 10f)] public float maxLength = 3f;

    [Header("Direction")]
    [Tooltip("Growth is biased toward this point. If empty, biased straight up.")]
    public Transform lightSource;
    [Tooltip("How strongly each new segment turns toward the target (0..1).")]
    [Range(0f, 1f)] public float directionBias = 0.1f;
    [Tooltip("Std of the Gaussian perturbation added to each new segment direction.")]
    [Range(0f, 1f)] public float perturbationStd = 0.15f;

    [Header("Shape")]
    [Range(0.001f, 0.2f)] public float maxRadius = 0.04f;
    [Tooltip("Radius gained per unit distance from the tip (0 at the tip = pointy).")]
    [Range(0.001f, 0.5f)] public float taper = 0.05f;
    [Range(3, 24)] public int radialSegments = 8;

    // --- simulation state ---
    System.Random rng;
    readonly List<Vector3> nodes = new List<Vector3>(); // committed nodes, local space
    Vector3 tipDir;          // direction of the segment currently growing
    float tipSegLen;         // length of the segment currently growing
    float committedLength;   // total length of committed segments
    float accumulator;       // unconsumed real time * simSpeed

    // --- rendering ---
    Mesh mesh;
    bool meshDirty;
    readonly List<Vector3> pts = new List<Vector3>();
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> norms = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> tris = new List<int>();

    public float TotalLength => committedLength + tipSegLen;

    void Awake()
    {
        mesh = new Mesh { name = "Stem" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        ResetPlant();
    }

    [ContextMenu("Regrow")]
    public void ResetPlant()
    {
        rng = new System.Random(seed);
        nodes.Clear();
        nodes.Add(Vector3.zero);
        tipDir = Vector3.up;
        tipSegLen = 0f;
        committedLength = 0f;
        accumulator = 0f;
        meshDirty = true;
    }

    void Update()
    {
        if (regrow) { regrow = false; ResetPlant(); }

        accumulator += Time.deltaTime * simSpeed;
        int steps = 0;
        while (accumulator >= deltaT && steps < MaxStepsPerFrame)
        {
            Step(deltaT);
            accumulator -= deltaT;
            steps++;
        }
        if (steps == MaxStepsPerFrame) accumulator = 0f; // don't spiral if sim can't keep up

        if (meshDirty) { BuildMesh(); meshDirty = false; }
    }

    /// <summary>One fixed simulation step. All dynamics live here.</summary>
    void Step(float dt)
    {
        if (TotalLength >= maxLength) return;

        tipSegLen += growthSpeed * dt;
        while (tipSegLen >= segmentLength)
        {
            nodes.Add(nodes[nodes.Count - 1] + tipDir * segmentLength);
            committedLength += segmentLength;
            tipSegLen -= segmentLength;
            tipDir = NextDirection(tipDir);
            // Later: branching / leaf spawning decisions go here, per committed node.
        }
        meshDirty = true;
    }

    Vector3 NextDirection(Vector3 dir)
    {
        Vector3 toTarget = Vector3.up;
        if (lightSource != null)
        {
            Vector3 d = transform.InverseTransformPoint(lightSource.position) - nodes[nodes.Count - 1];
            if (d.sqrMagnitude > 1e-6f) toTarget = d.normalized;
        }
        Vector3 biased = Vector3.Slerp(dir, toTarget, directionBias);
        Vector3 noise = new Vector3(Gaussian(), Gaussian(), Gaussian()) * perturbationStd;
        Vector3 result = biased + noise;
        return result.sqrMagnitude > 1e-6f ? result.normalized : dir;
    }

    /// <summary>Standard normal sample via Box-Muller, from the seeded RNG.</summary>
    float Gaussian()
    {
        double u1 = 1.0 - rng.NextDouble(); // (0,1]
        double u2 = rng.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }

    void BuildMesh()
    {
        pts.Clear();
        pts.AddRange(nodes);
        if (tipSegLen > 1e-4f) pts.Add(nodes[nodes.Count - 1] + tipDir * tipSegLen);

        mesh.Clear();
        if (pts.Count < 2) return;

        verts.Clear(); norms.Clear(); uvs.Clear(); tris.Clear();
        int n = pts.Count;
        int stride = radialSegments + 1; // duplicate seam vertex for clean UVs

        // Arc length from base for each point.
        var arc = new float[n];
        for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        float total = arc[n - 1];

        // Parallel-transport frames so the tube doesn't twist.
        Vector3 prevT = (pts[1] - pts[0]).normalized;
        Vector3 normal = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.99f ? Vector3.up : Vector3.right).normalized;

        for (int i = 0; i < n; i++)
        {
            Vector3 t = i == 0 ? pts[1] - pts[0]
                      : i == n - 1 ? pts[n - 1] - pts[n - 2]
                      : pts[i + 1] - pts[i - 1];
            t.Normalize();
            if (i > 0) normal = Quaternion.FromToRotation(prevT, t) * normal;
            prevT = t;
            Vector3 binormal = Vector3.Cross(t, normal);

            float radius = Mathf.Min(maxRadius, taper * (total - arc[i]));
            for (int j = 0; j <= radialSegments; j++)
            {
                float a = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 offset = Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal;
                verts.Add(pts[i] + offset * radius);
                norms.Add(offset);
                uvs.Add(new Vector2((float)j / radialSegments, arc[i]));
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int a = i * stride + j;
                int b = a + stride;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
