using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one folder per session (format "plantlab-log", version 1):
///   session.json  - who/what/how: participant, study, lab, build, settings of clock, panel, camera, plants, lamps;
///                   rewritten at the end with the end time and the number of completed trials.
///   trials.jsonl  - one line per completed trial: choice (buttons on, and their order), "?" presses, timing, images.
///   ticks.jsonl   - one line per tick during trials: action, camera view, world state (per plant, lamps).
///   events.jsonl  - everything else, with exact time: phase changes, "?", refused/blocked arrows, focus loss,
///                   slow ticks, trial start/end, session start/end.
///   images/       - the observation at the start and end of every trial (JPEG; probes to check replays against).
/// JSON Lines: one JSON object per line (Python: pandas.read_json(path, lines=True)).
///
/// Clocks in every record: "tick" (within the trial, the authoritative time), "t" (trial time at the end of that tick
/// = (tick + 1) x tickLength, exact), "wall" (real seconds since the session start, measured), "frame" (Unity frame).
/// Folder: in the editor Plant_Lab_Demo/SessionLogs/, in a build Application.persistentDataPath/SessionLogs/
/// (the Console prints the exact path at the start).
/// </summary>
[DefaultExecutionOrder(200)]//after TrialController (its plant list exists then)
public class SessionLogger : MonoBehaviour
{
    public const string Format = "plantlab-log";
    public const int FormatVersion = 1;

    [Header("Session")]
    [Tooltip("Participant ID (later from the study link, e.g. Prolific's PROLIFIC_PID).")]
    public string participantId = "test";
    public int sessionNumber = 1;
    [Tooltip("Study ID (later from the study link).")]
    public string studyId = "";
    [Tooltip("Which lab this scene is.")]
    public string labId = "lab1";

    [Header("Output")]
    public bool writeLogs = true;
    [Tooltip("Save the observation at the start and end of every trial.")]
    public bool probeImages = true;
    [Range(10, 100)] public int jpegQuality = 85;
    [Tooltip("A tick taking longer than this many tick lengths in real time is logged as a 'slow_tick' event.")]
    [Range(1.1f, 5f)] public float slowTickFactor = 1.5f;

    [Header("References (found automatically if empty)")]
    public TrialController trials;
    public TickClock clock;
    public ObservationCamera observationCamera;

    /// <summary>Folder of the current session's files.</summary>
    public string SessionFolder { get; private set; }

    StreamWriter ticksOut, trialsOut, eventsOut;
    readonly List<PlantGrowth> plants = new List<PlantGrowth>();
    GrowLamp[] lamps = new GrowLamp[0];
    string sessionId;
    DateTime startUtc;
    double wallStart;
    int trialsCompleted;
    bool ended;

    //current trial
    bool running;
    int trialIndex = -1;
    double choiceStart, trialStart, lastTickWall;
    int helpPresses, refusedInTrial;
    bool[] selection;
    int[] selectionOrder;
    string startImage;

    double Wall => Time.realtimeSinceStartupAsDouble - wallStart;

    void Start()
    {
        if (!writeLogs) { enabled = false; return; }
        if (trials == null) trials = FindFirstObjectByType<TrialController>();
        if (clock == null) clock = trials != null && trials.clock != null ? trials.clock : FindFirstObjectByType<TickClock>();
        if (observationCamera == null) observationCamera = trials != null && trials.observationCamera != null
            ? trials.observationCamera : FindFirstObjectByType<ObservationCamera>();
        if (trials == null || clock == null)
        {
            Debug.LogWarning("SessionLogger: no TrialController / TickClock found, nothing is logged.", this);
            enabled = false;
            return;
        }
        foreach (PlantGrowth p in trials.plants) if (p != null) plants.Add(p);
        lamps = FindObjectsByType<GrowLamp>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Array.Sort(lamps, (a, b) => string.CompareOrdinal(TrialController.HierarchyPath(a.transform), TrialController.HierarchyPath(b.transform)));

        wallStart = Time.realtimeSinceStartupAsDouble;
        startUtc = DateTime.UtcNow;
        sessionId = $"{Safe(participantId)}_s{sessionNumber}_{startUtc:yyyyMMdd-HHmmss}";
        string root = Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SessionLogs"))
            : Path.Combine(Application.persistentDataPath, "SessionLogs");
        SessionFolder = Path.Combine(root, sessionId);
        Directory.CreateDirectory(SessionFolder);
        if (probeImages) Directory.CreateDirectory(Path.Combine(SessionFolder, "images"));
        ticksOut = Open("ticks.jsonl");
        trialsOut = Open("trials.jsonl");
        eventsOut = Open("events.jsonl");
        WriteSessionFile(null);
        Event("session_start", null);
        Debug.Log($"SessionLogger: writing to {SessionFolder}", this);

        trials.TrialStarted += OnTrialStarted;
        trials.TrialEnded += OnTrialEnded;
        trials.PhaseChanged += OnPhaseChanged;
        trials.HelpToggled += OnHelp;
        trials.ArrowRefused += OnArrowRefused;
        trials.ArrowOutsideTrial += OnArrowOutsideTrial;
        clock.TickEnded += OnTick;
        choiceStart = Wall;
    }

