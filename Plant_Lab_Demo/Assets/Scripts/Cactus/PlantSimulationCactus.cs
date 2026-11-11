using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cactus part of the plant simulation: segments placed by S(s) in L-system rules (see LSystemDevelopment).
///   - A segment appears as a bud and grows to full size over segmentGrowDuration (its shape decides how: a swelling
///     ball, optionally with a column growing at the tip).
///   - Once it reaches segmentChildThreshold, what follows its S in the rules is run: ~ chooses an areole, S there
///     places a child (it appears when that areole exists, one after another every segmentChildInterval), flower symbols
///     place buds at its tip (or at the areole after a ~).
///   - Contacts (glass, older segments): a segment first tilts at its attachment (up to segmentMaxTiltDeg, then its
///     parent's attachment too); what's left stops its growth until it's free again (GrowthStop).
///   - Withering: the same wave as for stems; segments deflate toward their withered core (CactusShape.witheredCore).
/// </summary>
public partial class PlantSimulation
{
    /// <summary>All cactus segments, in creation order (parents before children).</summary>
    public readonly List<CactusSegment> CactusSegments = new List<CactusSegment>();

    /// <summary>Shape of the segments (null: no segment type assigned).</summary>
    public CactusShape SegmentShape => Settings.cactusSegment != null ? Settings.cactusSegment.shape : null;

    /// <summary>How withered a segment is: 0 = fresh, 1 = fully deflated.</summary>
    public float SegmentWither(CactusSegment c) => Mathf.Clamp01((SimTime - c.witherStart) / Settings.stemWitherTime);

    /// <summary>
    /// A segment at a branch's current tip (S in a branch's program), axis along H, its z along U (rest shape).
    /// `program` is what follows the S. Null if the limit is reached.
    /// </summary>
    public CactusSegment AddSegmentOnBranch(Branch b, Vector3 H, Vector3 U, float size, List<LModule> program)
    {
        if (SegmentShape == null) return null;
        if (CactusSegments.Count >= Settings.maxBranches) { LimitReached("cactus segments", "maxBranches", Settings.maxBranches); return null; }
        int seg = b.SegmentCount;
        float offset = b.tipSegLen;
        bool atNode = offset < 1e-5f && seg > 0;//right at the end of a committed segment: attach there
        var c = NewSegment(DeriveSeed(b.seed, 7777 + CactusSegments.Count), size, program);
        c.branch = b;
        c.branchSeg = atNode ? seg - 1 : seg;
        c.branchOffset = atNode ? b.segLength[seg - 1] : offset;
        c.restFrame = Frame(H, U);
        c.baseDistance = DistanceFromBase(b, b.TotalLength);
        CactusSegments.Add(c);
        UpdateCactusGeometry();
        return c;
    }

    CactusSegment NewSegment(int seed, float size, List<LModule> program) => new CactusSegment
    {
        index = CactusSegments.Count,
        seed = seed,
        rng = new System.Random(seed),
        size = Mathf.Max(0.01f, size),
        program = program,
        birthTime = SimTime,
        fullAreoles = SegmentShape.Areoles(seed),
    };

    /// <summary>
    /// ~ in a segment's rules: from the segment's center along the turtle's heading H (segment space) to the nearest free
    /// areole (all taken: the nearest one is shared), which is reserved. The turtle then stands there in the areole's frame
    /// (see AreoleFrame): heading out of the surface, turned like its parent. Turns after the ~ turn the child from there.
    /// False without areoles.
    /// </summary>
    public bool ToAreole(CactusSegment c, ref Vector3 H, ref Vector3 U, out int rib, out int areole)
    {
        rib = areole = -1;
        if (c.fullAreoles.Count == 0) return false;
        Vector3 center = 0.5f * SegmentShape.Tip();
        int best = -1, bestUsed = -1;
        float bestScore = float.NegativeInfinity, bestUsedScore = float.NegativeInfinity;
        for (int i = 0; i < c.fullAreoles.Count; i++)
        {
            CactusShape.SurfacePoint a = c.fullAreoles[i];
            float score = Vector3.Dot((a.position - center).normalized, H);
            if (c.reserved.Contains(CactusSegment.Key(a.rib, a.index))) { if (score > bestUsedScore) { bestUsedScore = score; bestUsed = i; } }
            else if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) best = bestUsed;
        CactusShape.SurfacePoint spot = c.fullAreoles[best];
        c.reserved.Add(CactusSegment.Key(spot.rib, spot.index));
        rib = spot.rib;
        areole = spot.index;
        Quaternion frame = AreoleFrame(spot.normal, spot.normal);
        H = frame * Vector3.up;
        U = frame * Vector3.forward;
        return true;
    }

