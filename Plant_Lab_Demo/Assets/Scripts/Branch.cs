using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One branch of the plant (the main stem is the branch with depth 0).
/// Plain C# class: pure simulation data and growth, no GameObjects, no rendering.
///
/// Growth: the tip grows at growthSpeed; every SegmentLength a segment is committed and keeps its length.
/// Positions are recalculated from the base outward every step (UpdateGeometry), because bending (below)
/// and the parent branch can move them.
///
/// Bending: every direction is stored as grown (the "rest" shape, the plant's memory of its intended form).
/// Each node is a joint that can be bent; the current shape is the rest shape with all bends applied,
/// from the plant's base outward (a bend at a node turns everything after it: later segments, side branches,
/// leaves, flowers). Bends relax back if nothing pushes anymore and become permanent over time.
/// </summary>
public class Branch
{
    public readonly int depth;           //branching level: 0 = main stem, 1 = side branch, ...
    public readonly Branch parent;       //null for the main stem
    public readonly int seed;            //this branch's own seed, derived from its parent's
    public readonly float birthTime;     //sim time the branch spawned

    //where on the parent this branch is attached: segment index + distance from that segment's start.
    //the branch moves along with that point when the parent bends
    public readonly int attachSeg;
    public readonly float attachOffset;
    public readonly float attachArc; //distance from the parent's base to the attachment point

    //each branch has its own random streams, so what happens on one branch
    //never changes the random numbers another branch gets
    public readonly System.Random dirRng;   //direction noise
    public readonly System.Random spawnRng; //spawn decisions (leaf/branch, angles, sizes)

    // --- committed segments (segment k goes from node k to node k+1) ---
    public readonly List<Vector3> segRest = new List<Vector3>();     //direction as grown (rest shape), fixed at commit
    public readonly List<Vector3> segDir = new List<Vector3>();      //current direction, with all bends (cache, see UpdateGeometry)
    public readonly List<float> segLength = new List<float>();  //length of the segment
    public readonly List<float> segBirth = new List<float>();        //sim time of commit

    // --- nodes (node 0 = branch base) ---
    public readonly List<Vector3> nodes = new List<Vector3>();       //current positions, plant-local (cache, see UpdateGeometry)
    public readonly List<float> nodeRadius = new List<float>();      //current radius (cache, see UpdateGeometry)
    public readonly List<float> nodeBirth = new List<float>();       //sim time the node was formed
    public readonly List<float> nodeArc = new List<float>();    //distance from the branch base
    public readonly List<float> nodeRadiusCap = new List<float>();   //pressed against an obstacle with no way out: stopped thickening here
    // joints: every node can bend (node 0 = where the branch meets its parent / the ground)
    public readonly List<Quaternion> jointBend = new List<Quaternion>();   //current bend at this node
    public readonly List<Quaternion> jointTarget = new List<Quaternion>(); //bend it relaxes back to (identity = as grown; drifts toward the bend = it becomes permanent)
    public readonly List<Quaternion> nodeRot = new List<Quaternion>();     //rotation from rest shape to current shape after this node (cache)
    public readonly List<float> bendUsed = new List<float>();              //degrees this node has already turned in the current step (speed limit)
    // sideways shifts: a node pushed away from an obstacle locally (a dent), without turning what comes after it
    public readonly List<Vector3> nodeShift = new List<Vector3>();         //current shift of this node
    public readonly List<Vector3> nodeShiftTarget = new List<Vector3>();   //shift it relaxes back to (zero = as grown; drifts toward the shift = permanent)
    public readonly List<float> shiftUsed = new List<float>();             //units this node has already been shifted in the current step (speed limit)

    public readonly List<Branch> children = new List<Branch>();
    public readonly List<Leaf> leaves = new List<Leaf>();

