using System;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Marks a PlantSettings field that the randomizer must leave alone (e.g. technical values).</summary>
[AttributeUsage(AttributeTargets.Field)]
public class NoRandomizeAttribute : Attribute { }

/// <summary>
/// Creates a randomly varied copy of a PlantSettings asset. The original is never changed.
/// Every float/int field with a [Range] is varied within that range, every enum field may switch value,
/// every L-system constant within its own min and max.
/// Same source values + same strength + same seed => same result.
/// </summary>
public static class PlantSettingsRandomizer
{
    /// <param name="strength">0 = exact copy, 1 = std of a quarter of each slider's range; enums switch with probability strength/2</param>
    /// <param name="changes">human-readable list of what changed (for the console)</param>
    public static PlantSettings Randomize(PlantSettings source, float strength, int seed, out string changes)
    {
        PlantSettings copy = UnityEngine.Object.Instantiate(source);//full copy, lives only in memory
        copy.name = source.name + " (randomized " + seed + ")";
        var rng = new System.Random(seed);
        var log = new StringBuilder();

        //reflection: go through all public fields of PlantSettings instead of listing them by hand,
        //so new parameters are included automatically
        //parameters the plant's development doesn't use are left alone (they'd change nothing)
        Type unused = source.development == DevelopmentMode.LSystem ? typeof(ClassicOnlyAttribute) : typeof(LSystemOnlyAttribute);
        foreach (FieldInfo field in typeof(PlantSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.GetCustomAttribute<NoRandomizeAttribute>() != null || Attribute.IsDefined(field, unused)) continue;
            var range = field.GetCustomAttribute<RangeAttribute>();

            if (field.FieldType == typeof(float) && range != null)
            {
                float before = (float)field.GetValue(copy);
                float after = Perturb(before, range.min, range.max, strength, rng);
                field.SetValue(copy, after);
                if (!Mathf.Approximately(before, after)) log.AppendLine($"  {field.Name}: {before:0.###} -> {after:0.###}");
            }
            else if (field.FieldType == typeof(int) && range != null)
            {
                int before = (int)field.GetValue(copy);
                int after = Mathf.RoundToInt(Perturb(before, range.min, range.max, strength, rng));
                field.SetValue(copy, after);
                if (before != after) log.AppendLine($"  {field.Name}: {before} -> {after}");
            }
            else if (field.FieldType.IsEnum)
            {
                //draw both numbers always, so the random sequence doesn't depend on the outcome
                double uSwitch = rng.NextDouble(), uPick = rng.NextDouble();
                Array values = Enum.GetValues(field.FieldType);
                object before = field.GetValue(copy);
                if (uSwitch < strength * 0.5 && values.Length > 1)
                {
                    object after = values.GetValue(Mathf.Min((int)(uPick * values.Length), values.Length - 1));
                    field.SetValue(copy, after);
                    if (!after.Equals(before)) log.AppendLine($"  {field.Name}: {before} -> {after}");
                }
            }
        }
        //L-system constants: each within its own min..max
        if (copy.lsConstants != null && source.development == DevelopmentMode.LSystem)
            foreach (LConstant c in copy.lsConstants)
            {
                float before = c.value;
                c.value = Perturb(before, Mathf.Min(c.min, c.max), Mathf.Max(c.min, c.max), strength, rng);
                if (!Mathf.Approximately(before, c.value)) log.AppendLine($"  L-system {c.name}: {before:0.###} -> {c.value:0.###}");
            }
        changes = log.Length > 0 ? log.ToString() : "  (no changes)";
        return copy;
    }

    /// <summary>
    /// Normal perturbation around the current value with std = strength * range / 4,
    /// redrawn if it falls outside the range (so values don't pile up at the slider ends).
    /// </summary>
    static float Perturb(float value, float min, float max, float strength, System.Random rng)
    {
        float std = strength * (max - min) * 0.25f;
        //always draw the same number of values, so one field's outcome can't shift the numbers of the next fields
        const int tries = 8;
        float result = float.NaN;
        for (int i = 0; i < tries; i++)
        {
            float candidate = value + PlantSimulation.Gaussian(rng) * std;
            if (float.IsNaN(result) && candidate >= min && candidate <= max) result = candidate;
        }
        return float.IsNaN(result) ? Mathf.Clamp(value, min, max) : result;//all tries outside: keep the value
    }
}
