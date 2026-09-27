using UnityEngine;

/// <summary>
/// Everything the simulation knows about one leaf. Plain data, no GameObject:
/// the visual is created and updated separately by PlantVisuals.
/// All fields are fixed facts, set once at spawn. The leaf's current position isn't stored,
/// because it moves as its segment elongates: it's always branch.PointAt(seg, offset).
/// </summary>
public class Leaf
{
    public Branch branch;       //branch the leaf grows on
    public int seg;             //segment of that branch the leaf is attached to
    public float offset;        //fresh distance from that segment's start (keeps its relative position as it stretches)
    public float arcFresh;      //fresh distance from the branch base (for the distance-from-base falloffs)
    public Vector3 tangent;     //branch direction at the attachment point
    public Vector3 outward;     //direction from the center line toward the leaf, perpendicular to tangent
    public float birthTime;     //sim time the leaf spawned
    public float sizeFactor;    //random per-leaf size multiplier
    public int variant;         //index into PlantSettings.leafPrefabs (-1 if there are none)
}