    public Vector3 tipDir;          // direction of the segment currently forming, in the rest shape
    public Vector3 tipNormal;       // parallel-transported reference axis perpendicular to tipDir (rest shape, for spawn angles)
    public float tipSegLen;         // length of the segment currently forming at the tip
    public float committedLength;   // length of all committed segments
    public float nextSpawnCheck;    // branch length at which the next spawn check happens
    public float finishTime = -1f;  // sim time the branch formed all its tissue (-1 = still growing)
    public bool stopped;            // growth was cut off before the branch finished (Growing phase ended): no bud
    public bool blocked;            // the tip ran into obstacles with no free direction left: counts as finished
    readonly List<Vector3> contacts = new List<Vector3>(); // obstacle normals touched by the tip in the current step
    public bool flowerDecided;      // the flower check at the finished tip has been made (once per branch)
    public Flower flower;           // flower at the tip, or null
    public float phyllotaxisAngle;  // angle around the branch (degrees) of the most recent leaf/branch

    public float TotalLength => committedLength + tipSegLen;
    public int SegmentCount => segRest.Count;
    public Vector3 LastNode => nodes[nodes.Count - 1];
    /// <summary>Current (bent) direction of the forming tip segment.</summary>
    public Vector3 TipDirNow => nodeRot[nodeRot.Count - 1] * tipDir;
    public Vector3 TipPosition => LastNode + TipDirNow * tipSegLen;
    /// <summary>Rotation from rest shape to current shape for everything on segment `seg` (or at node `seg`).</summary>
    public Quaternion RotAt(int seg) => nodeRot[Mathf.Clamp(seg, 0, nodeRot.Count - 1)];

    public Branch(int seed, int depth, Branch parent, int attachSeg, float attachOffset, float attachArc,
                  Vector3 basePos, Vector3 dir, Vector3 normal, float birthTime)
    {
        this.seed = seed;
        this.depth = depth;
        this.parent = parent;
        this.attachSeg = attachSeg;
        this.attachOffset = attachOffset;
        this.attachArc = attachArc;
        this.birthTime = birthTime;
        dirRng = new System.Random(seed);
        spawnRng = new System.Random(unchecked(seed * 486187739 + 1));//different, but still seed-determined stream
        nodes.Add(basePos);
        nodeRadius.Add(0f);
        nodeBirth.Add(birthTime);
        nodeArc.Add(0f);
        nodeRadiusCap.Add(float.PositiveInfinity);
        jointBend.Add(Quaternion.identity);
        jointTarget.Add(Quaternion.identity);
        nodeRot.Add(parent != null ? parent.RotAt(attachSeg) : Quaternion.identity);
        bendUsed.Add(0f);
        nodeShift.Add(Vector3.zero);
        nodeShiftTarget.Add(Vector3.zero);
        shiftUsed.Add(0f);
        tipDir = dir;
        tipNormal = normal;
    }

    /// <summary>
    /// Grows the tip by up to dL (never past maxLength).
    /// Returns false if the branch has already formed all its tissue.
    /// </summary>
    public bool Grow(float dL, float maxLength, PlantSimulation sim)
    {
        if (stopped || blocked) return false;
        if (TotalLength >= maxLength)
        {
            if (finishTime < 0f) finishTime = sim.SimTime;
            return false;
        }
        finishTime = -1f;//growing (again, e.g. if maxLength was increased)
        PlantSettings s = sim.Settings;

        float remaining = Mathf.Min(dL, maxLength - TotalLength);
        contacts.Clear();
        //the tip moves in pieces: as far as it can go in its direction, and if it reaches an obstacle,
        //it bends at that point and continues sliding along the obstacle with the rest of this step's growth
        //(so the planned length is kept, only the direction changes). A few pieces at most (e.g. into a corner)
        for (int piece = 0; piece < 4 && remaining > 1e-7f; piece++)
        {
            float allowed = remaining;
            bool hit = false;
            Vector3 normal = Vector3.zero;
            if (sim.Obstacles != null &&
                sim.Obstacles.Cast(TipPosition, TipDirNow, s.obstacleClearance, remaining, out float hitDistance, out normal))
            {
                //stop a little short of touching, so a cast along the wall afterwards doesn't hit it again
                allowed = Mathf.Clamp(hitDistance - 0.1f * s.obstacleClearance, 0f, remaining);
                hit = true;
            }
            tipSegLen += allowed;
            remaining -= allowed;
            ProcessEvents(sim);//spawn checks and regular node commits along the piece just grown
            if (!hit) continue;

            //contact: end the current segment right here, so the bend happens exactly at the obstacle
            if (tipSegLen > 1e-5f) CommitSegment(tipSegLen, sim);
            contacts.Add(normal);
            Vector3 slide = SlideDirection(TipDirNow, sim);
            if (slide == Vector3.zero)
            {
                //no free direction left (e.g. pushed into a corner): the branch counts as finished
                blocked = true;
                finishTime = sim.SimTime;//from now on the flower delay counts
                return true;
            }
            SetTipDirNow(slide);
        }

        //full length reached: commit the last, shorter piece too, so it thickens like the rest
        //(otherwise it would stay a permanent spike with radius 0 at the end)
        if (TotalLength >= maxLength - 1e-6f)
        {
            if (tipSegLen > 1e-5f) CommitSegment(tipSegLen, sim);
            finishTime = sim.SimTime;//from now on the flower delay counts
        }
        return true;
    }

