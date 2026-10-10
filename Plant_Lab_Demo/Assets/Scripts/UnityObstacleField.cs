using UnityEngine;

/// <summary>
/// IObstacleField backed by Unity colliders on the given layers (e.g. the terrarium glass).
/// Converts between the plant's local space (simulation) and world space (physics).
/// Queries against static colliders are deterministic, so the plant stays reproducible.
/// </summary>
public class UnityObstacleField : IObstacleField
{
    readonly Transform plant;
    readonly LayerMask layers;
    readonly Collider[] overlaps = new Collider[8];//reused buffer for overlap queries

    readonly float stepOut;//how far to push per pass when the shortest way out would lead through the obstacle

    public UnityObstacleField(Transform plant, LayerMask layers, float stepOut)
    {
        this.plant = plant;
        this.layers = layers;
        this.stepOut = stepOut;
    }

    public bool AnyWithin(Vector3 center, float radius)
    {
        float scale = plant.lossyScale.x;
        return Physics.CheckSphere(plant.TransformPoint(center), radius * scale, layers, QueryTriggerInteraction.Ignore);
    }

    public bool CapsuleOverlaps(Vector3 a, Vector3 b, float radius)
    {
        float scale = plant.lossyScale.x;
        return Physics.CheckCapsule(plant.TransformPoint(a), plant.TransformPoint(b), radius * scale, layers, QueryTriggerInteraction.Ignore);
    }

    public bool BoxOverlaps(Vector3 center, Vector3 half, Quaternion rotation)
    {
        float scale = plant.lossyScale.x;
        return Physics.CheckBox(plant.TransformPoint(center), half * scale, plant.rotation * rotation, layers, QueryTriggerInteraction.Ignore);
    }

    //invisible probe shapes, only used to ask Unity how deep a box/capsule reaches into an obstacle
    static BoxCollider probeBox;
    static CapsuleCollider probeCapsule;

    static void EnsureProbes()
    {
        if (probeBox != null && probeCapsule != null) return;
        var go = new GameObject("PlantContactProbe") { hideFlags = HideFlags.HideAndDontSave };
        go.layer = 2;//Ignore Raycast
        go.transform.position = new Vector3(0f, -20000f, 0f);
        probeBox = go.AddComponent<BoxCollider>();
        probeBox.isTrigger = true;//never pushes anything itself
        probeCapsule = go.AddComponent<CapsuleCollider>();
        probeCapsule.isTrigger = true;
        probeCapsule.direction = 1;//along Y
    }

    public bool BoxPenetration(Vector3 center, Vector3 half, Quaternion rotation, out Vector3 push)
    {
        push = Vector3.zero;
        float scale = plant.lossyScale.x;
        Vector3 wCenter = plant.TransformPoint(center);
        Quaternion wRot = plant.rotation * rotation;
        Vector3 wHalf = half * scale;
        int n = Physics.OverlapBoxNonAlloc(wCenter, wHalf, overlaps, wRot, layers, QueryTriggerInteraction.Ignore);
        if (n == 0) return false;
        EnsureProbes();
        probeBox.size = wHalf * 2f;
        probeBox.center = Vector3.zero;
        return Resolve(probeBox, wCenter, wRot, n, out push);
    }

    public bool CapsulePenetration(Vector3 a, Vector3 b, float radius, out Vector3 push)
    {
        push = Vector3.zero;
        float scale = plant.lossyScale.x;
        Vector3 wa = plant.TransformPoint(a), wb = plant.TransformPoint(b);
        float wr = radius * scale;
        int n = Physics.OverlapCapsuleNonAlloc(wa, wb, wr, overlaps, layers, QueryTriggerInteraction.Ignore);
        if (n == 0) return false;
        EnsureProbes();
        Vector3 axis = wb - wa;
        float len = axis.magnitude;
        probeCapsule.radius = wr;
        probeCapsule.height = len + 2f * wr;
        probeCapsule.center = Vector3.zero;
        Quaternion wRot = len > 1e-7f ? Quaternion.FromToRotation(Vector3.up, axis / len) : Quaternion.identity;
        return Resolve(probeCapsule, (wa + wb) * 0.5f, wRot, n, out push);
    }

    /// <summary>
    /// Combines the separation from each overlapping obstacle (Unity's ComputePenetration) into one push,
    /// without counting the same direction twice. Returned in plant-local space.
    /// </summary>
    bool Resolve(Collider probe, Vector3 wPos, Quaternion wRot, int n, out Vector3 push)
    {
        Vector3 total = Vector3.zero;
        bool any = false;
        Vector3 plantBase = plant.position;
        float scale0 = plant.lossyScale.x;
        for (int i = 0; i < n; i++)
        {
            Collider other = overlaps[i];
            if (!Physics.ComputePenetration(probe, wPos, wRot, other, other.transform.position, other.transform.rotation,
                                            out Vector3 dir, out float dist) || dist <= 0f) continue;
            //thin obstacles (e.g. 3 cm glass): if a part is more than halfway in, the shortest way out is the far side.
            //never push through: only toward the side the plant grows on (where its base is), a little per pass
            Vector3 plantSide = plantBase - other.ClosestPoint(plantBase);
            if (plantSide.sqrMagnitude > 1e-10f && Vector3.Dot(dir, plantSide) < 0f)
            {
                dir = plantSide.normalized;
                dist = Mathf.Max(stepOut * scale0, 1e-4f);
            }
            float already = Vector3.Dot(total, dir);
            if (already < dist) total += dir * (dist - already);
            any = true;
        }
        float scale = plant.lossyScale.x;
        push = any ? plant.InverseTransformDirection(total) / scale : Vector3.zero;
        return any;
    }

    public bool Cast(Vector3 from, Vector3 dir, float radius, float distance, out float hitDistance, out Vector3 normal)
    {
        float scale = plant.lossyScale.x;//assumes a uniformly scaled plant
        Vector3 wFrom = plant.TransformPoint(from);
        Vector3 wDir = plant.TransformDirection(dir).normalized;
        float wRadius = radius * scale;

        //already touching at the start (e.g. pushed toward the wall by bending)?
        //SphereCast ignores colliders it starts inside, so this case is checked separately
        int n = Physics.OverlapSphereNonAlloc(wFrom, wRadius, overlaps, layers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Vector3 closest = overlaps[i].ClosestPoint(wFrom);
            Vector3 away = wFrom - closest;
            //only counts if we're moving toward it (moving away/along is free)
            Vector3 wNormal = away.sqrMagnitude > 1e-12f ? away.normalized : -wDir;//center inside the collider: treat as head-on
            if (Vector3.Dot(wDir, wNormal) < 0f)
            {
                hitDistance = 0f;
                normal = plant.InverseTransformDirection(wNormal).normalized;
                return true;
            }
        }

        if (Physics.SphereCast(wFrom, wRadius, wDir, out RaycastHit hit, distance * scale, layers, QueryTriggerInteraction.Ignore))
        {
            hitDistance = hit.distance / scale;
            normal = plant.InverseTransformDirection(hit.normal).normalized;
            return true;
        }
        hitDistance = distance;
        normal = Vector3.zero;
        return false;
    }
}
