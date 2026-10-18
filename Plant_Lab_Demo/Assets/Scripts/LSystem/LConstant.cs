using System;

/// <summary>A named number usable in L-system rules, with the range its slider (and Randomize) covers.</summary>
[Serializable]
public class LConstant
{
    public string name = "x";
    public float value = 1f;
    public float min = 0f;
    public float max = 10f;
}
