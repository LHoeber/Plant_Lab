using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The trial workflow on the control panel (paradigm v3):
///   Selecting: effect buttons switch on/off (at most maxSelected; one more switches off the one pressed longest ago).
///              The current LED row shows the choice live, its 'done' LED blinks. "?" shows the instructions.
///   Ready:     the shutter was tapped closed over the buttons: choice locked ("logged in", done LED steady),
///              Start uncovered. Tapping the shutter again reopens it.
///   Running:   Start resets the world (plants back to seedlings, same seeds; camera to the start view), the screen
///              shows the camera feed, the timer bar runs down, the arrows turn the camera. After trialSeconds:
///              camera offline, shutter opens, next row.
///   Finished:  all trials done.
/// Outside a trial the arrows only show a hint, and the world is paused.
/// For an agent/logging: History (the LED grid as numbers), RemainingTime, CurrentSelection, TrialStarted/TrialEnded.
/// </summary>
[DefaultExecutionOrder(100)]//after PanelBuilder and Console3D
public class TrialController : MonoBehaviour
{
    public enum Phase { Selecting, Ready, Running, Finished }

    [Tooltip("Taken from the PanelBuilder at start (the panel and the workflow always use the same config).")]
    public PanelConfig config;
    public PanelBuilder panel;
    public Console3D console;
    public ObservationCamera observationCamera;
    public TickClock clock;
    [Tooltip("Plants reset at every trial start and paused in between (empty = all plants in the scene).")]
    public List<PlantGrowth> plants = new List<PlantGrowth>();

    [Header("Look")]
    public Color screenTextColor = new Color(0.7f, 1f, 0.75f);
    [Tooltip("Glow of a switched-on effect button (HDR: above 1 = brighter).")]
    [ColorUsage(false, true)] public Color selectedGlow = new Color(1.6f, 0.15f, 0.08f);
    [Tooltip("Glow of Start / ? when they can be pressed.")]
    [ColorUsage(false, true)] public Color availableGlow = new Color(0.45f, 0.45f, 0.4f);
    public Color ledRedOn = new Color(1f, 0.12f, 0.08f), ledRedOff = new Color(0.18f, 0.03f, 0.03f);
    public Color ledWhiteOn = new Color(1f, 1f, 0.95f), ledWhiteOff = new Color(0.15f, 0.15f, 0.15f);
    [Tooltip("Seconds the 'camera not accessible' hint stays on the screen.")]
    public float hintSeconds = 2.5f;
    [Tooltip("Seconds the shutter takes to slide.")]
    public float shutterTime = 0.25f;

    public Phase CurrentPhase { get; private set; }
    /// <summary>The current trial (0-based): the one being chosen, logged in or running.</summary>
    public int TrialIndex { get; private set; }
    public float RemainingTime { get; private set; }
    /// <summary>Which effect buttons are on right now.</summary>
    public bool[] CurrentSelection => selection;
    /// <summary>The LED grid as numbers: one row per started trial, 1 = button was on.</summary>
    public IReadOnlyList<bool[]> History => history;

    /// <summary>A trial started: (trial index, buttons on).</summary>
    public event Action<int, bool[]> TrialStarted;
    /// <summary>A trial ended: (trial index).</summary>
    public event Action<int> TrialEnded;

    bool[] selection = new bool[0];
    readonly List<int> pressOrder = new List<int>();//switched-on buttons, oldest first
    readonly List<bool[]> history = new List<bool[]>();
    bool helpOpen = true;//the instructions are shown at the very start
    float hintUntil = -1f;
    float shutterT;//0 = open, 1 = closed (animated)
    int buttons, trials;

    Texture2D screenTex;
    Color32[] screenPx;
    string shownText;
    const int ScreenW = 288, ScreenH = 192;//pixel size of the screen texts (3:2, like the camera image)
    static readonly int FeedId = Shader.PropertyToID("_FeedAmount"), UITexId = Shader.PropertyToID("_UITex");

