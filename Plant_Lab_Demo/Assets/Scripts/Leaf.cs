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
    public Vector3 tangent;     //branch direction at the attachment point (rest shape, i.e. as grown)
    public Vector3 outward;     //direction from the center line toward the leaf, perpendicular to tangent (rest shape)
    public float birthTime;     //sim time the leaf spawned
    public float sizeFactor;    //random per-leaf size multiplier
    public Quaternion tilt = Quaternion.identity; //how it has turned at its base to get away from obstacles (rest shape)
    public float sizeCap = float.PositiveInfinity; //it got stuck at this size and stopped growing
    public bool movedThisStep;  //contact solving: tilted in this step (check it again in the next pass)
    public float tiltUsed;      //degrees it has already tilted in the current step (speed limit)
    public int variant;         //index into PlantSettings.leafPrefabs (-1 if there are none)
    public float witherStart = float.PositiveInfinity; //sim time withering reaches this leaf (set when the Withering phase starts)
}
