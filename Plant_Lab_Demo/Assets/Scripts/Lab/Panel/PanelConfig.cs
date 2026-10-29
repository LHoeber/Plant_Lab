using UnityEngine;

/// <summary>
/// One experimental condition of the control panel: how many effect buttons, how many trials, how many buttons may be
/// on at once, how long a trial runs. The panel (buttons, LED grid) is built from this at Play start.
/// Create via: Project window -> right-click -> Create -> Botanic Lab -> Panel Config.
/// </summary>
[CreateAssetMenu(fileName = "PanelConfig", menuName = "Botanic Lab/Panel Config")]
public class PanelConfig : ScriptableObject
{
    [Tooltip("Number of effect buttons (numbered 1, 2, ... on the panel and in the LED grid).")]
    [Range(1, 8)] public int effectButtons = 4;
    [Tooltip("Trials per lab = rows of the LED grid.")]
    [Range(1, 30)] public int trials = 13;
    [Tooltip("At most this many buttons can be on at once; pressing another one switches off the one pressed longest ago.")]
    [Range(1, 8)] public int maxSelected = 2;
    [Tooltip("Real seconds a trial runs (the plants' growth speed is simSpeed on each PlantGrowth).")]
    [Range(5f, 600f)] public float trialSeconds = 60f;

    [Header("Screen texts ({max}, {trials}, {trial}, {buttons} are filled in)")]
    [TextArea(6, 20)] public string instructions =
        "WELCOME TO THE LAB\n\n" +
        "THE BUTTONS 1-{buttons} SWITCH DEVICES IN THE GROWTH CHAMBER ON. YOU CAN SWITCH ON UP TO {max} AT ONCE.\n\n" +
        "CLOSE THE SHUTTER TO LOG IN YOUR CHOICE, THEN PRESS START. A NEW CULTURE GROWS FOR EVERY TRIAL.\n\n" +
        "DURING A TRIAL, TURN THE CAMERA WITH THE ARROWS.\n\n" +
        "YOU HAVE {trials} TRIALS. PRESS ? TO READ THIS AGAIN.";
    [TextArea(2, 6)] public string offline = "CAMERA OFFLINE\n\nTRIAL {trial} OF {trials}\n\nSELECT UP TO {max} BUTTONS,\nCLOSE THE SHUTTER, PRESS START.";
    [TextArea(2, 6)] public string ready = "CAMERA OFFLINE\n\nTRIAL {trial} OF {trials}\n\nCHOICE LOGGED IN.\nPRESS START.";
    [TextArea(2, 6)] public string notAccessible = "CAMERA NOT ACCESSIBLE\n\nTRIAL HAS NOT STARTED.";
    [TextArea(2, 6)] public string finished = "LAB COMPLETE\n\nALL {trials} TRIALS DONE.";

    /// <summary>Increases whenever a value is changed in the Inspector (the panel rebuilds itself while playing).</summary>
    public int Version { get; private set; }
    void OnValidate() { maxSelected = Mathf.Min(maxSelected, effectButtons); Version++; }

    public string Fill(string text, int trial) => text
        .Replace("{max}", maxSelected.ToString()).Replace("{trials}", trials.ToString())
        .Replace("{trial}", trial.ToString()).Replace("{buttons}", effectButtons.ToString());
}
