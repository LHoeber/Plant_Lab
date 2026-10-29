using UnityEngine;

/// <summary>
/// The terrarium camera ("camera A"): orbits the terrarium in fixed steps and renders into a RenderTexture.
/// That texture is the observation: the agent gets exactly this image, participants see it on the console screen.
/// Its culling mask must exclude the console (UI layer), so the console can never appear in the observation.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ObservationCamera : MonoBehaviour
{
    [Tooltip("What the camera orbits around (e.g. the plant); the orbit center is this point plus centerHeight.")]
    public Transform target;
    public float centerHeight = 1.1f;
    public float distance = 3f;

    [Header("Steps")]
    [Tooltip("Degrees per arrow press (azimuth and elevation).")]
    [Range(5f, 45f)] public float stepDeg = 20f;
    [Tooltip("Elevation limits: up/down presses beyond them are refused (action masking).")]
    public float minElevation = -20f, maxElevation = 60f;
    [Tooltip("Starting view.")]
    public float azimuth = 0f, elevation = 20f;
    [Tooltip("Seconds to glide to a new step (keep shorter than a tick).")]
    [Range(0f, 1f)] public float moveTime = 0.3f;

    [Header("Observation")]
    [Tooltip("Resolution of the observation texture. The console screen must have the same aspect ratio (3:2 by default). " +
             "The camera's field of view is vertical, so a wider image shows more to the sides, not less above and below.")]
    public int width = 768, height = 512;

    /// <summary>The observation: what the agent receives, and what the console screen shows.</summary>
    public RenderTexture Observation { get; private set; }

    float shownAzimuth, shownElevation, azVel, elVel;
    Camera cam;

    float startAzimuth, startElevation;

    /// <summary>Back to the starting view, immediately (no glide): every trial starts from the same view.</summary>
    public void ResetView()
    {
        azimuth = shownAzimuth = startAzimuth;
        elevation = shownElevation = startElevation;
        azVel = elVel = 0f;
        Place();
    }

    void Awake()
    {
        startAzimuth = azimuth;
        startElevation = elevation;
        cam = GetComponent<Camera>();
        Observation = new RenderTexture(width, height, 24) { name = "Observation", antiAliasing = 2 };
        cam.targetTexture = Observation;
        shownAzimuth = azimuth;
        shownElevation = elevation;
        Place();
    }

    /// <summary>Would this camera action do anything? (Up/down are refused at the elevation limits.)</summary>
    public bool CanStep(int action) => action switch
    {
        LabActions.CameraUp => elevation + stepDeg <= maxElevation + 1e-3f,
        LabActions.CameraDown => elevation - stepDeg >= minElevation - 1e-3f,
        LabActions.CameraLeft or LabActions.CameraRight => true,
        _ => false,
    };

    public void Step(int action)
    {
        if (!CanStep(action)) return;
        switch (action)
        {
            //a larger azimuth moves the camera clockwise seen from above, i.e. to its own left
            case LabActions.CameraLeft: azimuth += stepDeg; break;
            case LabActions.CameraRight: azimuth -= stepDeg; break;
            case LabActions.CameraUp: elevation += stepDeg; break;
            case LabActions.CameraDown: elevation -= stepDeg; break;
        }
    }

    void LateUpdate()
    {
        //glide to the stepped view (continuous to look at, but the logged view is always the stepped one)
        shownAzimuth = Mathf.SmoothDampAngle(shownAzimuth, azimuth, ref azVel, Mathf.Max(1e-3f, moveTime * 0.3f));
        shownElevation = Mathf.SmoothDamp(shownElevation, elevation, ref elVel, Mathf.Max(1e-3f, moveTime * 0.3f));
        Place();
    }

    void Place()
    {
        Vector3 center = (target != null ? target.position : Vector3.zero) + Vector3.up * centerHeight;
        Quaternion q = Quaternion.Euler(shownElevation, shownAzimuth, 0f);
        transform.position = center + q * (Vector3.back * distance);
        transform.rotation = Quaternion.LookRotation(center - transform.position, Vector3.up);
    }

    void OnDestroy()
    {
        if (Observation != null) { if (cam != null) cam.targetTexture = null; Observation.Release(); Destroy(Observation); }
    }
}
