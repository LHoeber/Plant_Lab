using UnityEngine;

/// <summary>
/// Everything the simulation knows about one flower. Plain data, no GameObject:
/// the visual (a prefab with FlowerMorph) is created and updated separately by PlantVisuals.
/// Classic: a flower sits at the end of a finished branch and points in that branch's last direction.
/// L-system: wherever a flower symbol is placed, pointing in the turtle's direction there.
/// </summary>
public class Flower
{
    public Branch branch;       //branch whose end the flower sits on
    public int seg;             //last segment of that branch at the time the flower spawned
    public float offset;        //offset in that segment = its full length (the end point)
    public float arcPos;        //distance from the branch base (the branch's end for classic flowers)
    public int indexOnBranch;   //0 for the first flower on its branch, 1 for the next, ... (separate random streams)
    public int index;           //position in PlantSimulation.Flowers (earlier flowers have the right of way)
    //self-collision: result and situation of the last check (an organ is only checked again when something changed)
    public bool selfTouching;
    public Vector3 selfCheckedPos;
    public Quaternion selfCheckedRot;
    public float selfCheckedScale = -1f;
    public OrganBox[] selfCheckedBoxes;//pose at the last check
    public float birthTime;     //sim time the bud appeared (it stays closed until the Flowering phase)
    public float sizeFactor;    //random per-flower size multiplier
    public Vector3 dir;         //direction the flower faces as grown: the branch's last direction (rest shape)
    public Quaternion tilt = Quaternion.identity; //its own tilt away from obstacles (rest shape)
    public float sizeCap = float.PositiveInfinity; //it got stuck at this size and stopped growing
    public readonly GrowthStop stop = new GrowthStop(); //when it may grow again after getting stuck
    public bool movedThisStep;  //contact solving: tilted in this step (check it again in the next pass)
    public float tiltUsed;      //degrees it has already tilted in the current step (speed limit)
    public float rollDeg;       //random rotation around the flower's own axis, so flowers don't all line up
    public int variant;         //index into PlantSettings.flowerPrefabs (-1 if there are none)
    public bool setsFruit;      //decided at the start of the Fruiting phase: develops into a fruit (otherwise it withers then)
    public float witherStart = float.PositiveInfinity; //sim time withering reaches this flower (Fruiting start if no fruit, else Withering)
}
