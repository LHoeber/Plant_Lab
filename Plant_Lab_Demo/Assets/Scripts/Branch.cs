using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One branch of the plant (the main stem is the branch with depth 0).
/// Plain C# class: pure simulation data and growth, no GameObjects, no rendering.
///
/// Growth has two parts:
/// - the tip forms new "fresh" tissue at growthSpeed; every segmentLength of fresh tissue a segment is committed
/// - every committed segment keeps elongating on its own clock (fast at first, then slowing down),
///   so positions are not fixed: they're recalculated from the base outward (UpdateGeometry).
/// Lengths called "fresh" are measured before any stretching.
/// </summary>
public class Branch
{
    public readonly int depth;           //branching level: 0 = main stem, 1 = side branch, ...
    public readonly Branch parent;       //null for the main stem
    public readonly int seed;            //this branch's own seed, derived from its parent's
    public readonly float birthTime;     //sim time the branch spawned

    //where on the parent this branch is attached: segment index + fresh distance from that segment's start.
    //the branch moves along with that point as the parent's segments elongate
    public readonly int attachSeg;
    public readonly float attachOffset;
    public readonly float attachArcFresh; //fresh distance from the parent's base to the attachment point

    //each branch has its own random streams, so what happens on one branch
    //never changes the random numbers another branch gets
    public readonly System.Random dirRng;   //direction noise
    public readonly System.Random spawnRng; //spawn decisions (leaf/branch, angles, sizes)

    // --- committed segments (segment k goes from node k to node k+1) ---
    public readonly List<Vector3> segDir = new List<Vector3>();      //direction, fixed at commit
    public readonly List<float> segFreshLength = new List<float>();  //length at commit (before stretching)
    public readonly List<float> segBirth = new List<float>();        //sim time of commit
    public readonly List<float> segScale = new List<float>();        //current stretch factor (cache, see UpdateGeometry)

    // --- nodes (node 0 = branch base) ---
    public readonly List<Vector3> nodes = new List<Vector3>();       //current positions, plant-local (cache, see UpdateGeometry)
    public readonly List<float> nodeRadius = new List<float>();      //current radius (cache, see UpdateGeometry)
    public readonly List<float> nodeBirth = new List<float>();       //sim time the node was formed
    public readonly List<float> nodeArcFresh = new List<float>();    //fresh distance from the branch base

    public readonly List<Branch> children = new List<Branch>();
    public readonly List<Leaf> leaves = new List<Leaf>();

    public Vector3 tipDir;          // direction of the segment currently forming
    public Vector3 tipNormal;       // parallel-transported reference axis perpendicular to tipDir (for spawn angles)
    public float tipSegLen;         // fresh length of the segment currently forming (doesn't stretch yet)
    public float committedLength;   // fresh length of all committed segments
    public float nextSpawnCheck;    // fresh branch length at which the next spawn check happens
    public float phyllotaxisAngle;  // angle around the branch (degrees) of the most recent leaf/branch

    public float FreshLength => committedLength + tipSegLen;
    public int SegmentCount => segDir.Count;
    public Vector3 LastNode => nodes[nodes.Count - 1];
    public Vector3 TipPosition => LastNode + tipDir * tipSegLen;

    public Branch(int seed, int depth, Branch parent, int attachSeg, float attachOffset, float attachArcFresh,
                  Vector3 basePos, Vector3 dir, Vector3 normal, float birthTime)
    {
        this.seed = seed;
        this.depth = depth;
        this.parent = parent;
        this.attachSeg = attachSeg;
        this.attachOffset = attachOffset;
        this.attachArcFresh = attachArcFresh;
        this.birthTime = birthTime;
        dirRng = new System.Random(seed);
        spawnRng = new System.Random(unchecked(seed * 486187739 + 1));//different, but still seed-determined stream
        nodes.Add(basePos);
        nodeRadius.Add(0f);
        nodeBirth.Add(birthTime);
        nodeArcFresh.Add(0f);
        tipDir = dir;
        tipNormal = normal;
    }

