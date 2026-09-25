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
public class StemGrowth : MonoBehaviour// inheritance makes this class a component, name must match script name
{
    [Header("Simulation")]//Defines subgroups of parameters in inspector (only if set PUBLIC)
    public int seed = 42;
    [Tooltip("Fixed simulation time step, in sim seconds.")]//shown when hovering over field
    [Range(0.005f, 0.5f)] public float deltaT = 0.05f;//step size for things to evolve
    [Tooltip("Sim seconds per real second (0 = paused).")]
    [Range(0f, 20f)] public float simSpeed = 1f;//speed of overall ssimulation relative to real time
    [Tooltip("Tick to restart growth from scratch with the current seed.")]
    public bool regrow;//immediatedly set back to false when clicked -> acts like toggle for reset
    const int MaxStepsPerFrame = 200;

    [Header("Growth")]
    [Tooltip("Stem elongation, units per sim second.")]
    [Range(0f, 2f)] public float growthSpeed = 0.2f;
    [Tooltip("Length of one skeleton segment (a new node is committed each time).")]
    [Range(0.02f, 0.5f)] public float segmentLength = 0.1f;
    [Range(0.1f, 10f)] public float maxLength = 3f;//of all concatenated segments

    [Header("Direction")]
    [Tooltip("Growth is biased toward this point. If empty, biased straight up.")]
    public Transform lightSource;
    [Tooltip("Turning rate toward the target, per unit of stem length. The remaining angle to the target " +
             "shrinks by a factor exp(-directionBias * distance), independent of segmentLength.")]
    [Range(0f, 10f)] public float directionBias = 1f;
    [Tooltip("Direction noise per sqrt(unit of stem length). Per-segment std = perturbationStd * sqrt(segmentLength), " +
             "so the wobble per unit length is independent of segmentLength (random walk of the direction).")]
    [Range(0f, 2f)] public float perturbationStd = 0.5f;

    [Header("Shape")]
    [Range(0.001f, 0.2f)] public float maxRadius = 0.04f;//maximum radius reached further away from tip
    [Tooltip("Radius gained per unit distance from the tip (0 at the tip = pointy).")]
    [Range(0.001f, 0.5f)] public float taper = 0.05f;//the smaller, the longer the peaky part before reaching full radius
                                                     //inverse slope of cone tip
    [Range(3, 24)] public int radialSegments = 8;

    // --- simulation state ---
    System.Random rng;//local random seed instead of the global UnityEngine.Random
    readonly List<Vector3> nodes = new List<Vector3>(); // committed nodes, local space, reference can't be changed
    Vector3 tipDir;          // direction of the segment currently growing
    float tipSegLen;         // length of the segment currently growing
    float committedLength;   // total length of committed segments (skeleton that finished growing)
    float accumulator;       // unconsumed real time * simSpeed

    // --- rendering ---
    Mesh mesh;
    bool meshDirty;//flag, that indicates if the mesh is out of date and needs rebuilding
    readonly List<Vector3> pts = new List<Vector3>();//storing center spline through the stem
    readonly List<Vector3> verts = new List<Vector3>();//3D corner points
                                                       //indices are given within i*stride+j
                                                       //i... ring number, stride ... vertices per ring, j...within-ring position
    readonly List<Vector3> norms = new List<Vector3>();//facing direction of a vertex (yes, for a point, not a face)
    readonly List<Vector2> uvs = new List<Vector2>();//extra coordinates within a face, because xyz are alread taken for global
                                                     //u... goes around the stem, v... distance from base
    readonly List<int> tris = new List<int>();//vertex indices; 3 for each triangle
    readonly List<float> arc = new List<float>();//full distance from base to pt

    public float TotalLength => committedLength + tipSegLen;//getter, recalculated every frame because of growing tip

