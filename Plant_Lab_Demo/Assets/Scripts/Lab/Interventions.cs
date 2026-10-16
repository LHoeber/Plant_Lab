using UnityEngine;

/// <summary>
/// The intervention channels behind the buttons, one per button. Each press cycles that channel through its levels.
/// Which button controls which channel is hidden and permuted per participant (see ControlPanel).
/// What the channels do (light, water, ...) is not defined yet: for now they only keep and report their levels,
/// and the plants don't react to them.
/// </summary>
public class Interventions : MonoBehaviour
{
    [Tooltip("Placeholder names of the channels (for logs). What each one does is not defined yet.")]
    public string[] channelNames = { "Channel 1", "Channel 2", "Channel 3", "Channel 4", "Channel 5", "Channel 6" };
    [Tooltip("Levels per channel; a press moves to the next level, after the last one back to the first.")]
    [Range(2, 6)] public int levels = 3;
    [Tooltip("Starting level of every channel.")]
    public int startLevel = 1;

    public int Count => LabActions.InterventionCount;
    /// <summary>Current level per channel.</summary>
    public int[] Level { get; private set; }

    void Awake()
    {
        Level = new int[Count];
        for (int i = 0; i < Count; i++) Level[i] = Mathf.Clamp(startLevel, 0, levels - 1);
    }

    /// <summary>Cycles a channel to its next level.</summary>
    public void Cycle(int channel)
    {
        Level[channel] = (Level[channel] + 1) % levels;
    }

    public string ChannelName(int channel) =>
        channel < channelNames.Length && !string.IsNullOrEmpty(channelNames[channel]) ? channelNames[channel] : $"Channel {channel + 1}";
}