    /// <summary>Sets the tip's direction from a current (bent) direction: stored in the rest shape, reference axis carried along.</summary>
    public void SetTipDirNow(Vector3 dirNow)
    {
        Vector3 rest = Quaternion.Inverse(nodeRot[nodeRot.Count - 1]) * dirNow.normalized;
        tipNormal = Vector3.ProjectOnPlane(Quaternion.FromToRotation(tipDir, rest) * tipNormal, rest).normalized;
        tipDir = rest;
    }

    /// <summary>
    /// Direction along the obstacle(s) touched in this step (current shape): the planned direction with the parts
    /// pointing into the obstacles removed, then normalized again (full length, new direction).
    /// Head-on contact (nothing left of the planned direction): falls back to the light direction, then to
    /// the reference axis, each projected onto the obstacle. Zero = no free direction.
    /// </summary>
    Vector3 SlideDirection(Vector3 planned, PlantSimulation sim)
    {
        Vector3 toLight = sim.LightTarget.HasValue ? (sim.LightTarget.Value - TipPosition) : Vector3.up;
        Vector3 normalNow = nodeRot[nodeRot.Count - 1] * tipNormal;
        Vector3[] candidates = { planned, toLight, normalNow, -normalNow };
        foreach (Vector3 c in candidates)
        {
            Vector3 v = c;
            //remove the parts pointing into any touched obstacle (twice, so corners with two walls work out)
            for (int pass = 0; pass < 2; pass++)
                foreach (Vector3 n in contacts)
                    if (Vector3.Dot(v, n) < 0f) v = Vector3.ProjectOnPlane(v, n);
            if (v.sqrMagnitude > 0.05f * 0.05f * c.sqrMagnitude && c.sqrMagnitude > 1e-8f) return v.normalized;
        }
        return Vector3.zero;
    }

    /// <summary>
    /// Handles the events along the newly formed piece of branch, in order of their position:
    /// a node commit (every segmentLength) and a spawn check (every spawnCheckInterval),
    /// so a spawn check always sees the segment it actually lies on.
    /// </summary>
    void ProcessEvents(PlantSimulation sim)
    {
        PlantSettings s = sim.Settings;
        while (true)
        {
            float nodeAt = committedLength + PlantSettings.SegmentLength;
            if (nextSpawnCheck <= TotalLength && nextSpawnCheck <= nodeAt)//spawn check
            //needs to happen first, because at the same time a new segment could be reached,
            //but the leaf/branch needs to still take the old segment's orientation
            {
                //the check position lies on the current tip segment
                sim.SpawnCheck(this, nextSpawnCheck);
                nextSpawnCheck += s.spawnCheckInterval;
            }
            else if (nodeAt <= TotalLength)//commit segment, start new one
            {
                CommitSegment(PlantSettings.SegmentLength, sim);
                //new direction (light, noise) is decided in the current shape, then stored in the rest shape
                Vector3 next = sim.NextDirection(this);
                //if it would lead straight back into an obstacle the tip is sliding along (e.g. light behind the glass),
                //slide right away instead of committing a tiny segment at the obstacle (which makes a zigzag)
                if (sim.Obstacles != null &&
                    sim.Obstacles.Cast(TipPosition, next, s.obstacleClearance, 0.5f * PlantSettings.SegmentLength, out _, out Vector3 n))
                {
                    Vector3 along = Vector3.ProjectOnPlane(next, n);
                    if (along.sqrMagnitude > 0.05f * 0.05f) next = along.normalized;
                }
                SetTipDirNow(next);
            }
            else break;
        }
    }

