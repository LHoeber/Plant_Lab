using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The decision clock (default 0.5 s) and, during a trial, the world's only clock.
///
/// Input: only the first action in each tick counts; later ones in the same tick are refused (the caller shows press
/// feedback only for accepted ones). A tick without input is a no-op, reported like any other action.
///
/// World (determinism): during a trial the plants and Unity's physics don't run on frame time. This clock steps them
/// in lockstep: each tick consists of physicsStepsPerTick substeps; in each substep every plant does its share of its
/// steps per tick (from its simSpeed), then physics advances one fixed step. Within a tick this is spread over the
/// frames (smooth to watch); at the tick's end the rest is done at once, so every tick ends in exactly the same state
/// whatever the frame rate. A slow computer makes the ticks take longer in real time, it never changes the world
/// (the logger records the real time of every tick, so this stays visible).
///
/// Agents: with waitForAction the clock holds at the end of each tick until an action is submitted (Submit, also
/// NoOp), so a slow model gets as long as it needs; the world doesn't move meanwhile.
/// </summary>
[DefaultExecutionOrder(-50)]//before the plants' and the camera's Update, so they show this frame's state
public class TickClock : MonoBehaviour
{
    [Tooltip("Seconds per tick (decision and observation clock). 0.5 s default, 1 s is the alternative.")]
    [Range(0.1f, 2f)] public float tickLength = 0.5f;
    [Tooltip("Physics steps per tick (each tickLength / this seconds long). 25 at 0.5 s = Unity's usual 0.02 s.")]
    [Range(5, 100)] public int physicsStepsPerTick = 25;
    [Tooltip("Agents: each tick waits until an action was submitted (Submit, NoOp included). Off for humans.")]
    public bool waitForAction = false;
    [Tooltip("Milliseconds per frame the world may use to catch up within a tick (the rest follows in later frames; " +
             "the tick's end is always completed).")]
    [Range(2f, 100f)] public float frameBudgetMs = 12f;
    [Tooltip("Print every tick to the Console (for testing).")]
    public bool logTicks = false;

    /// <summary>Is a trial running (the world is stepped by this clock)?</summary>
    public bool Running { get; private set; }
    /// <summary>Index of the tick currently collecting input, counted from 0 in each trial.</summary>
    public int Tick { get; private set; }
    /// <summary>How far the current tick has progressed (0..1, in game time).</summary>
    public float TickFraction => Mathf.Clamp01(timer / tickLength);
    /// <summary>Action accepted in the current tick (NoOp if none yet).</summary>
    public int CurrentAction { get; private set; } = LabActions.NoOp;
    /// <summary>Inputs refused in the current tick (a second action, or one that wasn't possible).</summary>
    public int RefusedThisTick { get; private set; }
    /// <summary>The plants this clock steps in the current trial, in stepping order.</summary>
    public IReadOnlyList<PlantGrowth> Plants => plants;

    /// <summary>
    /// End of a tick: (tick index, action taken in it). The world is exactly in its end-of-tick state here
    /// (simulation and physics complete, visuals refreshed). Logging and agents hook in here.
    /// </summary>
    public event Action<int, int> TickEnded;
    /// <summary>After TickEnded (flow control, e.g. ending the trial after its last tick).</summary>
    public event Action<int, int> AfterTickEnded;

    readonly List<PlantGrowth> plants = new List<PlantGrowth>();
    int[] stepsPerTick = new int[0];
    int[] stepsDone = new int[0];//plant steps done in the current tick
    int substepsDone;            //substeps done in the current tick
    float timer;
    bool submitted;
    SimulationMode previousMode;

    /// <summary>Starts stepping the world for a trial (after the plants and camera were reset).</summary>
    public void BeginTrial(IEnumerable<PlantGrowth> trialPlants)
    {
        plants.Clear();
        foreach (PlantGrowth p in trialPlants) if (p != null) plants.Add(p);
        stepsPerTick = new int[plants.Count];
        stepsDone = new int[plants.Count];
        for (int i = 0; i < plants.Count; i++)
        {
            stepsPerTick[i] = plants[i].StepsPerTick(tickLength);
            plants[i].SetDriven(true);
            plants[i].RefreshVisuals();
        }
        previousMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;//physics only advances when this clock says so
        Tick = 0;
        timer = 0f;
        NewTick();
        Running = true;
        enabled = true;
    }

    /// <summary>Stops stepping the world (end of the trial): plants and physics go back to their normal clocks.</summary>
    public void EndTrial()
    {
        if (!Running) return;
        Running = false;
        foreach (PlantGrowth p in plants) if (p != null) p.SetDriven(false);
        Physics.simulationMode = previousMode;
    }

    /// <summary>Offers an action for the current tick. True if it's the first one (accepted), false if refused.</summary>
    public bool Submit(int action)
    {
        if (!Running) return false;
        if (submitted) { RefusedThisTick++; return false; }//first action in this tick wins
        submitted = true;
        CurrentAction = action;
        return true;
    }

    /// <summary>Counts an input that was refused before reaching Submit (e.g. camera at its elevation limit).</summary>
    public void ReportRefused() { if (Running) RefusedThisTick++; }

    void NewTick()
    {
        CurrentAction = LabActions.NoOp;
        RefusedThisTick = 0;
        submitted = false;
        substepsDone = 0;
        Array.Clear(stepsDone, 0, stepsDone.Length);
    }

    void Update()
    {
        if (!Running) return;
        timer += Time.deltaTime;
        if (waitForAction && !submitted) timer = Mathf.Min(timer, tickLength);//hold at the tick's end

        //progress within the tick, spread over the frames (the last substep belongs to the tick's end)
        int target = Mathf.Min(physicsStepsPerTick - 1, Mathf.FloorToInt(physicsStepsPerTick * TickFraction));
        AdvanceTo(target, true);

        while (Running && timer >= tickLength && (!waitForAction || submitted))
        {
            AdvanceTo(physicsStepsPerTick, false);//exactly the tick's end, however long it takes
            foreach (PlantGrowth p in plants) if (p != null) p.RefreshVisuals();
            timer -= tickLength;
            int tick = Tick, action = CurrentAction;
            if (logTicks) Debug.Log($"tick {tick}: {LabActions.Name(action)}");
            TickEnded?.Invoke(tick, action);
            Tick++;
            NewTick();
            AfterTickEnded?.Invoke(tick, action);//may end the trial
        }
    }

    /// <summary>Does substeps until `substep` substeps of this tick are done (with a time budget if `budget`).</summary>
    void AdvanceTo(int substep, bool budget)
    {
        var watch = budget ? System.Diagnostics.Stopwatch.StartNew() : null;
        float physicsDt = tickLength / physicsStepsPerTick;
        while (substepsDone < substep)
        {
            int k = substepsDone + 1;
            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i] == null) continue;
                int want = stepsPerTick[i] * k / physicsStepsPerTick;//integer: the steps are distributed exactly
                while (stepsDone[i] < want) { plants[i].StepOnce(); stepsDone[i]++; }
            }
            Physics.Simulate(physicsDt);
            substepsDone = k;
            if (budget && watch.Elapsed.TotalMilliseconds > frameBudgetMs) break;
        }
    }

    void OnDisable() => EndTrial();
}