    void Start()
    {
        //one config for the whole panel: the one the panel is built from
        if (panel != null && panel.config != null) config = panel.config;
        if (plants.Count == 0) plants.AddRange(FindObjectsByType<PlantGrowth>());
        screenTex = PixelFont.MakeTexture(new Color32[ScreenW * ScreenH], ScreenW, ScreenH, "Screen texts");
        screenPx = new Color32[ScreenW * ScreenH];
        if (panel != null) panel.Rebuilt += Wire;
        Wire();
        ResetSession();
    }

    /// <summary>Starts the lab from scratch: no trials done, first row, shutter open, world paused.</summary>
    public void ResetSession()
    {
        buttons = config.effectButtons;
        trials = config.trials;
        selection = new bool[buttons];
        pressOrder.Clear();
        history.Clear();
        TrialIndex = 0;
        CurrentPhase = Phase.Selecting;
        helpOpen = true;
        shutterT = 0f;
        RemainingTime = config.trialSeconds;
        PauseWorld();
    }

    /// <summary>Connects the panel's buttons (again after every rebuild).</summary>
    void Wire()
    {
        if (panel == null) return;
        //a different number of buttons or trials: the old history doesn't fit anymore
        if (config.effectButtons != buttons || config.trials != trials) ResetSession();
        for (int i = 0; i < panel.EffectButtons.Length; i++)
        {
            int index = i;
            panel.EffectButtons[i].onPress = () => PressEffect(index);
        }
        for (int a = 0; a < panel.Arrows.Length; a++)
        {
            int action = a;
            if (panel.Arrows[a] != null) panel.Arrows[a].onPress = () => PressArrow(action);
        }
        if (panel.Shutter != null) panel.Shutter.onPress = PressShutter;
        if (panel.StartButton != null) panel.StartButton.onPress = PressStart;
        if (panel.HelpButton != null) panel.HelpButton.onPress = PressHelp;
        shownText = null;
    }

    // --- presses ---

    bool PressEffect(int i)
    {
        if (CurrentPhase != Phase.Selecting) return false;//locked under the shutter (or a trial is running)
        if (selection[i])
        {
            selection[i] = false;
            pressOrder.Remove(i);
        }
        else
        {
            selection[i] = true;
            pressOrder.Add(i);
            //too many: the one switched on longest ago goes off
            while (pressOrder.Count > config.maxSelected) { selection[pressOrder[0]] = false; pressOrder.RemoveAt(0); }
        }
        helpOpen = false;
        return true;
    }

    bool PressShutter()
    {
        if (CurrentPhase == Phase.Selecting) CurrentPhase = Phase.Ready;
        else if (CurrentPhase == Phase.Ready) CurrentPhase = Phase.Selecting;
        helpOpen = false;
        return false;//the slide is its feedback (the press feedback would move it)
    }

    bool PressStart()
    {
        if (CurrentPhase != Phase.Ready) return false;
        history.Add((bool[])selection.Clone());
        CurrentPhase = Phase.Running;
        RemainingTime = config.trialSeconds;
        helpOpen = false;
        //every trial starts from the same world: plants back to seedlings (same seeds), camera at the start view
        foreach (PlantGrowth p in plants)
            if (p != null) { p.enabled = true; p.ResetPlant(); }
        if (observationCamera != null) observationCamera.ResetView();
        if (clock != null) clock.enabled = true;
        TrialStarted?.Invoke(TrialIndex, (bool[])selection.Clone());
        return true;
    }

    bool PressHelp()
    {
        if (CurrentPhase == Phase.Running) return false;//not during a trial
        helpOpen = !helpOpen;
        return true;
    }

    bool PressArrow(int action)
    {
        if (CurrentPhase != Phase.Running)
        {
            hintUntil = Time.time + hintSeconds;
            return true;
        }
        //at the elevation limit, or a camera action was already taken in this tick: refused
        if (observationCamera == null || !observationCamera.CanStep(action)) return false;
        if (clock != null && !clock.Submit(action)) return false;
        observationCamera.Step(action);
        return true;
    }

    // --- every frame ---

    void Update()
    {
        if (config == null || panel == null) return;
        if (CurrentPhase == Phase.Running)
        {
            RemainingTime -= Time.deltaTime;
            if (RemainingTime <= 0f) EndTrial();
        }
        UpdateShutter();
        UpdateLights();
        UpdateScreen();
        panel.SetTimer(CurrentPhase == Phase.Running ? RemainingTime / Mathf.Max(0.01f, config.trialSeconds) : 1f);
    }