    /// <summary>
    /// Ends tissue formation of an unfinished branch (the Growing phase ended before it finished).
    /// Its last piece is committed so it thickens like the rest; it never gets a bud.
    /// </summary>
    public void StopGrowth(PlantSimulation sim)
    {
        if (finishTime >= 0f || stopped) return;//already finished normally (or stopped)
        if (tipSegLen > 1e-5f) CommitSegment(tipSegLen, sim);
        stopped = true;
        flowerDecided = true;//only buds that already managed to appear can flower
    }

    /// <summary>Turns the first `length` of the growing tip into a committed segment.</summary>
    void CommitSegment(float length, PlantSimulation sim)
    {
        Vector3 dirNow = TipDirNow;
        segRest.Add(tipDir);
        segDir.Add(dirNow);
        segLength.Add(length);
        segBirth.Add(sim.SimTime);
        nodes.Add(LastNode + dirNow * length);//exact position follows in UpdateGeometry
        nodeRadius.Add(0f);
        nodeBirth.Add(sim.SimTime);
        nodeRadiusCap.Add(float.PositiveInfinity);
        jointBend.Add(Quaternion.identity);
        jointTarget.Add(Quaternion.identity);
        nodeRot.Add(nodeRot[nodeRot.Count - 1]);//no bend at the new node yet
        bendUsed.Add(0f);
        nodeShift.Add(Vector3.zero);
        nodeShiftTarget.Add(Vector3.zero);
        shiftUsed.Add(0f);
        committedLength += length;
        nodeArc.Add(committedLength);
        tipSegLen -= length;//remove already committed part, before tip continues growing
    }

    /// <summary>
    /// Recalculates bends, node positions and radii for the current sim time, from the base outward.
    /// The parent must already be up to date (guaranteed by updating branches in creation order).
    /// </summary>
    public void UpdateGeometry(PlantSimulation sim)
    {
        if (parent != null) nodes[0] = parent.PointAt(attachSeg, attachOffset);
        float t = sim.SimTime;

        //rotation after each node: everything before it (parent included) times this node's bend
        Quaternion rot = parent != null ? parent.RotAt(attachSeg) : Quaternion.identity;
        for (int i = 0; i < nodes.Count; i++)
        {
            //re-normalized after every combination: tiny rounding errors would otherwise grow until the rotation breaks
            rot = Quaternion.Normalize(rot * jointBend[i]);
            nodeRot[i] = rot;
        }

        //positions from the base outward: each segment in its current (bent) direction; then each node's own
        //sideways shift is added on top. The shift only moves that node (the segments next to it get a bit longer or
        //shorter), not what comes after it, so a dent stays local
        Vector3 unshifted = nodes[0];//the base never shifts: it stays attached to the parent / the ground
        for (int k = 0; k < segRest.Count; k++)
        {
            segDir[k] = nodeRot[k] * segRest[k];
            unshifted += segDir[k] * segLength[k];
            nodes[k + 1] = unshifted + nodeShift[k + 1];
        }
        for (int k = 0; k < nodes.Count; k++)
            nodeRadius[k] = Mathf.Min(sim.RadiusAt(this, nodeArc[k], t - nodeBirth[k]), nodeRadiusCap[k]);
    }

    // --- bending ---

    /// <summary>Starts a new step for the speed limit: every node may turn by up to its budget again.</summary>
    public void ResetBendBudget()
    {
        for (int i = 0; i < bendUsed.Count; i++) bendUsed[i] = 0f;
        for (int i = 0; i < shiftUsed.Count; i++) shiftUsed[i] = 0f;
    }

