using UnityEngine;

/// <summary>
/// What the simulation may ask about its surroundings. Everything is in plant-local space.
/// Keeps PlantSimulation independent of Unity's physics: in the scene it's backed by colliders
/// (UnityObstacleField), offline it could be e.g. an analytic terrarium box.
/// </summary>
public interface IObstacleField
{
    /// <summary>
    /// Moves a sphere of the given radius from `from` along `dir` (unit length) for up to `distance`.
    /// Returns true if it touches an obstacle on the way (or already touches one at the start), with the distance
    /// travelled until contact and the obstacle's surface normal there (pointing away from the obstacle).
    /// </summary>
    bool Cast(Vector3 from, Vector3 dir, float radius, float distance, out float hitDistance, out Vector3 normal);

    /// <summary>Cheap pre-check: is any obstacle within `radius` of `center`? (false = no need for detailed checks there)</summary>
    bool AnyWithin(Vector3 center, float radius);

    /// <summary>Does a capsule (rounded rod from a to b with the given radius) overlap any obstacle?</summary>
    bool CapsuleOverlaps(Vector3 a, Vector3 b, float radius);

    /// <summary>Does a rotated box (center, half size along its own axes, rotation) overlap any obstacle?</summary>
    bool BoxOverlaps(Vector3 center, Vector3 half, Quaternion rotation);

    /// <summary>
    /// If a rotated box overlaps obstacles: the shortest move that gets it out of all of them (direction * depth).
    /// </summary>
    bool BoxPenetration(Vector3 center, Vector3 half, Quaternion rotation, out Vector3 push);

    /// <summary>
    /// If a capsule (rounded rod from a to b) overlaps obstacles: the shortest move that gets it out of all of them.
    /// </summary>
    bool CapsulePenetration(Vector3 a, Vector3 b, float radius, out Vector3 push);
}
