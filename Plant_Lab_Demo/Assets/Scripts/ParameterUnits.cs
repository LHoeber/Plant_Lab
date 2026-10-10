using System;

/// <summary>
/// Marks what kind of quantity a PlantSettings parameter is, so the whole plant can be rescaled consistently
/// (PlantSettings: component menu "Rescale Plant Size"):
/// a length scales with the plant, a rate per length the other way round, a random wobble per sqrt(length) with 1/sqrt.
/// Unmarked parameters (fractions, probabilities, angles, durations) stay the same.
/// </summary>
[AttributeUsage(AttributeTargets.Field)] public class LengthAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public class PerLengthAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public class PerSqrtLengthAttribute : Attribute { }
