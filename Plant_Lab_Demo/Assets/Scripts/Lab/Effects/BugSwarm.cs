using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A swarm of bugs coming out of a vent, flying around in an area, and flying back into the vent.
///
/// Level (the experiment's variable): an index into levelCounts (e.g. 0 = none, 1 = few, 2 = many). While the
/// effect is on, every changeEveryTicks ticks the level goes one up (pUp) or one down (pDown), or stays. Off = level 0.
/// The number of bugs present follows the level: missing bugs come out of the vent, surplus bugs fly back into it.
///
/// Order of bugs: a planned sequence in blocks of blockSize bugs of one type (Fixed: always the first type;
/// Changing: each block a random type, different from the block before). Arriving bugs are the next ones of that
/// sequence; leaving bugs are the oldest present ones (first in, first out). Partly used blocks continue at the next
/// increase.
///
/// Flight: each bug flies at its speed toward a target point in the area, turning at most turnRateDeg; at random
/// times it picks a new target. Near the target it overshoots and circles. Bugs keep a little distance from each
/// other and are pushed back into the area at its border. No physics, no colliders.
///
/// The area is a box centred on this object (its rotation applies; keep this object's scale at 1).
/// </summary>
public class BugSwarm : LabEffect
{
    public enum TypeMode { Fixed, Changing }

    [Header("Place")]
    [Tooltip("Where bugs come out and fly back in (its blue axis = direction out of the vent). Empty = this object.")]
    public Transform vent;
    [Tooltip("Size of the flight area (m), a box centred on this object.")]
    public Vector3 areaSize = new Vector3(0.6f, 0.25f, 0.4f);

    [Header("Bugs")]
    public BugType[] types = new BugType[0];
    [Tooltip("Fixed: always the first type. Changing: each block of bugs gets a random other type.")]
    public TypeMode typeMode = TypeMode.Fixed;
    [Tooltip("Bugs per block of one type (Changing).")]
    [Min(1)] public int blockSize = 5;

    [Header("Level")]
    [Tooltip("Number of bugs per level (index = level; level 0 should be 0 = none).")]
    public int[] levelCounts = { 0, 3, 8 };
    [Tooltip("The level can change every this many ticks (while on).")]
    [Min(1)] public int changeEveryTicks = 4;
    [Tooltip("Probability of one level up at a change point.")]
    [Range(0f, 1f)] public float pUp = 0.45f;
    [Tooltip("Probability of one level down at a change point (pUp + pDown <= 1; the rest = stays).")]
    [Range(0f, 1f)] public float pDown = 0.45f;
    [Tooltip("Level when the effect is switched on (at the trial start).")]
    [Min(0)] public int startLevel = 0;

    [Header("Flight")]
    [Tooltip("Bugs closer than this (m) push each other apart.")]
    [Min(0f)] public float separation = 0.06f;
    [Tooltip("How strongly they push apart.")]
    [Range(0f, 5f)] public float separationWeight = 1f;
    [Tooltip("A leaving bug disappears within this distance (m) of the vent.")]
    [Min(0.001f)] public float ventRadius = 0.03f;

    /// <summary>Current level (index into levelCounts).</summary>
    public int Level { get; private set; }
    /// <summary>Bugs present (in arrival order, oldest first).</summary>
    public int Present => present.Count;
    /// <summary>Bugs on their way back into the vent.</summary>
    public int Leaving => leaving.Count;

    class Bug
    {
        public int id, type;
        public Vector3 pos, dir, target;
        public float speed, retarget;
        public System.Random rng;
        public GameObject go;
    }

    readonly List<Bug> present = new List<Bug>();
    readonly List<Bug> leaving = new List<Bug>();
    readonly List<int> blockTypes = new List<int>();
    System.Random levelRng, typeRng;
    int nextId;
    int lastTick = -1;
    Transform container;
    MaterialPropertyBlock mpb;

    static readonly int LabTimeId = Shader.PropertyToID("_LabTime");
    static readonly int FlapFreqId = Shader.PropertyToID("_FlapFrequency");
    static readonly int FlapAmpId = Shader.PropertyToID("_FlapAmplitude");
    static readonly int FlapCenterId = Shader.PropertyToID("_FlapCenter");
    static readonly int FlapPhaseId = Shader.PropertyToID("_FlapPhase");
    static readonly int ForwardId = Shader.PropertyToID("_FlapForward");
    static readonly int SideId = Shader.PropertyToID("_FlapSide");
    static readonly int UpId = Shader.PropertyToID("_FlapUp");

    Transform Vent => vent != null ? vent : transform;
    protected override bool Busy => present.Count + leaving.Count > 0;

    // --- effect lifecycle ---