    /// <summary>
    /// Pushes segment `seg` away from an obstacle by `push`, by shifting its two end nodes sideways (and the nodes
    /// next to them by 60% and 25%, for a gentle dent instead of a kink). Each node moves at most stepBudget per step
    /// and at most maxShiftRadii times its own radius in total; the base node never moves.
    /// Returns the part of the push that couldn't be done this way (for bending to take over).
    /// </summary>
    public Vector3 ShiftAway(int seg, Vector3 push, float stepBudget, float maxShiftRadii, float extraShift)
    {
        Vector3 doneMin = push;//what both end nodes achieved (the segment only moves as far as its less-moved end)
        bool first = true;
        for (int j = seg - 2; j <= seg + 3; j++)
        {
            if (j < 1 || j >= nodes.Count) continue;//base node stays attached
            int distance = j <= seg ? seg - j : j - (seg + 1);//0 for the segment's own end nodes
            float weight = distance == 0 ? 1f : distance == 1 ? 0.6f : 0.25f;
            Vector3 want = push * weight;
            //speed limit
            float left = stepBudget - shiftUsed[j];
            if (left <= 0f) want = Vector3.zero;
            else if (want.magnitude > left) want = want.normalized * left;
            //size limit: at most a few times its own thickness away from where the branch's shape puts it
            float maxShift = maxShiftRadii * nodeRadius[j] + extraShift;
            Vector3 shifted = nodeShift[j] + want;
            if (shifted.magnitude > maxShift) shifted = shifted.normalized * maxShift;
            Vector3 applied = shifted - nodeShift[j];
            nodeShift[j] = shifted;
            shiftUsed[j] += applied.magnitude;
            if (distance == 0)
            {
                //how much of the push this end node achieved, along the push direction
                Vector3 achieved = push.sqrMagnitude > 1e-14f ? Vector3.Project(applied, push) : Vector3.zero;
                if (first || achieved.sqrMagnitude < doneMin.sqrMagnitude) doneMin = achieved;
                first = false;
            }
        }
        if (first) return push;//no node could move (e.g. the segment touches the base)
        Vector3 rest = push - doneMin;
        return Vector3.Dot(rest, push) > 0f ? rest : Vector3.zero;
    }

    /// <summary>
    /// Turns everything after node `node` by the rotation `turn` (given in the current shape, around that node).
    /// The node turns at most stepBudgetDeg per step in total (shared by all contacts pushing on it in this step),
    /// and its total bend stays within maxDeg. Returns the fraction of the requested turn that was applied.
    /// </summary>
    public float BendAtNode(int node, Quaternion turn, float maxDeg, float stepBudgetDeg)
    {
        float requested = Quaternion.Angle(Quaternion.identity, turn);
        if (requested < 1e-5f || float.IsNaN(requested)) return 0f;
        float left = stepBudgetDeg - bendUsed[node];
        if (left <= 1e-5f) return 0f;//this node has used up its turning for this step
        float fraction = Mathf.Min(1f, left / requested);//speed limit
        if (fraction < 1f) turn = Quaternion.Slerp(Quaternion.identity, turn, fraction);

        //express the turn in the node's own frame: new rotation after the node = turn * old rotation
        Quaternion before = node > 0 ? nodeRot[node - 1] : (parent != null ? parent.RotAt(attachSeg) : Quaternion.identity);
        Quaternion wanted = Quaternion.Normalize(Quaternion.Inverse(before) * turn * nodeRot[node]);
        float total = Quaternion.Angle(Quaternion.identity, wanted);
        if (total > maxDeg)
        {
            //only as far as the limit allows
            float current = Quaternion.Angle(Quaternion.identity, jointBend[node]);
            if (current >= maxDeg - 1e-3f) return 0f;
            float part = Mathf.Clamp01((maxDeg - current) / Mathf.Max(1e-4f, total - current));
            wanted = Quaternion.Normalize(Quaternion.Slerp(jointBend[node], wanted, part));
            fraction *= part;
        }
        if (float.IsNaN(wanted.x) || float.IsNaN(wanted.w)) return 0f;//never let an invalid rotation in
        jointBend[node] = wanted;
        bendUsed[node] += requested * fraction;
        return fraction;
    }