    /// <summary>
    /// S in a segment's rules: a child at the given areole (rib -1: at the segment's tip), frame H/U in segment space.
    /// It appears once its areole exists, `order` x segmentChildInterval after the rules ran.
    /// </summary>
    public void PlanChild(CactusSegment parent, int rib, int areole, Vector3 H, Vector3 U, float size, List<LModule> program, int order)
    {
        Quaternion frame = Frame(H, U);
        //on an areole: relative to the areole's frame (full grown), so it follows the surface as it is at any time
        if (rib >= 0)
        {
            Vector3 n = FullAreole(parent, rib, areole).normal;
            frame = Quaternion.Inverse(AreoleFrame(n, n)) * frame;
        }
        parent.pending.Add(new PendingChild
        {
            size = size, rib = rib, areole = areole, restFrame = frame,
            program = program, readyTime = SimTime + order * Settings.segmentChildInterval,
        });
    }

    static CactusShape.SurfacePoint FullAreole(CactusSegment c, int rib, int areole) =>
        c.fullAreoles.Find(a => a.rib == rib && a.index == areole);

    /// <summary>
    /// Frame of an areole (segment space): y = H out of the surface (normal), turned like the segment itself: it keeps
    /// the segment axis (z or x) that's more perpendicular to the areole's full-grown normal (decided by that one only, so
    /// it never flips while the segment grows or withers). On a paddle: a child from the edge lies in the paddle's plane,
    /// one from the flat side keeps the paddle's width direction.
    /// </summary>
    /// H is turned from the normal toward the segment's own axis (its tip direction) by segmentChildUpwardDeg, at most
    /// until it points along the axis (rotating toward y scales x and z alike, so the axis choice below stays the same).
    Quaternion AreoleFrame(Vector3 normal, Vector3 fullNormal)
    {
        Vector3 h = Vector3.RotateTowards(normal, Vector3.up, Settings.segmentChildUpwardDeg * Mathf.Deg2Rad, 0f);
        if (Mathf.Abs(fullNormal.z) <= Mathf.Abs(fullNormal.x))
            return Frame(h, Vector3.ProjectOnPlane(Vector3.forward, h).normalized);//keep z (U)
        Vector3 l = Vector3.ProjectOnPlane(Vector3.right, h).normalized;//keep x (L)
        return Frame(h, Vector3.Cross(l, h));
    }

    CactusSegment AddChildSegment(CactusSegment parent, PendingChild ch)
    {
        if (CactusSegments.Count >= Settings.maxBranches) { LimitReached("cactus segments", "maxBranches", Settings.maxBranches); return null; }
        var c = NewSegment(DeriveSeed(parent.seed, parent.children.Count), ch.size, ch.program);
        c.parent = parent;
        c.depth = parent.depth + 1;
        c.rib = ch.rib;
        c.areole = ch.areole;
        c.restFrame = ch.restFrame;
        Vector3 at = ch.rib < 0 ? SegmentShape.Tip() : FullAreole(parent, ch.rib, ch.areole).position;
        c.baseDistance = parent.baseDistance + at.magnitude * parent.size;
        if (ch.rib >= 0) parent.occupied.Add(CactusSegment.Key(ch.rib, ch.areole));
        parent.children.Add(c);
        CactusSegments.Add(c);
        UpdateCactusGeometry();
        return c;
    }