    void Awake()
    {
        mesh = new Mesh { name = "Stem" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;//allows more vertices than Int16 if needed
        mesh.MarkDynamic();//optimizes performance, because mesh gets rewritten very often
        GetComponent<MeshFilter>().sharedMesh = mesh;//hands the mesh over meshFilter to the meshRenderer for drawing it
        ResetPlant();
    }

    [ContextMenu("Regrow")]//This is separate from the "regrow" button in the menu group
    public void ResetPlant()//the only difference is that this directly calls the function, unlike regrow, that only leads to the function call down the line
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
        if (regrow) { regrow = false; ResetPlant(); }//immediately reset to false after regrow was pressed

        //deltaTime is the actual time since last frame -> can vary depending on actual FPS
        //simulated time, that has passed in the real world, but wasn't simulated yet
        accumulator += Time.deltaTime * simSpeed;//avoids plant growing faster on fast PC
        //example: game runs on 60fps -> 0.017 s/frame -> for simspeed=1, deltaT=0.05...
        //...after 3 frames one step is done, because accumulator >0.05
        //... if slower fps, more steps are done in one go
        int steps = 0;
        //multiple steps are done in one frame if that frame takes too long to process on PC
        while (accumulator >= deltaT && steps < MaxStepsPerFrame)
        {
            Step(deltaT);
            accumulator -= deltaT;
            steps++;
        }
        //if the frames are so slow that many steps have to be done within (making the frame even slower), the game can freeze
        //capping maximum steps avoids this, even if step timing isn't accurate anymore
        if (steps == MaxStepsPerFrame) accumulator = 0f;

        if (meshDirty) { BuildMesh(); meshDirty = false; }//updating how everything looks like after all the steps taken
    }

    /// <summary>One fixed simulation step. All dynamics live here.</summary>
    void Step(float dt)
    {
        if (TotalLength >= maxLength) return;

        tipSegLen += growthSpeed * dt;
        //if the segment length is reached, freeze that part and choose new direction
        //while loop because if step length growt speed is fast, multiple segments could get committed
        while (tipSegLen >= segmentLength)
        {
            nodes.Add(nodes[nodes.Count - 1] + tipDir * segmentLength);
            committedLength += segmentLength;
            tipSegLen -= segmentLength;//remove already committed part
            tipDir = NextDirection(tipDir);
            // Later: branching / leaf spawning decisions go here, per committed node.
        }
        meshDirty = true;
    }

    Vector3 NextDirection(Vector3 dir)
    {
        Vector3 toTarget = Vector3.up;
        if (lightSource != null)
        {//inverseTransform transforms the lightsource's global position to local relative to plant origin
            Vector3 d = transform.InverseTransformPoint(lightSource.position) - nodes[nodes.Count - 1];
            //prevent normalization if plant reached almost exact light position
            if (d.sqrMagnitude > 1e-6f) toTarget = d.normalized;
        }
        //spherical interpolation toward the light. The per-segment fraction 1 - exp(-k*L) compounds to
        //exp(-k*distance) over any distance, so the bending per unit length doesn't depend on segmentLength
        //the longer the segment, the more it turns
        //the shorter the segment, the less it turns, so turning angles don't accumulate too fast
        float turnFraction = 1f - Mathf.Exp(-directionBias * segmentLength);
        Vector3 biased = Vector3.Slerp(dir, toTarget, turnFraction);
        //variances of independent steps add up, so std per segment scales with sqrt(segmentLength)
        //total variance is independent from L this way, because: (D/L)*sigma^2*sqrt(L)^2 = D*sigma^2
        // -> (D/L)... number of segments for total length D and segment length L
        // -> variance accumulates over total distance
        float segmentStd = perturbationStd * Mathf.Sqrt(segmentLength);
        Vector3 noise = new Vector3(Gaussian(), Gaussian(), Gaussian()) * segmentStd;
        Vector3 result = biased + noise;
        return result.sqrMagnitude > 1e-6f ? result.normalized : dir;//check if noise canceled out the direction
    }

    /// <summary>Standard normal sample via Box-Muller, from the seeded RNG.</summary>
    /// necessary, because no inherent function is available in c# to draw standard normal values
    float Gaussian()//transfroms unifrom random to gaussian N[0,1]
    {
        double u1 = 1.0 - rng.NextDouble(); // (0,1] to avoid log(0)
        double u2 = rng.NextDouble();
        //sin() would also be possible, but that's not used here
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }

    void BuildMesh()
    {
        pts.Clear();//this is only cleared completely, because it's shorter code. You could also only update the last element
        pts.AddRange(nodes);
        //all completed nodes and the still growing tip position
        //right after segment was committed, the if avoids zero-vector problems
        if (tipSegLen > 1e-4f) pts.Add(nodes[nodes.Count - 1] + tipDir * tipSegLen);

        mesh.Clear();//needs to be done before returning, in case full plant was reset right before
        //otherwise the fully grown old plant would still be drawn
        if (pts.Count < 2) return;//at least one point is necessary

        //here clearing all is necessary because of the radius change
        //only contents is cleared, same memory is still reserved
        verts.Clear(); norms.Clear(); uvs.Clear(); tris.Clear();
        int n = pts.Count;
        int stride = radialSegments + 1; // duplicate seam vertex for clean UVs

        // Arc length from base for each point.
        arc.Clear(); arc.Add(0f);
        for (int i = 1; i < n; i++) arc.Add(arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
        float total = arc[n - 1];//total length as cumulative distance of nodes

        // Parallel-transport frames so the tube doesn't twist
        //normal at base is used as reference and changed as little as possible, as the plant grows
        Vector3 prevT = (pts[1] - pts[0]).normalized;
        //normal is chosen arbitrary between growing direction and either up, or right pointing vector(if growing direction itself is currently up)
        //but once it's chosen it's fixed
        Vector3 normal = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.99f ? Vector3.up : Vector3.right).normalized;

        //placing a ring of vertices around every point along the stem skeleton
        //makes sure cylinders transition smoothly over to one another, instead of having sticking-out edges of cylinders
        for (int i = 0; i < n; i++)
        {
            Vector3 t = i == 0 ? pts[1] - pts[0]//forward-looking vector
                      : i == n - 1 ? pts[n - 1] - pts[n - 2]//backward-looking vector
                      : pts[i + 1] - pts[i - 1];//vector from previous to next point, jumping current point
            t.Normalize();
            //take smallest rotation from previous to current t and change the normal by it
            //this makes sure indices of vertices in consecutive rings keep parallel, not twist
            if (i > 0) normal = Quaternion.FromToRotation(prevT, t) * normal;
            prevT = t;
            //gives the third normal vector, that completes the 3D reference grid around a segment
            //always chosen left-handed, so direction doesn't flip
            Vector3 binormal = Vector3.Cross(t, normal);

            float radius = Mathf.Min(maxRadius, taper * (total - arc[i]));
            //vertices along one ring +1, because ring needs to get closed
            for (int j = 0; j <= radialSegments; j++)
            {
                float a = (float)j / radialSegments * Mathf.PI * 2f;//position around circumference of unit circle
                //finding xyz coords of point in the plane that goes through the circle, normal to growing direction t
                Vector3 offset = Mathf.Cos(a) * normal + Mathf.Sin(a) * binormal;
                verts.Add(pts[i] + offset * radius);//local coords of circle vertex relative to plant origin
                norms.Add(offset);//vector pointing from node to circle vertex
                uvs.Add(new Vector2((float)j / radialSegments, arc[i]));
            }
        }

        //connecting neighboring rings via triangles
        for (int i = 0; i < n - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                //ring i+1:   b ──────── b+1
                //            │ ╲         │
                //            │   ╲   ②  │
                //            │ ①   ╲    │
                //            │       ╲   │
                //ring i:     a ──────── a+1
                //
                //① = (a, a+1, b)      ② = (a+1, b+1, b)
                int a = i * stride + j;//index part of lower ring
                int b = a + stride;//index part of upper ring
                tris.Add(a); tris.Add(a + 1); tris.Add(b);//left triangle
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);//right triangle
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);//also recalculates the bounds (calculateBounds defaults to true)
    }

    //meshes created with new Mesh() live on Unity's native side and aren't garbage collected
    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