    protected override void ResetEffect(int s)
    {
        foreach (Bug b in present) Kill(b);
        foreach (Bug b in leaving) Kill(b);
        present.Clear();
        leaving.Clear();
        blockTypes.Clear();
        levelRng = new System.Random(Mix(s, -1));
        typeRng = new System.Random(Mix(s, -2));
        nextId = 0;
        Level = 0;
        lastTick = -1;
        Shader.SetGlobalFloat(LabTimeId, 0f);
    }

    protected override void Step(float dt, int tick)
    {
        if (tick != lastTick)
        {
            bool first = lastTick < 0;
            lastTick = tick;
            UpdateLevel(tick, first);
        }
        for (int i = 0; i < present.Count; i++) Fly(present[i], dt, false);
        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            Bug b = leaving[i];
            Fly(b, dt, true);
            if ((b.pos - Vent.position).sqrMagnitude <= ventRadius * ventRadius) { Kill(b); leaving.RemoveAt(i); }
        }
        Shader.SetGlobalFloat(LabTimeId, (float)EffectTime);
    }

    // --- level and order ---

    void UpdateLevel(int tick, bool first)
    {
        int maxLevel = Mathf.Max(0, levelCounts.Length - 1);
        if (!On) Level = 0;
        else if (first) Level = Mathf.Clamp(startLevel, 0, maxLevel);
        else if (tick % changeEveryTicks == 0)
        {
            double u = levelRng.NextDouble();
            if (u < pUp) Level = Mathf.Min(Level + 1, maxLevel);
            else if (u < pUp + pDown) Level = Mathf.Max(Level - 1, 0);
        }
        int want = levelCounts.Length > 0 ? Mathf.Max(0, levelCounts[Level]) : 0;
        while (present.Count < want && types.Length > 0) present.Add(Spawn());
        while (present.Count > want)
        {
            Bug oldest = present[0];//first in, first out
            present.RemoveAt(0);
            leaving.Add(oldest);
        }
    }

    int TypeOfBug(int id)
    {
        int block = typeMode == TypeMode.Changing ? id / blockSize : 0;
        while (blockTypes.Count <= block)
        {
            int t = 0;
            if (typeMode == TypeMode.Changing && types.Length > 1)
            {
                if (blockTypes.Count == 0) t = typeRng.Next(types.Length);
                else
                {
                    int prev = blockTypes[blockTypes.Count - 1];
                    t = typeRng.Next(types.Length - 1);//any other type
                    if (t >= prev) t++;
                }
            }
            blockTypes.Add(t);
        }
        return blockTypes[block];
    }

    // --- bugs ---

    Bug Spawn()
    {
        int id = nextId++;
        var b = new Bug { id = id, type = TypeOfBug(id), rng = new System.Random(Mix(TrialSeed, id)) };
        BugType bt = types[b.type];
        b.pos = Vent.position;
        b.dir = Vent.forward;
        b.speed = bt.speed * (1f + bt.speedJitter * (float)(2.0 * b.rng.NextDouble() - 1.0));
        b.target = RandomInArea(b.rng);
        b.retarget = NextRetarget(b.rng, bt);
        b.go = CreateVisual(b, bt);
        Place(b);
        return b;
    }

    Vector3 RandomInArea(System.Random r)
    {
        var local = new Vector3(
            (float)(r.NextDouble() - 0.5) * areaSize.x,
            (float)(r.NextDouble() - 0.5) * areaSize.y,
            (float)(r.NextDouble() - 0.5) * areaSize.z);
        return transform.position + transform.rotation * local;
    }

    static float NextRetarget(System.Random r, BugType bt) =>
        -bt.retargetSeconds * (float)Math.Log(1.0 - r.NextDouble());//exponential, mean retargetSeconds

    void Fly(Bug b, float dt, bool goingHome)
    {
        BugType bt = types[b.type];
        float turn = bt.turnRateDeg;
        Vector3 goal;
        if (goingHome)
        {
            goal = Vent.position;
            turn *= 3f;//head home decisively (no circling around the vent)
        }
        else
        {
            b.retarget -= dt;
            if (b.retarget <= 0f) { b.target = RandomInArea(b.rng); b.retarget = NextRetarget(b.rng, bt); }
            goal = b.target;
        }

        Vector3 to = goal - b.pos;
        Vector3 desired = to.sqrMagnitude > 1e-10f ? to.normalized : b.dir;
        if (!goingHome)
        {
            //keep distance from the others
            if (separation > 0f)
                for (int i = 0; i < present.Count; i++)
                {
                    Bug o = present[i];
                    if (o == b) continue;
                    Vector3 away = b.pos - o.pos;
                    float d = away.magnitude;
                    if (d > 1e-5f && d < separation) desired += away / d * (separationWeight * (1f - d / separation));
                }
            //back into the area at its border
            Vector3 local = Quaternion.Inverse(transform.rotation) * (b.pos - transform.position);
            Vector3 half = areaSize * 0.5f;
            if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                desired += (transform.position - b.pos).normalized;
            if (desired.sqrMagnitude < 1e-10f) desired = b.dir;
            desired.Normalize();
        }

        b.dir = Vector3.RotateTowards(b.dir, desired, turn * Mathf.Deg2Rad * dt, 0f);
        float stepLen = b.speed * dt;
        if (goingHome) stepLen = Mathf.Min(stepLen, to.magnitude);//don't overshoot the vent
        b.pos += b.dir * stepLen;
        Place(b);
    }

    void Place(Bug b)
    {
        if (b.go == null) return;
        Vector3 up = Mathf.Abs(Vector3.Dot(b.dir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.forward;
        b.go.transform.SetPositionAndRotation(b.pos, Quaternion.LookRotation(b.dir, up));
    }

    GameObject CreateVisual(Bug b, BugType bt)
    {
        if (container == null)
        {
            container = new GameObject("Bugs (generated)").transform;
            container.SetParent(transform, false);
        }
        var root = new GameObject($"{bt.name} {b.id}");
        root.transform.SetParent(container, false);
        root.transform.localScale = Vector3.one * bt.scale;
        if (bt.prefab == null) return root;

        GameObject model = Instantiate(bt.prefab, root.transform);
        model.transform.localPosition = Vector3.zero;//the prefab root's own position is ignored
        model.transform.localRotation = Quaternion.Euler(bt.modelRotation) * model.transform.localRotation;

        //wing flap values per renderer: the bug's axes in the mesh's own space, the type's flap, a random phase
        if (mpb == null) mpb = new MaterialPropertyBlock();
        float phase = (float)(b.rng.NextDouble() * 2.0 * Math.PI);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Transform t = r.transform;
            mpb.Clear();
            r.GetPropertyBlock(mpb);
            mpb.SetVector(ForwardId, t.InverseTransformDirection(root.transform.forward).normalized);
            mpb.SetVector(SideId, t.InverseTransformDirection(root.transform.right).normalized);
            mpb.SetVector(UpId, t.InverseTransformDirection(root.transform.up).normalized);
            mpb.SetFloat(FlapFreqId, bt.flapFrequency);
            mpb.SetFloat(FlapAmpId, bt.flapAmplitudeDeg);
            mpb.SetFloat(FlapCenterId, bt.flapCenterDeg);
            mpb.SetFloat(FlapPhaseId, phase);
            r.SetPropertyBlock(mpb);
        }
        return root;
    }

    static void Kill(Bug b)
    {
        if (b.go == null) return;
        b.go.SetActive(false);//gone in this frame already (Destroy happens at the frame's end)
        if (Application.isPlaying) Destroy(b.go); else DestroyImmediate(b.go);
        b.go = null;
    }

    // --- logging ---

    public override void LogState(Json j)
    {
        //types present, oldest first, as runs: [{type, n}, ...]
        var runs = new List<Json>();
        int i = 0;
        while (i < present.Count)
        {
            int t = present[i].type, n = 0;
            while (i < present.Count && present[i].type == t) { n++; i++; }
            runs.Add(new Json().Add("type", t).Add("n", n));
        }
        ulong h = StateHash.Start;
        foreach (Bug b in present) { StateHash.Add(ref h, b.id); StateHash.Add(ref h, b.pos); StateHash.Add(ref h, b.dir); }
        foreach (Bug b in leaving) { StateHash.Add(ref h, -1 - b.id); StateHash.Add(ref h, b.pos); StateHash.Add(ref h, b.dir); }
        j.Add("on", On)
         .Add("level", Level)
         .Add("present", present.Count)
         .Add("leaving", leaving.Count)
         .Add("types", runs)
         .Add("spawned", nextId)
         .Add("hash", StateHash.Hex(h));
    }

    public override void LogInfo(Json j)
    {
        var typeNames = new List<Json>();
        for (int i = 0; i < types.Length; i++)
            typeNames.Add(new Json().Add("index", i).Add("name", types[i] != null ? types[i].name : null));
        j.Add("typeMode", typeMode.ToString())
         .Add("types", typeNames)
         .Add("blockSize", blockSize)
         .Add("levelCounts", levelCounts)
         .Add("changeEveryTicks", changeEveryTicks)
         .Add("pUp", pUp)
         .Add("pDown", pDown)
         .Add("startLevel", startLevel);
    }

    // --- scene view ---

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, areaSize);
        Gizmos.matrix = Matrix4x4.identity;
        Transform v = Vent;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(v.position, ventRadius);
        Gizmos.DrawLine(v.position, v.position + v.forward * 0.1f);
    }
}
