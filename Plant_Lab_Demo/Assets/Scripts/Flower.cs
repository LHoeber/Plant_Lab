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
    public float rollDeg;       //random rotation around the flower's own axis, so flowers don't all line up
    public int variant;         //index into PlantSettings.flowerPrefabs (-1 if there are none)
    public bool setsFruit;      //decided at the start of the Fruiting phase: develops into a fruit (otherwise it withers then)
    public float witherStart = float.PositiveInfinity; //sim time withering reaches this flower (Fruiting start if no fruit, else Withering)
}