    /// <summary>
    /// Elastic + plastic behavior of all joints and shifts over a time step: bends/shifts relax toward their target
    /// (elastic springback), and the target drifts toward the current bend/shift (it becomes permanent).
    /// </summary>
    public void RelaxBends(float dt, float relaxTime, float settleTime, bool inContact)
    {
        //while (or shortly after) being pressed against something, springing back would only push it back in
        float relax = inContact ? 0f : 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, relaxTime));
        float settle = 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, settleTime));
        for (int i = 0; i < jointBend.Count; i++)
        {
            Quaternion b = jointBend[i], target = jointTarget[i];
            if (Quaternion.Angle(b, Quaternion.identity) < 1e-3f && Quaternion.Angle(target, Quaternion.identity) < 1e-3f) continue;
            jointTarget[i] = Quaternion.Normalize(Quaternion.Slerp(target, b, settle));
            jointBend[i] = Quaternion.Normalize(Quaternion.Slerp(b, jointTarget[i], relax));
        }
        //sideways shifts behave the same way
        for (int i = 0; i < nodeShift.Count; i++)
        {
            Vector3 sh = nodeShift[i], target = nodeShiftTarget[i];
            if (sh.sqrMagnitude < 1e-14f && target.sqrMagnitude < 1e-14f) continue;
            nodeShiftTarget[i] = Vector3.Lerp(target, sh, settle);
            nodeShift[i] = Vector3.Lerp(sh, nodeShiftTarget[i], relax);
        }
    }

    /// <summary>
    /// Is any obstacle near this branch? Box around its current nodes (+ tip) as one sphere query, with a margin.
    /// </summary>
    public bool NearObstacle(IObstacleField obstacles)
    {
        Vector3 min = nodes[0], max = nodes[0];
        foreach (Vector3 p in nodes) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        Vector3 tip = TipPosition;
        min = Vector3.Min(min, tip); max = Vector3.Max(max, tip);
        //margin: tip growth + thickness + organs on it
        float margin = 0.25f + TotalLength * 0.1f;
        return obstacles.AnyWithin((min + max) * 0.5f, (max - min).magnitude * 0.5f + margin);
    }

    /// <summary>Contact solving: bent in the current pass / bent or carried along (parent bent) in the last pass.</summary>
    public bool movedThisPass, affectedLastPass;

    /// <summary>Sim time this branch last had to give way to a contact (no elastic springback while still pressed).</summary>
    public float lastContactTime = float.NegativeInfinity;

    /// <summary>Does any node currently have a bend or shift (or a target to return to)?</summary>
    public bool HasBends
    {
        get
        {
            for (int i = 0; i < jointBend.Count; i++)
                if (jointBend[i] != Quaternion.identity || jointTarget[i] != Quaternion.identity) return true;
            for (int i = 0; i < nodeShift.Count; i++)
                if (nodeShift[i] != Vector3.zero || nodeShiftTarget[i] != Vector3.zero) return true;
            return false;
        }
    }

    /// <summary>Current position of the point at an offset within a segment (segment == SegmentCount is the tip segment).</summary>
    public Vector3 PointAt(int seg, float offset)
    {
        if (seg >= segRest.Count) return LastNode + TipDirNow * offset;//tip segment: still growing
        return Vector3.Lerp(nodes[seg], nodes[seg + 1], Mathf.Clamp01(offset / segLength[seg]));//between its (possibly shifted) ends
    }

    /// <summary>Current radius at an offset within a segment, interpolated between its two nodes.</summary>
    public float RadiusAt(int seg, float offset)
    {
        if (seg >= segRest.Count)//tip segment: from the last node's radius down to 0 at the tip
            return tipSegLen > 1e-6f ? nodeRadius[seg] * (1f - Mathf.Clamp01(offset / tipSegLen)) : 0f;
        return Mathf.Lerp(nodeRadius[seg], nodeRadius[seg + 1], Mathf.Clamp01(offset / segLength[seg]));
    }

    /// <summary>Unit direction perpendicular to the tip segment, at a given angle (degrees) around it (rest shape).</summary>
    public Vector3 OutwardAt(float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        Vector3 binormal = Vector3.Cross(tipDir, tipNormal);//to get a defined plane to spawn in
        return Mathf.Cos(a) * tipNormal + Mathf.Sin(a) * binormal;
    }
}