    StreamWriter Open(string file) =>
        new StreamWriter(Path.Combine(SessionFolder, file), false, new UTF8Encoding(false)) { NewLine = "\n" };

    static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unknown";
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.ToString();
    }

    // --- records ---

    Json Stamp()
    {
        return new Json()
            .Add("trial", running ? trialIndex : (trials != null ? trials.TrialIndex : -1))
            .Add("tick", running ? clock.Tick : -1)
            .Add("wall", Math.Round(Wall, 6))
            .Add("frame", Time.frameCount);
    }

    void Event(string type, Action<Json> fill)
    {
        if (eventsOut == null) return;
        Json j = Stamp().Add("type", type);
        fill?.Invoke(j);
        eventsOut.WriteLine(j.ToString());
        eventsOut.Flush();
    }

    void OnTick(int tick, int action)
    {
        double wall = Wall;
        double duration = wall - lastTickWall;
        lastTickWall = wall;
        refusedInTrial += clock.RefusedThisTick;
        var j = new Json()
            .Add("trial", trialIndex)
            .Add("tick", tick)
            .Add("t", Math.Round((tick + 1) * (double)clock.tickLength, 6))
            .Add("wall", Math.Round(wall, 6))
            .Add("frame", Time.frameCount)
            .Add("action", action)
            .Add("refused", clock.RefusedThisTick)
            .Add("view", View())
            .Add("ticksLeft", trials.TrialTicks - (tick + 1))
            .Add("plants", PlantStates())
            .Add("lamps", LampStates());
        ticksOut.WriteLine(j.ToString());
        ticksOut.Flush();
        if (duration > slowTickFactor * clock.tickLength)
            Event("slow_tick", e => e.Add("ofTick", tick).Add("seconds", Math.Round(duration, 4)));
    }

    Json View()
    {
        if (observationCamera == null) return null;
        float az = Mathf.Repeat(observationCamera.azimuth, 360f);
        return new Json().Add("az", (float)Math.Round(az, 3)).Add("el", (float)Math.Round(observationCamera.elevation, 3));
    }

    IEnumerable<Json> PlantStates()
    {
        foreach (PlantGrowth p in plants)
        {
            PlantSimulation sim = p != null ? p.Simulation : null;
            if (sim == null) { yield return null; continue; }
            ulong physics = StateHash.Start;
            int fallen = p.Visuals != null ? p.Visuals.HashFallen(ref physics) : 0;
            yield return new Json()
                .Add("phase", sim.Phase.ToString())
                .Add("simT", (float)Math.Round(sim.SimTime, 4))
                .Add("height", (float)Math.Round(sim.Height(), 4))
                .Add("shoot", (float)Math.Round(sim.ShootLength(), 4))
                .Add("rootLen", (float)Math.Round(sim.RootLength(), 4))
                .Add("branches", sim.Branches.Count)
                .Add("leaves", sim.Leaves.Count)
                .Add("flowers", sim.Flowers.Count)
                .Add("segments", sim.CactusSegments.Count)
                .Add("roots", sim.Roots.Count)
                .Add("fallen", fallen)
                .Add("hash", StateHash.Hex(sim.Hash()))
                .Add("physHash", StateHash.Hex(physics));
        }
    }

    IEnumerable<bool> LampStates()
    {
        foreach (GrowLamp l in lamps) yield return l != null && l.IsOn;
    }

    // --- trial flow ---

    void OnTrialStarted(int trial, bool[] buttonsOn)
    {
        running = true;
        trialIndex = trial;
        trialStart = lastTickWall = Wall;
        refusedInTrial = 0;
        selection = buttonsOn;
        selectionOrder = new List<int>(trials.SelectionOrder).ToArray();
        startImage = SaveImage($"t{trial:000}_start.jpg");
        Event("trial_start", e => e.Add("selection", selection).Add("order", selectionOrder));
    }

    void OnTrialEnded(int trial)
    {
        string endImage = SaveImage($"t{trial:000}_end.jpg");
        double end = Wall;
        var j = new Json()
            .Add("trial", trial)
            .Add("lab", labId)
            .Add("selection", selection)
            .Add("order", selectionOrder)
            .Add("help", helpPresses)
            .Add("choiceStart", Math.Round(choiceStart, 6))
            .Add("start", Math.Round(trialStart, 6))
            .Add("end", Math.Round(end, 6))
            .Add("ticks", clock.Tick)
            .Add("refused", refusedInTrial)
            .Add("startImage", startImage)
            .Add("endImage", endImage);
        trialsOut.WriteLine(j.ToString());
        trialsOut.Flush();
        Event("trial_end", null);
        running = false;
        trialsCompleted++;
        helpPresses = 0;
        choiceStart = end;//the next choice starts now
        WriteSessionFile(null);
    }

    void OnPhaseChanged(TrialController.Phase phase) => Event("phase", e => e.Add("to", phase.ToString()));

    void OnHelp(bool open)
    {
        if (open) helpPresses++;
        Event("help", e => e.Add("open", open));
    }

    void OnArrowRefused(int action, string reason) => Event("arrow_refused", e => e.Add("action", action).Add("reason", reason));
    void OnArrowOutsideTrial(int action) => Event("arrow_outside_trial", e => e.Add("action", action));

    void OnApplicationFocus(bool focus) { if (enabled) Event("focus", e => e.Add("focused", focus)); }
    void OnApplicationPause(bool paused) { if (enabled) Event("pause", e => e.Add("paused", paused)); }

    string SaveImage(string file)
    {
        if (!probeImages || observationCamera == null) return null;
        foreach (PlantGrowth p in plants) if (p != null) p.RefreshVisuals(true);//exactly the current state
        Texture2D tex = observationCamera.Capture();
        try { File.WriteAllBytes(Path.Combine(SessionFolder, "images", file), tex.EncodeToJPG(jpegQuality)); }
        finally { Destroy(tex); }
        return "images/" + file;
    }

    // --- session file ---

    void WriteSessionFile(string endReason)
    {
        var j = new Json()
            .Add("format", Format)
            .Add("formatVersion", FormatVersion)
            .Add("sessionId", sessionId)
            .Add("participantId", participantId)
            .Add("sessionNumber", sessionNumber)
            .Add("studyId", studyId)
            .Add("labId", labId)
            .Add("startUtc", startUtc.ToString("o", CultureInfo.InvariantCulture))
            .Add("endUtc", endReason != null ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) : null)
            .Add("endReason", endReason)
            .Add("trialsCompleted", trialsCompleted)
            .Add("build", new Json()
                .Add("unity", Application.unityVersion)
                .Add("platform", Application.platform.ToString())
                .Add("appVersion", Application.version)
                .Add("scene", gameObject.scene.name))
            .Add("screen", new Json().Add("width", Screen.width).Add("height", Screen.height).Add("dpi", Screen.dpi))
            .Add("clock", new Json()
                .Add("tickLength", clock.tickLength)
                .Add("physicsStepsPerTick", clock.physicsStepsPerTick)
                .Add("ticksPerTrial", trials.TrialTicks)
                .Add("waitForAction", clock.waitForAction)
                .Add("simDeltaT", PlantSettings.DeltaT))
            .Add("actions", ActionNames())
            .Add("panel", PanelInfo())
            .Add("camera", CameraInfo())
            .Add("plants", PlantInfo())
            .Add("lamps", LampInfo());
        File.WriteAllText(Path.Combine(SessionFolder, "session.json"), j.ToString() + "\n", new UTF8Encoding(false));
    }

    static IEnumerable<Json> ActionNames()
    {
        for (int a = 0; a < LabActions.Count; a++) yield return new Json().Add("id", a).Add("name", LabActions.Name(a));
    }

    Json PanelInfo()
    {
        PanelConfig c = trials.config;
        var j = new Json()
            .Add("effectButtons", c.effectButtons)
            .Add("maxSelected", c.maxSelected)
            .Add("trials", c.trials)
            .Add("trialSeconds", c.trialSeconds);
        //positions of the effect buttons on the panel face (m, panel space), index = button index in the logs.
        //which effect each button triggers comes with the effects layer
        var buttons = new List<Json>();
        if (trials.panel != null)
            for (int i = 0; i < trials.panel.EffectButtons.Length; i++)
            {
                Vector3 p = trials.panel.EffectButtons[i].transform.localPosition;
                buttons.Add(new Json().Add("index", i).Add("x", (float)Math.Round(p.x, 4)).Add("y", (float)Math.Round(p.y, 4)));
            }
        return j.Add("buttonPositions", buttons).Add("buttonEffects", (string)null);
    }

    Json CameraInfo()
    {
        if (observationCamera == null) return null;
        ObservationCamera c = observationCamera;
        Camera cam = c.GetComponent<Camera>();
        return new Json()
            .Add("width", c.width).Add("height", c.height)
            .Add("fov", cam != null ? cam.fieldOfView : 0f)
            .Add("stepDeg", c.stepDeg)
            .Add("startAzimuth", c.StartAzimuth).Add("startElevation", c.StartElevation)
            .Add("minElevation", c.minElevation).Add("maxElevation", c.maxElevation)
            .Add("distance", c.distance).Add("centerHeight", c.centerHeight);
    }

    IEnumerable<Json> PlantInfo()
    {
        for (int i = 0; i < plants.Count; i++)
        {
            PlantGrowth p = plants[i];
            if (p == null) { yield return null; continue; }
            PlantSettings s = p.ActiveSettings;
            Vector3 pos = p.transform.position;
            yield return new Json()
                .Add("index", i)
                .Add("name", p.name)
                .Add("path", TrialController.HierarchyPath(p.transform))
                .Add("settings", s != null ? s.name : null)
                .Add("roots", s != null && s.roots != null ? s.roots.name : null)
                .Add("seed", p.seed)
                .Add("simSpeed", p.simSpeed)
                .Add("stepsPerTick", p.StepsPerTick(clock.tickLength))
                .Add("position", new Json().Add("x", pos.x).Add("y", pos.y).Add("z", pos.z));
        }
    }

    IEnumerable<Json> LampInfo()
    {
        for (int i = 0; i < lamps.Length; i++)
            if (lamps[i] != null)
                yield return new Json().Add("index", i).Add("path", TrialController.HierarchyPath(lamps[i].transform));
    }

    // --- end ---

    void EndSession(string reason)
    {
        if (ended || eventsOut == null) return;
        ended = true;
        if (running) Event("trial_aborted", null);
        Event("session_end", e => e.Add("reason", reason));
        WriteSessionFile(reason);
        ticksOut?.Dispose(); trialsOut?.Dispose(); eventsOut?.Dispose();
        ticksOut = trialsOut = eventsOut = null;
    }

    void OnApplicationQuit() => EndSession(trials != null && trials.CurrentPhase == TrialController.Phase.Finished ? "finished" : "quit");

    void OnDestroy()
    {
        EndSession("destroyed");
        if (trials != null)
        {
            trials.TrialStarted -= OnTrialStarted;
            trials.TrialEnded -= OnTrialEnded;
            trials.PhaseChanged -= OnPhaseChanged;
            trials.HelpToggled -= OnHelp;
            trials.ArrowRefused -= OnArrowRefused;
            trials.ArrowOutsideTrial -= OnArrowOutsideTrial;
        }
        if (clock != null) clock.TickEnded -= OnTick;
    }
}
