using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class of the effects an effect button can trigger (bug swarm, ...).
///
/// On/off: during a trial the effect is on if its button (buttonIndex, set per session) was selected for that trial,
/// or if testOn is ticked. Outside trials only testOn counts: the effect then runs on frame time (for looking at it
/// in the scene; not logged, not exactly reproducible). Otherwise it is frozen between trials, like the plants.
///
/// Determinism: in a trial the effect only advances in the TickClock's substeps (Step), in lockstep with the plants,
/// and all its randomness comes from System.Random seeded with TrialSeed. So the same seed gives exactly the same
/// effect at every tick, at any frame rate.
///
/// Logging: SessionLogger writes LogInfo into session.json, LogState into every tick of ticks.jsonl, and
/// (on, seed) per trial into trials.jsonl.
/// </summary>
[DefaultExecutionOrder(150)]//Start after TrialController, before SessionLogger: reset before the logger's start image
public abstract class LabEffect : MonoBehaviour
{
    [Header("Effect")]
    [Tooltip("Effect button that switches this effect on during a trial (index as in the logs, -1 = none). " +
             "Can be set per session from a script.")]
    public int buttonIndex = -1;
    [Tooltip("Testing: on regardless of the button, also outside trials (then running on frame time, not logged).")]
    public bool testOn;
    [Tooltip("Seed of the effect's randomness.")]
    public int seed = 1;
    [Tooltip("On: every trial gets its own seed, derived from seed and the trial index (logged). " +
             "Off: every trial repeats exactly the same randomness.")]
    public bool newSeedEachTrial = true;

    [Header("References (found automatically if empty)")]
    public TrialController trials;
    public TickClock clock;

    /// <summary>All enabled effects (the logger sorts them by hierarchy path).</summary>
    public static readonly List<LabEffect> All = new List<LabEffect>();

    /// <summary>Name in the logs.</summary>
    public virtual string EffectName => GetType().Name;
    /// <summary>Is a trial running (the effect advances with the TickClock)?</summary>
    public bool InTrial { get; private set; }
    /// <summary>Is the effect on right now?</summary>
    public bool On => testOn || (InTrial && selectedInTrial);
    /// <summary>Seed of the current trial's randomness.</summary>
    public int TrialSeed { get; private set; }
    /// <summary>Time since the trial start in s (substep-exact), or since free running started outside trials.</summary>
    public double EffectTime { get; private set; }
    /// <summary>Current tick (in a trial: the clock's tick; free running: counted from the frame time).</summary>
    public int EffectTick { get; private set; }

    bool selectedInTrial;
    bool freeRunning;

    protected virtual void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    protected virtual void Start()
    {
        if (trials == null) trials = FindFirstObjectByType<TrialController>();
        if (clock == null) clock = trials != null && trials.clock != null ? trials.clock : FindFirstObjectByType<TickClock>();
        if (trials != null) { trials.TrialStarted += OnTrialStarted; trials.TrialEnded += OnTrialEnded; }
        if (clock != null) clock.Substepped += OnSubstep;
    }

    protected virtual void OnDestroy()
    {
        if (trials != null) { trials.TrialStarted -= OnTrialStarted; trials.TrialEnded -= OnTrialEnded; }
        if (clock != null) clock.Substepped -= OnSubstep;
    }

    /// <summary>Seed for trial `trial` (newSeedEachTrial) or the plain seed.</summary>
    public int SeedForTrial(int trial) => newSeedEachTrial ? Mix(seed, trial) : seed;

    /// <summary>Deterministic integer mix (for deriving independent seeds).</summary>
    public static int Mix(int a, int b) { unchecked { return (a * 486187739) ^ (b * 16777619 + 7919); } }

    void OnTrialStarted(int trial, bool[] buttonsOn)
    {
        InTrial = true;
        freeRunning = false;
        selectedInTrial = buttonIndex >= 0 && buttonsOn != null && buttonIndex < buttonsOn.Length && buttonsOn[buttonIndex];
        TrialSeed = SeedForTrial(trial);
        EffectTime = 0;
        EffectTick = 0;
        ResetEffect(TrialSeed);
    }

    void OnTrialEnded(int trial) => InTrial = false;//frozen in the final state until the next trial starts

    void OnSubstep(float dt)
    {
        if (!InTrial) return;
        EffectTick = clock.Tick;
        EffectTime += dt;
        Step(dt, EffectTick);
    }

    double freeAccum;

    protected virtual void Update()
    {
        //testing outside trials: run on frame time in the same fixed substeps
        //(after switching testOn off it keeps running until the effect has settled, e.g. all bugs are back in the vent)
        if (InTrial || !(testOn || (freeRunning && Busy))) { freeRunning = false; return; }
        float tickLength = clock != null ? clock.tickLength : 0.5f;
        float dt = clock != null ? tickLength / clock.physicsStepsPerTick : 0.02f;
        if (!freeRunning)
        {
            freeRunning = true;
            freeAccum = 0;
            EffectTime = 0;
            TrialSeed = seed;
            ResetEffect(TrialSeed);
        }
        freeAccum += Mathf.Min(Time.deltaTime, 0.1f);
        while (freeAccum >= dt)
        {
            freeAccum -= dt;
            EffectTime += dt;
            EffectTick = (int)(EffectTime / tickLength);
            Step(dt, EffectTick);
        }
    }

    /// <summary>Still something going on (free running continues after testOn is switched off while true)?</summary>
    protected virtual bool Busy => false;
    /// <summary>Back to the start state for a trial (or for free running), with this seed.</summary>
    protected abstract void ResetEffect(int seed);
    /// <summary>Advance by dt seconds; `tick` = the current tick.</summary>
    protected abstract void Step(float dt, int tick);
    /// <summary>Per-tick state for ticks.jsonl.</summary>
    public abstract void LogState(Json j);
    /// <summary>Settings for session.json.</summary>
    public abstract void LogInfo(Json j);
}
