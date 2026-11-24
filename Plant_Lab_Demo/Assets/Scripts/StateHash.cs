using UnityEngine;

/// <summary>
/// 64-bit FNV-1a hash over exact float bits: a fingerprint of the world state for logging. Two runs with the same
/// hash at a tick were in exactly the same state; the first tick where a replay's hash differs shows where it diverged.
/// </summary>
public static class StateHash
{
    public const ulong Start = 14695981039346656037UL;
    const ulong Prime = 1099511628211UL;

    public static void Add(ref ulong h, int v)
    {
        unchecked
        {
            for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= Prime; }
        }
    }

    public static void Add(ref ulong h, float v) => Add(ref h, System.BitConverter.SingleToInt32Bits(v));

    public static void Add(ref ulong h, Vector3 v) { Add(ref h, v.x); Add(ref h, v.y); Add(ref h, v.z); }

    public static string Hex(ulong h) => h.ToString("x16");
}