    void EndTrial()
    {
        RemainingTime = 0f;
        PauseWorld();
        TrialEnded?.Invoke(TrialIndex);
        TrialIndex++;
        Array.Clear(selection, 0, selection.Length);
        pressOrder.Clear();
        CurrentPhase = TrialIndex >= trials ? Phase.Finished : Phase.Selecting;//shutter opens again for the next choice
        RemainingTime = config.trialSeconds;
    }

    void PauseWorld()
    {
        foreach (PlantGrowth p in plants) if (p != null) p.enabled = false;
        if (clock != null) clock.enabled = false;
    }

    void UpdateShutter()
    {
        if (panel.Shutter == null) return;
        float target = CurrentPhase == Phase.Selecting || CurrentPhase == Phase.Finished ? 0f : 1f;
        shutterT = Mathf.MoveTowards(shutterT, target, Time.deltaTime / Mathf.Max(0.01f, shutterTime));
        float eased = shutterT * shutterT * (3f - 2f * shutterT);
        panel.Shutter.transform.localPosition = Vector3.Lerp(panel.ShutterOpen, panel.ShutterClosed, eased);
    }

    void UpdateLights()
    {
        for (int i = 0; i < panel.EffectButtons.Length && i < selection.Length; i++)
            panel.EffectButtons[i].SetGlow(selection[i] ? selectedGlow : Color.black);
        if (panel.StartButton != null) panel.StartButton.SetGlow(CurrentPhase == Phase.Ready ? availableGlow : Color.black);
        if (panel.HelpButton != null)
            panel.HelpButton.SetGlow(CurrentPhase == Phase.Selecting || CurrentPhase == Phase.Ready ? availableGlow : Color.black);

        bool blinkOn = Time.time % 1f < 0.5f;
        int rows = Mathf.Min(trials, panel.DoneLeds.Length);
        for (int t = 0; t < rows; t++)
        {
            bool done = t < history.Count;//started (logged in and run) trials
            bool current = t == TrialIndex && CurrentPhase != Phase.Finished;
            bool[] row = done ? history[t] : current ? selection : null;
            for (int b = 0; b < buttons && b < panel.ButtonLeds.GetLength(1); b++)
                panel.SetColor(panel.ButtonLeds[t, b], row != null && row[b] ? ledRedOn : ledRedOff);
            bool white = done || (current && (CurrentPhase != Phase.Selecting || blinkOn));//blinks until logged in
            panel.SetColor(panel.DoneLeds[t], white ? ledWhiteOn : ledWhiteOff);
        }
    }

    void UpdateScreen()
    {
        Material m = console != null ? console.ScreenMaterial : null;
        if (m == null) return;
        bool feed = CurrentPhase == Phase.Running;
        m.SetFloat(FeedId, feed ? 1f : 0f);
        if (feed) return;

        int trialNumber = Mathf.Min(TrialIndex + 1, trials);
        string text = Time.time < hintUntil ? config.notAccessible
                    : helpOpen ? config.instructions
                    : CurrentPhase == Phase.Finished ? config.finished
                    : CurrentPhase == Phase.Ready ? config.ready
                    : config.offline;
        text = config.Fill(text, trialNumber);
        if (text == shownText) return;//only redrawn when it changes
        shownText = text;
        Array.Clear(screenPx, 0, screenPx.Length);
        const int margin = 12;
        string wrapped = PixelFont.Wrap(text, (ScreenW - 2 * margin) / PixelFont.CellWidth);
        int lines = wrapped.Split('\n').Length;
        int top = Mathf.Max(margin, (ScreenH - lines * PixelFont.CellHeight) / 2);//vertically centered
        PixelFont.Draw(screenPx, ScreenW, ScreenH, wrapped, margin, top, 1, screenTextColor);
        screenTex.SetPixels32(screenPx);
        screenTex.Apply(false);
        m.SetTexture(UITexId, screenTex);
    }

    void OnDestroy()
    {
        if (panel != null) panel.Rebuilt -= Wire;
        if (screenTex != null) Destroy(screenTex);
    }
}
