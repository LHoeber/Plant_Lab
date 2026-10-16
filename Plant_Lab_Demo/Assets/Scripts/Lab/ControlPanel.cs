using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The console: shows the observation on the screen and turns button presses into actions, at most one per tick.
/// Per participant (seed), the intervention channels are randomly assigned to the intervention buttons.
/// The buttons look identical, so a button's only identity is its position: one permutation is enough.
/// (With visually distinct buttons, e.g. colors, their positions would have to be permuted independently as well.)
/// The seed is recorded (Console now, the log later). The panel itself never changes except for the brief press
/// feedback: anything that changed would be an observation the agent would also need.
/// </summary>
public class ControlPanel : MonoBehaviour
{
    public TickClock clock;
    public ObservationCamera observationCamera;
    public Interventions interventions;
    [Tooltip("2D console only: screen image that shows the observation. (The 3D console sets up its screen itself.)")]
    public RawImage screen;

    [Header("Per-participant randomization")]
    [Tooltip("Seed for assigning the channels to the buttons. -1 = draw a new one at start " +
             "(it's printed, and kept in the field while playing).")]
    public int permutationSeed = -1;

    /// <summary>Channel controlled by intervention button i (A, B, ... in their fixed panel positions).</summary>
    public int[] ChannelOfButton { get; private set; } = new int[LabActions.InterventionCount];

    void Start()
    {
        if (permutationSeed < 0) permutationSeed = new System.Random().Next(0, int.MaxValue);
        ChannelOfButton = Shuffled(LabActions.InterventionCount, new System.Random(permutationSeed));
        if (screen != null && observationCamera != null) screen.texture = observationCamera.Observation;
        var mapping = new System.Text.StringBuilder();
        for (int i = 0; i < ChannelOfButton.Length; i++)
            mapping.Append($"{(i > 0 ? ", " : "")}{LabActions.ButtonLetter(i)} -> " +
                           (interventions != null ? interventions.ChannelName(ChannelOfButton[i]) : $"Channel {ChannelOfButton[i] + 1}"));
        Debug.Log($"ControlPanel: permutation seed {permutationSeed}. {mapping}", this);
    }

    static int[] Shuffled(int n, System.Random rng)
    {
        int[] a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        for (int i = a.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        return a;
    }

    /// <summary>
    /// A button was pressed. Returns true if the action was accepted (first in this tick, and possible),
    /// which is when the button shows its press feedback.
    /// </summary>
    public bool Press(int action)
    {
        //camera up/down at the elevation limit: refused like a masked action, so no feedback either
        if (LabActions.IsCamera(action) && (observationCamera == null || !observationCamera.CanStep(action))) return false;
        if (clock == null || !clock.Submit(action)) return false;
        if (LabActions.IsCamera(action)) observationCamera.Step(action);
        else if (LabActions.IsIntervention(action) && interventions != null)
            interventions.Cycle(ChannelOfButton[LabActions.ButtonIndex(action)]);
        return true;
    }
}
