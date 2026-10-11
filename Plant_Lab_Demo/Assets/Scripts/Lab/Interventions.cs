using UnityEngine;

/// <summary>
/// The four intervention functions behind buttons A-D. Each press cycles that function through its levels.
/// Which button controls which function is hidden and permuted per participant (see ControlPanel).
/// For now this only keeps and reports the levels; the plants don't react to them yet.
/// </summary>
public class Interventions : MonoBehaviour
{
    public enum Function { Light, Temperature, Water, Fertiliser }

    [Tooltip("Levels per function; a press moves to the next level, after the last one back to the first.")]
    [Range(2, 6)] public int levels = 3;
    [Tooltip("Starting level of every function.")]
    public int startLevel = 1;

    /// <summary>Current level per function (index = (int)Function).</summary>
    public int[] Level { get; private set; }

    void Awake()
    {
        Level = new int[4];
        for (int i = 0; i < 4; i++) Level[i] = Mathf.Clamp(startLevel, 0, levels - 1);
    }

    /// <summary>Cycles a function to its next level.</summary>
    public void Cycle(Function f)
    {
        int i = (int)f;
        Level[i] = (Level[i] + 1) % levels;
    }
}