    /// <summary>
    /// A flower bud on a segment: at its tip (rib -1) or at an areole, facing `heading` (segment space; at an areole it
    /// faces out of the surface). Null if the flower limit is reached.
    /// </summary>
    public Flower AddFlowerOnSegment(CactusSegment c, int rib, int areole, Vector3 heading, float sizeFactor)
    {
        if (Flowers.Count >= Settings.maxFlowers) { LimitReached("flowers", "maxFlowers", Settings.maxFlowers); return null; }
        System.Random r = c.rng;
        double uRoll = r.NextDouble();
        double uVariant = r.NextDouble();
        float sizeNoise = Gaussian(r);
        int count = Settings.flowerPrefabs != null ? Settings.flowerPrefabs.Length : 0;
        var flower = new Flower
        {
            branch = Root,//not on a branch: the root only provides its random stream index (fruit set) and contact bookkeeping
            segment = c,
            segmentRib = rib,
            segmentAreole = areole,
            indexOnBranch = Root.flowerCount++,
            birthTime = SimTime,
            sizeFactor = Mathf.Max(0.1f, 1f + sizeNoise * Settings.flowerSizeNoise) * Mathf.Max(0f, sizeFactor),
            rollDeg = (float)(uRoll * 360.0),
            variant = count > 0 ? Mathf.Min((int)(uVariant * count), count - 1) : -1,
            dir = heading.normalized,
        };
        if (rib >= 0) c.occupied.Add(CactusSegment.Key(rib, areole));//the flower replaces the tuft
        flower.index = Flowers.Count;
        Flowers.Add(flower);
        return flower;
    }

    /// <summary>Current position of a flower on a segment (plant space).</summary>
    Vector3 SegmentFlowerPosition(Flower f) => SegmentPoint(f.segment, f.segmentRib, f.segmentAreole, out _);

    /// <summary>Direction a flower on a segment faces now (segment space, before its own tilt): along its heading at the tip, out of the surface at an areole.</summary>
    Vector3 SegmentFlowerDir(Flower f)
    {
        if (f.segmentRib < 0) return f.dir;
        SegmentPoint(f.segment, f.segmentRib, f.segmentAreole, out Vector3 normal);
        return normal;
    }

    /// <summary>A point of a segment at its current state: its tip (rib -1) or an areole, with the surface normal there (segment space).</summary>
    Vector3 SegmentPoint(CactusSegment c, int rib, int areole, out Vector3 normalLocal)
    {
        CactusShape shape = SegmentShape;
        float g = c.ShownGrowth, w = SegmentWither(c);
        Vector3 local;
        if (rib < 0) { local = shape.Tip(g, w); normalLocal = Vector3.up; }
        else
        {
            CactusShape.SurfacePoint sp = shape.Areole(c.seed, rib, areole, g, w);
            local = sp.position;
            normalLocal = sp.normal;
        }
        return c.position + c.rotation * (local * c.size);
    }

    /// <summary>
    /// One growth step for the segments (Growing phase only): they grow unless stuck, run their rules at the child
    /// threshold, and place waiting children whose time has come and whose areole exists.
    /// </summary>
    void GrowSegments(float dt, LSystemDevelopment dev)
    {
        if (SegmentShape == null) return;
        CactusShape shape = SegmentShape;
        float rate = dt / Mathf.Max(0.1f, Settings.segmentGrowDuration);
        int n = CactusSegments.Count;//children placed in this step start growing in the next one
        for (int i = 0; i < n; i++)
        {
            CactusSegment c = CactusSegments[i];
            if (c.growth < 1f && (!c.stuck || c.stop.MayGrow(SimTime)))
            {
                c.stuck = false;
                c.growth = Mathf.Min(1f, c.growth + rate);
            }
            if (c.program != null && c.growth >= Settings.segmentChildThreshold) dev.RunSegmentProgram(this, c);
            for (int k = 0; k < c.pending.Count; k++)
            {
                PendingChild ch = c.pending[k];
                if (SimTime < ch.readyTime || (ch.rib >= 0 && !shape.AreoleExists(ch.rib, ch.areole, c.growth))) continue;
                AddChildSegment(c, ch);
                c.pending.RemoveAt(k--);
            }
        }
    }

