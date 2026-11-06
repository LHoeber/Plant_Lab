using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything the simulation knows about one cactus segment (placed by S(s) in the rules). Plain data, no GameObject:
/// the visual is built by CactusVisuals, the shape comes from PlantSettings.cactusSegment.
/// Attached to a branch (the first segment, usually at the plant's base), to its parent segment's tip (S S), or to one
/// of its parent's areoles (~ S), whose tuft it replaces.
/// </summary>
public class CactusSegment
{
    public int index;             //position in PlantSimulation.CactusSegments (parents before children; younger ones give way)
    public int seed;              //tuft jitter and turns
    public System.Random rng;     //rule choices and rand() in what follows its S
    public float size;            //size factor s of S(s): the whole segment is scaled by it
    public int depth;             //0 for a segment on a branch, +1 per segment in between

    //attachment
    public CactusSegment parent;  //null: on a branch
    public Branch branch;         //the branch it sits on (only without a parent)
    public int branchSeg;
    public float branchOffset;
    public int rib = -1, areole = -1;//the parent's areole it grows out of (rib, number along it); -1: the parent's tip / the branch
    public Quaternion restFrame;  //its frame as placed (segment y = H = axis, z = U, x = L): on a branch in plant space (rest
                                  //shape), on the parent's tip in the parent's segment space, on an areole relative to the
                                  //areole's frame (which follows the parent's surface while it grows and withers)
    public float baseDistance;    //distance from the plant's base along the plant (for the withering wave)

    //growth
    public float birthTime;
    public float growth;          //0 = bud .. 1 = full grown; only advances in the Growing phase and while not stuck
    public List<LModule> program; //what follows its S in the rules: run once it reaches the child threshold (null = done)
    public readonly List<PendingChild> pending = new List<PendingChild>();//children waiting for their time / areole
    public readonly HashSet<long> reserved = new HashSet<long>();  //areoles chosen by ~ (children or flowers)
    public readonly HashSet<long> occupied = new HashSet<long>();  //areoles a child grows out of now: no tuft there
    public readonly List<CactusSegment> children = new List<CactusSegment>();
    public List<CactusShape.SurfacePoint> fullAreoles;//its areoles at full growth (for choosing with ~)
    public float witherStart = float.PositiveInfinity;//sim time the withering wave reaches it

    //contacts
    public Quaternion tilt = Quaternion.identity;//its own tilt at the attachment, away from obstacles (in the joint's frame)
    public float tiltUsed;        //degrees it has already tilted in the current step (speed limit)
    public bool touching;         //touched something in the last contact pass
    public bool stuck;            //stopped growing because it still touched after tilting
    public readonly GrowthStop stop = new GrowthStop();

    //current pose (cache, see PlantSimulation.UpdateCactusGeometry): segment space x size -> plant space
    public Vector3 position;      //its base (attachment point)
    public Quaternion rotation;   //segment axes: x = L, y = H (axis to the tip), z = U
    public Quaternion jointRotation;//the frame its tilt is expressed in (parent's rotation, or the branch's bend)

    //collision cache (see PlantSimulation): rough body and surface samples for the current growth/wither state
    public float bodyA, bodyB, bodyC, bodyColumn;
    public readonly List<Vector3> localSamples = new List<Vector3>();
    public readonly List<Vector3> samples = new List<Vector3>();//the same in plant space (current pose)
    public float samplesGrowth = -1f, samplesWither = -1f;

    /// <summary>Smallest growth shown and used for collisions: a brand-new bud is a small ball, not a point.</summary>
    public const float MinGrowth = 0.03f;
    public float ShownGrowth => Mathf.Max(MinGrowth, growth);

    /// <summary>Grown to full size, rules done, all children placed.</summary>
    public bool Finished => growth >= 1f && program == null && pending.Count == 0;

    /// <summary>Key for an areole id in the reserved/occupied sets.</summary>
    public static long Key(int rib, int index) => ((long)rib << 32) | (uint)index;
}

/// <summary>A child segment decided by the rules, waiting until its time has come and its areole exists.</summary>
public class PendingChild
{
    public float size;
    public int rib = -1, areole = -1;//-1: at the parent's tip
    public Quaternion restFrame;      //like CactusSegment.restFrame
    public List<LModule> program;    //what follows its S
    public float readyTime;          //not before this sim time (children of one segment appear one after another)
}
