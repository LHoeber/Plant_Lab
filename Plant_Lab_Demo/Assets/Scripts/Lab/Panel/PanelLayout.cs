using UnityEngine;

/// <summary>
/// Where things sit on the control panel's face, in meters: x to the right, y up, origin = center of the panel face.
/// Each group is fitted into its rectangle (e.g. any number of effect buttons into Buttons, any number of trials into
/// History). An asset, so changes made while playing are kept.
/// Create via: Project window -> right-click -> Create -> Botanic Lab -> Panel Layout.
/// </summary>
[CreateAssetMenu(fileName = "PanelLayout", menuName = "Botanic Lab/Panel Layout")]
public class PanelLayout : ScriptableObject
{
    [Tooltip("Size of the panel face (width, height); the console camera frames this.")]
    public Vector2 casingSize = new Vector2(0.8f, 0.45f);
    public float casingDepth = 0.04f;
    [Tooltip("The screen (keep its aspect equal to the camera image, 3:2).")]
    public Rect screen = Rect.MinMaxRect(-0.3667f, -0.1667f, 0.1333f, 0.1667f);
    public Rect start = Rect.MinMaxRect(0.165f, 0.14f, 0.295f, 0.19f);
    public Rect help = Rect.MinMaxRect(0.305f, 0.14f, 0.375f, 0.19f);
    [Tooltip("The timer bar (runs down from right to left during a trial).")]
    public Rect timer = Rect.MinMaxRect(0.165f, 0.092f, 0.295f, 0.108f);
    [Tooltip("The effect buttons are arranged in a grid inside this; the closed shutter covers it.")]
    public Rect buttons = Rect.MinMaxRect(0.165f, -0.07f, 0.295f, 0.06f);
    [Tooltip("The LED grid: one row per trial, one column per button, plus the 'trial done' column.")]
    public Rect history = Rect.MinMaxRect(0.305f, -0.07f, 0.375f, 0.06f);
    [Tooltip("The four camera arrows, as a cross.")]
    public Rect arrows = Rect.MinMaxRect(0.165f, -0.215f, 0.375f, -0.085f);
    [Tooltip("How far the shutter slides up from closed (over the buttons) to open (over Start).")]
    public float shutterTravel = 0.145f;

    [Header("Heights (how far parts stand out of the face)")]
    public float buttonHeight = 0.012f;
    public float socketHeight = 0.004f;

    public int Version { get; private set; }
    void OnValidate() => Version++;
}
