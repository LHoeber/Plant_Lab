using UnityEngine;

/// <summary>
/// When a stopped organ or stem piece may grow again. Without care, "stop when touching, grow again when free"
/// oscillates: it grows back into the same obstacle, stops, shrinks free, grows back ... (a visible pulsating).
/// So it waits a while after the last contact before growing again, and every time it runs into something again
/// right after growing, the wait doubles: it soon stays put where it fits.
/// </summary>
public class GrowthStop
{
    const float FirstWait = 1f;  //sim seconds free before growing again, at first
    const float LongestWait = 30f;

    float lastStuck = float.NegativeInfinity;
    float wait = FirstWait;
    bool growingAgain;

    /// <summary>It touches something (now).</summary>
    public void Stuck(float time)
    {
        if (growingAgain) { wait = Mathf.Min(2f * wait, LongestWait); growingAgain = false; }//grew back into it: wait longer next time
        lastStuck = time;
    }

    /// <summary>Has it been free long enough to grow again?</summary>
    public bool MayGrow(float time)
    {
        if (time - lastStuck < wait) return false;
        growingAgain = true;
        return true;
    }
}