    /// <summary>
    /// Forms up to dL of fresh tissue at the tip (never past maxFreshLength).
    /// Returns false if the branch has already formed all its tissue.
    /// </summary>
    public bool Grow(float dL, float maxFreshLength, PlantSimulation sim)
    {
        if (FreshLength >= maxFreshLength) return false;
        PlantSettings s = sim.Settings;

        tipSegLen += Mathf.Min(dL, maxFreshLength - FreshLength);
        //two kinds of events can happen along the newly formed piece of branch:
        //a node commit (every segmentLength) and a spawn check (every spawnCheckInterval).
        //they're handled in order of their position along the branch, so a spawn check always
        //sees the segment it actually lies on
        while (true)
        {
            float nodeAt = committedLength + s.segmentLength;
            if (nextSpawnCheck <= FreshLength && nextSpawnCheck <= nodeAt)//spawn check
            //needs to happen first, because at the same time a new segment could be reached,
            //but the leaf/branch needs to still take the old segment's orientation
            {
                //the check position lies on the current tip segment
                sim.SpawnCheck(this, nextSpawnCheck);
                nextSpawnCheck += s.spawnCheckInterval;
            }
            else if (nodeAt <= FreshLength)//commit segment, start new one
            {
                segDir.Add(tipDir);
                segFreshLength.Add(s.segmentLength);
                segBirth.Add(sim.SimTime);
                segScale.Add(1f);
                nodes.Add(LastNode + tipDir * s.segmentLength);//exact position follows in UpdateGeometry
                nodeRadius.Add(0f);
                nodeBirth.Add(sim.SimTime);
                committedLength += s.segmentLength;
                nodeArcFresh.Add(committedLength);
                tipSegLen -= s.segmentLength;//remove already committed part, before tip continues growing

                Vector3 newDir = sim.NextDirection(this);
                //carry the reference axis along with the branch (parallel transport, like in the tube mesh),
                //then remove tiny floating point drift so it stays exactly perpendicular and unit length
                tipNormal = Vector3.ProjectOnPlane(Quaternion.FromToRotation(tipDir, newDir) * tipNormal, newDir).normalized;
                tipDir = newDir;
            }
            else break;
        }
        return true;
    }

    /// <summary>
    /// Recalculates stretch, node positions and radii for the current sim time, from the base outward.
    /// The parent must already be up to date (guaranteed by updating branches in creation order).
    /// </summary>
    public void UpdateGeometry(PlantSimulation sim)
    {
        if (parent != null) nodes[0] = parent.PointAt(attachSeg, attachOffset);
        float t = sim.SimTime;
        for (int k = 0; k < segDir.Count; k++)
        {
            segScale[k] = sim.StretchScale(t - segBirth[k]);
            nodes[k + 1] = nodes[k] + segDir[k] * (segFreshLength[k] * segScale[k]);
        }
        for (int k = 0; k < nodes.Count; k++)
            nodeRadius[k] = sim.RadiusAt(this, nodeArcFresh[k], t - nodeBirth[k]);
    }

    /// <summary>Current position of the point at a fresh offset within a segment (segment == SegmentCount is the tip segment).</summary>
    public Vector3 PointAt(int seg, float offset)
    {
        if (seg >= segDir.Count) return LastNode + tipDir * offset;//tip segment: fresh tissue, not stretched yet
        return nodes[seg] + segDir[seg] * (offset * segScale[seg]);
    }

    /// <summary>Current radius at a fresh offset within a segment, interpolated between its two nodes.</summary>
    public float RadiusAt(int seg, float offset)
    {
        if (seg >= segDir.Count)//tip segment: from the last node's radius down to 0 at the tip
            return tipSegLen > 1e-6f ? nodeRadius[seg] * (1f - Mathf.Clamp01(offset / tipSegLen)) : 0f;
        return Mathf.Lerp(nodeRadius[seg], nodeRadius[seg + 1], Mathf.Clamp01(offset / segFreshLength[seg]));
    }

    /// <summary>Unit direction perpendicular to the tip segment, at a given angle (degrees) around it.</summary>
    public Vector3 OutwardAt(float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        Vector3 binormal = Vector3.Cross(tipDir, tipNormal);//to get a defined plane to spawn in
        return Mathf.Cos(a) * tipNormal + Mathf.Sin(a) * binormal;
    }
}