    /// <summary>Leaving the Growing phase: segments stop where they are, waiting children never appear.</summary>
    void StopSegments()
    {
        foreach (CactusSegment c in CactusSegments) { c.pending.Clear(); c.program = null; }
    }

    /// <summary>Withering: fixes when the wave reaches each segment (farthest from the base first), with the same jitter as organs.</summary>
    void StartSegmentWithering(System.Random r)
    {
        foreach (CactusSegment c in CactusSegments)
            c.witherStart = WaveArrival(c.baseDistance + 0.5f * SegmentLength(c)) + Mathf.Abs(Gaussian(r)) * Settings.witherJitter;
    }

    /// <summary>Distance from a segment's base to its tip at its current state (m).</summary>
    float SegmentLength(CactusSegment c) => SegmentShape.Tip(c.ShownGrowth).y * c.size;

    /// <summary>Distance from the base of the farthest segment tip (for the withering wave).</summary>
    float SegmentMaxDistance()
    {
        float d = 0f;
        foreach (CactusSegment c in CactusSegments) d = Mathf.Max(d, c.baseDistance + SegmentLength(c));
        return d;
    }

    /// <summary>Distance from the plant's base of a flower (on a segment or a branch).</summary>
    float FlowerBaseDistance(Flower f) =>
        f.segment == null ? DistanceFromBase(f.branch, f.arcPos)
                          : f.segment.baseDistance + (f.segmentRib < 0 ? SegmentLength(f.segment) : 0.5f * SegmentLength(f.segment));

    /// <summary>Positions and rotations of all segments from their attachments (parents first), and their collision samples.</summary>
    public void UpdateCactusGeometry()
    {
        CactusShape shape = SegmentShape;
        if (shape == null) return;
        foreach (CactusSegment c in CactusSegments)
        {
            Quaternion frame;
            if (c.parent == null)
            {
                c.position = c.branch.PointAt(c.branchSeg, c.branchOffset);
                c.jointRotation = c.branch.RotAt(c.branchSeg);
                frame = c.restFrame;
            }
            else
            {
                CactusSegment p = c.parent;
                c.position = SegmentPoint(p, c.rib, c.areole, out Vector3 n);
                c.jointRotation = p.rotation;
                //on an areole: the areole's frame as the surface is now (it turns while the parent swells or withers)
                frame = c.rib < 0 ? c.restFrame : AreoleFrame(n, FullAreole(p, c.rib, c.areole).normal) * c.restFrame;
            }
            c.rotation = Quaternion.Normalize(c.jointRotation * c.tilt * frame);
            UpdateSamples(c, shape);
        }
    }

    /// <summary>Rotation of a segment frame: y = H, z = U, x = H x U = L.</summary>
    static Quaternion Frame(Vector3 H, Vector3 U) => Quaternion.LookRotation(U, H);

    /// <summary>Surface samples and rough body of a segment (recomputed only when its growth or wither changed), placed at its pose.</summary>
    void UpdateSamples(CactusSegment c, CactusShape shape)
    {
        float g = c.ShownGrowth, w = SegmentWither(c);
        if (Mathf.Abs(g - c.samplesGrowth) > 0.003f || Mathf.Abs(w - c.samplesWither) > 0.003f)
        {
            c.samplesGrowth = g;
            c.samplesWither = w;
            shape.BodyExtents(g, w, out c.bodyA, out c.bodyB, out c.bodyC, out c.bodyColumn);
            c.localSamples.Clear();
            //only needed against other segments. Not near the base: it's anchored (tilting can't move it), and touching
            //there is mostly where segments are attached next to each other
            if (CactusSegments.Count > 1)
                for (int i = 0; i < SampleRings; i++)
                    for (int j = 0; j < SampleColumns; j++)
                        c.localSamples.Add(shape.Point(2f * Mathf.PI * (j + 0.5f * (i % 2)) / SampleColumns,
                                                       Mathf.Lerp(SampleFrom, 0.95f, i / (SampleRings - 1f)), g, w));
        }
        c.samples.Clear();
        foreach (Vector3 p in c.localSamples) c.samples.Add(c.position + c.rotation * (p * c.size));
    }
    const int SampleRings = 5, SampleColumns = 10;
    const float SampleFrom = 0.3f;//lowest sample ring (0 = base, 1 = tip)

