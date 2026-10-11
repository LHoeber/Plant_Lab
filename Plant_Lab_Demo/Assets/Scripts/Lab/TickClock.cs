using System;
using UnityEngine;

/// <summary>
/// The decision clock (default 0.5 s). The world never waits for input: every tick happens on time,
/// with or without an action. Only the first action in each tick counts; later ones in the same tick are refused
/// (the caller shows press feedback only for accepted ones). A tick without input is a no-op, and no-ops are
/// reported like any other action (dwell time is a dependent variable, not waste).
/// </summary>
public class TickClock : MonoBehaviour
{
    [Tooltip("Seconds per tick (decision and observation clock). 0.5 s default, 1 s is the alternative.")]
    [Range(0.1f, 2f)] public float tickLength = 0.5f;
    [Tooltip("Print every tick to the Console (for testing).")]
    public bool logTicks = false;

    /// <summary>Index of the tick currently collecting input.</summary>
    public int Tick { get; private set; }
    /// <summary>Action accepted in the current tick (NoOp if none yet).</summary>
    public int CurrentAction { get; private set; } = LabActions.NoOp;

    /// <summary>Called at the end of every tick with (tick index, action taken in it). Logging hooks in here.</summary>
    public event Action<int, int> TickEnded;

    float timer;

    /// <summary>Offers an action for the current tick. True if it's the first one (accepted), false if refused.</summary>
    public bool Submit(int action)
    {
        if (CurrentAction != LabActions.NoOp) return false;//first action in this tick wins
        CurrentAction = action;
        return true;
    }

    void Update()
    {
        timer += Time.deltaTime;
        while (timer >= tickLength)
        {
            timer -= tickLength;
            int action = CurrentAction;
            CurrentAction = LabActions.NoOp;
            if (logTicks) Debug.Log($"tick {Tick}: {LabActions.Name(action)}");
            TickEnded?.Invoke(Tick, action);
            Tick++;
        }
    }
}
