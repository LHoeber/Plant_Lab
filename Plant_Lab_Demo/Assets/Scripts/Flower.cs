using UnityEngine;

/// <summary>
/// Everything the simulation knows about one flower. Plain data, no GameObject:
/// the visual (a prefab with FlowerMorph) is created and updated separately by PlantVisuals.
/// A flower sits at the end of a finished branch and points in that branch's last direction.
/// </summary>
public class Flower
{
    public Branch branch;       //branch whose end the flower sits on
    public int seg;             //last segment of that branch at the time the flower spawned
    public float offset;        //fresh offset in that segment = its full fresh length (the end point)
    public float birthTime;     //sim time the bud appeared (it stays closed until the Flowering phase)
    public float sizeFactor;    //random per-flower size multiplier
    public Vector3 dir;         //direction the flower faces as grown: the branch's last direction (rest shape)
    public Quaternion tilt = Quaternion.identity; //its own tilt away from obstacles (rest shape)
    public float sizeCap = float.PositiveInfinity; //it got stuck at this size and stopped growing
    public bool movedThisStep;  //contact solving: tilted in this step (check it again in the next pass)
    public float tiltUsed;      //degrees it has already tilted in the current step (speed limit)
    public float rollDeg;       //random rotation around the flower's own axis, so flowers don't all line up
    public int variant;         //index into PlantSettings.flowerPrefabs (-1 if there are none)
    public bool setsFruit;      //decided at the start of the Fruiting phase: develops into a fruit (otherwise it withers then)
    public float witherStart = float.PositiveInfinity; //sim time withering reaches this flower (Fruiting start if no fruit, else Withering)
}