    // --- contacts ---

    /// <summary>
    /// Segments touching obstacles or older segments (not their own parent or children) tilt away at their attachment,
    /// then at their parent's; what's left stops their growth. Several passes, like ResolveContacts.
    /// </summary>
    void ResolveCactusContacts(float dt)
    {
        if (CactusSegments.Count == 0 || SegmentShape == null) return;
        float step = 1.5f * PlantSettings.BendSpeed * dt;
        float maxTilt = Settings.segmentMaxTiltDeg;
        foreach (CactusSegment c in CactusSegments) c.tiltUsed = 0f;
        bool any = false;
        for (int pass = 0; pass < PlantSettings.ContactIterations; pass++)
        {
            any = false;
            foreach (CactusSegment c in CactusSegments)
            {
                c.touching = SegmentPush(c, out Vector3 push, out Vector3 at);
                if (!c.touching) continue;
                any = true;
                Vector3 rest = TiltOrgan(ref c.tilt, ref c.tiltUsed, c.jointRotation, c.position, push, at, maxTilt, step);
                if (rest.sqrMagnitude > 1e-12f && c.parent != null)
                    TiltOrgan(ref c.parent.tilt, ref c.parent.tiltUsed, c.parent.jointRotation, c.parent.position, rest, at, maxTilt, step);
            }
            if (!any) break;
            UpdateCactusGeometry();
        }
        //still touching after all passes: stop growing (for now)
        if (any)
            foreach (CactusSegment c in CactusSegments)
                if (c.touching && SegmentPush(c, out _, out _)) { c.stuck = true; c.stop.Stuck(SimTime); }
    }

    /// <summary>
    /// The rest of a flower's push (after its own tilt) moves the segment it sits on: the segment tilts at its attachment.
    /// </summary>
    void PushSegment(CactusSegment c, Vector3 at, Vector3 push)
    {
        float step = 1.5f * PlantSettings.BendSpeed * PlantSettings.DeltaT;
        TiltOrgan(ref c.tilt, ref c.tiltUsed, c.jointRotation, c.position, push, at, Settings.segmentMaxTiltDeg, step);
        UpdateCactusGeometry();
    }

    /// <summary>
    /// How far a segment reaches into obstacles and older segments (combined push out, like OrganPush) and where.
    /// </summary>
    bool SegmentPush(CactusSegment c, out Vector3 push, out Vector3 at)
    {
        push = at = Vector3.zero;
        int n = 0;
        float gap = 0.5f * Settings.obstacleClearance;
        if (Obstacles != null)
        {
            SegmentBounds(c, out Vector3 center, out float radius);
            if (Obstacles.AnyWithin(center, radius + gap))
            {
                SegmentBoxes(c, boxBuffer);
                foreach (var (bc, half) in boxBuffer)
                    if (Obstacles.BoxPenetration(bc, half + Vector3.one * gap, c.rotation, out Vector3 v)) Add(v, bc, ref push, ref at, ref n);
            }
        }
        //older segments don't give way to younger ones; a segment and its parent/children overlap where they're attached
        for (int j = 0; j < c.index; j++)
        {
            CactusSegment o = CactusSegments[j];
            if (o == c.parent || o.parent == c) continue;
            SegmentBounds(c, out Vector3 c1, out float r1);
            SegmentBounds(o, out Vector3 c2, out float r2);
            if ((c1 - c2).sqrMagnitude > Sq(r1 + r2 + gap)) continue;
            //c's surface inside o: out of o. o's surface inside c: c moves the other way
            foreach (Vector3 p in c.samples)
                if (InsideBody(o, p, gap, out Vector3 v)) Add(v, p, ref push, ref at, ref n);
            foreach (Vector3 p in o.samples)
                if (InsideBody(c, p, gap, out Vector3 v)) Add(-v, p, ref push, ref at, ref n);
        }
        if (n == 0) return false;
        at /= n;
        return true;
    }

