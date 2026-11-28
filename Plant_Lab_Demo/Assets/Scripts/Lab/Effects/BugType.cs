using UnityEngine;

/// <summary>
/// One kind of bug for a BugSwarm (fly, beetle, butterfly, ...): its model and how it flies.
/// Create: Assets > Create > Plant Lab > Bug Type.
///
/// The prefab's head must point along +Z of the prefab root (otherwise correct it with modelRotation). The wing
/// material uses the shader "BotanicLab/BugWing": it flaps every vertex of that material around the forward axis
/// through the model's origin (left and right side mirrored), using the flap values below.
/// </summary>
[CreateAssetMenu(menuName = "Plant Lab/Bug Type", fileName = "BugType")]
public class BugType : ScriptableObject
{
    [Header("Model")]
    public GameObject prefab;
    [Tooltip("Uniform scale of the model.")]
    [Min(0.001f)] public float scale = 1f;
    [Tooltip("Extra rotation of the model (degrees) if its head doesn't point along +Z.")]
    public Vector3 modelRotation;

    [Header("Flight")]
    [Tooltip("Flight speed (m/s).")]
    [Min(0.01f)] public float speed = 0.3f;
    [Tooltip("Random speed difference between individuals (fraction, 0.2 = +-20 %).")]
    [Range(0f, 0.9f)] public float speedJitter = 0.2f;
    [Tooltip("Maximum turn rate (degrees/s). Lower = wider loops around the target point.")]
    [Min(1f)] public float turnRateDeg = 300f;
    [Tooltip("Mean time (s) until a bug picks a new target point in the area (random, exponentially distributed).")]
    [Min(0.05f)] public float retargetSeconds = 1.2f;

    [Header("Wings (BugWing shader)")]
    [Tooltip("Wing beats per second. Real flies ~200 Hz; above ~30 the beat isn't visible as such anyway.")]
    [Min(0f)] public float flapFrequency = 40f;
    [Tooltip("Flap amplitude (degrees up and down around the center angle).")]
    [Range(0f, 90f)] public float flapAmplitudeDeg = 50f;
    [Tooltip("Center angle of the flap (degrees, + = wings raised).")]
    [Range(-45f, 45f)] public float flapCenterDeg = 10f;
}
