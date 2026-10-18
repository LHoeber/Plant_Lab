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

/// <summary>
/// Marks a PlantSettings parameter that only the classic development uses (the L-system rules replace it).
/// Hidden in the Inspector and not randomized while development = LSystem. Temporary: goes away with the classic development.
/// </summary>
[AttributeUsage(AttributeTargets.Field)] public class ClassicOnlyAttribute : Attribute { }

/// <summary>Marks a PlantSettings parameter that only the L-system development uses (hidden and not randomized in Classic).</summary>
[AttributeUsage(AttributeTargets.Field)] public class LSystemOnlyAttribute : Attribute { }