    static void Add(Vector3 v, Vector3 point, ref Vector3 push, ref Vector3 at, ref int n)
    {
        float d = v.magnitude;
        if (d < 1e-7f) return;
        Vector3 dir = v / d;
        float already = Vector3.Dot(push, dir);
        if (already < d) push += dir * (d - already);//only what's still missing in that direction (no wall counted twice)
        at += point;
        n++;
    }

    static float Sq(float x) => x * x;
    readonly List<(Vector3 center, Vector3 half)> boxBuffer = new List<(Vector3, Vector3)>();

    /// <summary>A sphere around a segment's rough body (plant space).</summary>
    static void SegmentBounds(CactusSegment c, out Vector3 center, out float radius)
    {
        float halfLength = c.bodyB + 0.5f * c.bodyColumn;
        center = c.position + c.rotation * (Vector3.up * (halfLength * c.size));
        radius = Mathf.Sqrt(Sq(Mathf.Max(c.bodyA, c.bodyC)) + Sq(halfLength)) * c.size;
    }

    /// <summary>
    /// A segment as a few slices of its rough body (boxes along its axis, rotated like the segment), for obstacles.
    /// The bottom (its attachment) is left out: it sits on its parent or on the ground.
    /// </summary>
    public void SegmentBoxes(CactusSegment c, List<(Vector3 center, Vector3 half)> boxes)
    {
        boxes.Clear();
        float b = c.bodyB, col = c.bodyColumn;
        float y0 = 0.15f * b, y1 = 2f * b + col;
        if (y1 - y0 < 1e-5f) return;
        for (int k = 0; k < BoxSlices; k++)
        {
            float ya = Mathf.Lerp(y0, y1, k / (float)BoxSlices), yb = Mathf.Lerp(y0, y1, (k + 1f) / BoxSlices);
            float f = Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(AxialOffset(c, 0.5f * (ya + yb)))));
            boxes.Add((c.position + c.rotation * (Vector3.up * (0.5f * (ya + yb) * c.size)),
                       new Vector3(c.bodyA * f, 0.5f * (yb - ya), c.bodyC * f) * c.size));
        }
    }
    const int BoxSlices = 5;

    /// <summary>Along the rough body's axis: -1..0 over the lower half of the ball, 0 along the column, 0..1 over the upper half.</summary>
    static float AxialOffset(CactusSegment c, float y)
    {
        float b = Mathf.Max(1e-6f, c.bodyB);
        if (y < b) return (y - b) / b;
        if (y < b + c.bodyColumn) return 0f;
        return (y - b - c.bodyColumn) / b;
    }

    /// <summary>Is a point (plant space) inside a segment's rough body (plus gap)? push = the way out (plant space).</summary>
    static bool InsideBody(CactusSegment c, Vector3 point, float gap, out Vector3 push)
    {
        push = Vector3.zero;
        Vector3 l = Quaternion.Inverse(c.rotation) * (point - c.position) / c.size;
        float g = gap / c.size;
        float a = c.bodyA + g, b = Mathf.Max(1e-6f, c.bodyB + g), cz = c.bodyC + g;
        if (l.y < 0f || a < 1e-6f || cz < 1e-6f) return false;
        float dy = AxialOffset(c, l.y);
        float k = Sq(l.x / a) + Sq(l.z / cz) + dy * dy;
        if (k >= 1f) return false;
        //distance to the surface ~ (1 - k) / |gradient of k|, out along the gradient
        var grad = new Vector3(2f * l.x / (a * a), 2f * dy / b, 2f * l.z / (cz * cz));
        float len = grad.magnitude;
        Vector3 dirLocal = len > 1e-6f ? grad / len : Vector3.right;
        float depth = len > 1e-6f ? Mathf.Min((1f - k) / len, Mathf.Max(a, cz)) : Mathf.Min(a, cz);
        push = c.rotation * dirLocal * (depth * c.size);
        return true;
    }
}
