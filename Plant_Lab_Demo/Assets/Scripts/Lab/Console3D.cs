using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 3D console: puts the camera image on the screen, keeps the console lit only by its own light, and frames the
/// console camera so the whole casing is visible. The parts are found by name ("Casing", "Screen"): from the
/// PanelBuilder on this object if there is one (rebuilt panels included), otherwise from the children (an imported model).
/// - all parts are put on the console's rendering layer, so only the console light lights them
///   (the terrarium's lights, which interventions may change, never change how the console looks)
/// </summary>
public class Console3D : MonoBehaviour
{
    public ObservationCamera observationCamera;
    [Tooltip("The camera showing the console to the participant.")]
    public Camera consoleCamera;
    [Tooltip("Material with the BotanicLab/ConsoleScreen shader (a copy is used at runtime).")]
    public Material screenMaterial;

    [Header("Framing")]
    [Tooltip("Vertical field of view of the console camera. Small = flat look, large = more perspective.")]
    [Range(5f, 60f)] public float fieldOfView = 20f;
    [Tooltip("Free space around the casing (1 = touching the window edges).")]
    [Range(1f, 1.3f)] public float margin = 1.03f;
    [Tooltip("Turn on if the camera ends up behind the console (the Screen mesh's normal points backward).")]
    public bool flipFront = false;

    [Header("Lighting")]
    [Tooltip("Rendering layer (bit index) of the console. The console light lights only this layer, the scene's lights don't.")]
    [Range(1, 31)] public int renderingLayer = 1;

    [Header("Screen glass (static)")]
    [Range(0f, 1f)] public float vignette = 0.45f;
    [Range(0f, 1f)] public float glare = 0.12f;
    [Range(0f, 1f)] public float scanlines = 0.06f;
    [Range(2, 8)] public int scanlinePeriod = 3;

    /// <summary>The screen's material (runtime copy): TrialController switches between camera image and screen texts.</summary>
    public Material ScreenMaterial => screenInstance;

    Renderer casing, screen;
    Material screenInstance;
    Texture2D overlay;
    bool warnedUnreadable;
    PanelBuilder builder;

    void Awake()
    {
        builder = GetComponent<PanelBuilder>();
        if (builder != null) builder.Rebuilt += OnRebuilt;
        FindParts();
    }

    void OnRebuilt()
    {
        FindParts();
        AssignScreenMaterial();
    }

    void FindParts()
    {
        Transform Part(string name)
        {
            Transform t = builder != null ? builder.Part(name) : FindChild(transform, name);
            if (t == null) Debug.LogWarning($"Console3D: the console has no part named '{name}'.", this);
            return t;
        }
        casing = Part("Casing")?.GetComponent<Renderer>();
        screen = Part("Screen")?.GetComponent<Renderer>();

        //all parts: on this object's layer (seen by the console camera only) and the console's rendering layer
        uint mask = 1u << renderingLayer;
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = mask;
    }

    static Transform FindChild(Transform t, string name)
    {
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
        return null;
    }

    void Start()
    {
        //in Start: the observation texture is created in ObservationCamera.Awake
        if (screenMaterial == null) { Debug.LogWarning("Console3D: no screen material assigned.", this); return; }
        screenInstance = new Material(screenMaterial) { name = "ConsoleScreen (runtime)" };
        if (observationCamera != null && observationCamera.Observation != null)
        {
            screenInstance.SetTexture("_MainTex", observationCamera.Observation);
            overlay = StaticScreenOverlay.Generate(observationCamera.width, observationCamera.height, vignette, glare, scanlines, scanlinePeriod);
            screenInstance.SetTexture("_OverlayTex", overlay);
        }
        AssignScreenMaterial();
    }

    void AssignScreenMaterial()
    {
        if (screen == null || screenInstance == null) return;
        var mats = new Material[Mathf.Max(1, screen.sharedMaterials.Length)];
        for (int i = 0; i < mats.Length; i++) mats[i] = screenInstance;
        screen.sharedMaterials = mats;
    }

    /// <summary>The direction the console's front faces (out of the screen), in world space.</summary>
    public Vector3 Front()
    {
        Vector3 n = -transform.forward;
        var mf = screen != null ? screen.GetComponent<MeshFilter>() : null;
        if (mf != null && mf.sharedMesh != null)
        {
            //in Play mode a mesh can only be read if its model has Read/Write enabled (Use 3D Console sets that)
            if (mf.sharedMesh.isReadable && mf.sharedMesh.normals.Length > 0)
                n = screen.transform.TransformDirection(mf.sharedMesh.normals[0]).normalized;
            else if (!warnedUnreadable)
            {
                warnedUnreadable = true;
                Debug.LogWarning("Console3D: can't read the Screen mesh's normal. Enable Read/Write in the console model's " +
                                 "import settings (Model tab).", this);
            }
        }
        return flipFront ? -n : n;
    }

    void LateUpdate()
    {
        if (consoleCamera == null || casing == null) return;
        Vector3 front = Front();
        Vector3 up = Vector3.ProjectOnPlane(transform.up, front);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(transform.forward, front);
        up.Normalize();
        Vector3 right = Vector3.Cross(up, front);

        //casing extents in the console's own frame (from its mesh, so it follows re-exports and rotations)
        Bounds local = casing.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null
            ? mf.sharedMesh.bounds : new Bounds(casing.transform.InverseTransformPoint(casing.bounds.center), Vector3.zero);
        Vector3 center = casing.transform.TransformPoint(local.center);
        float halfW = 0f, halfH = 0f, frontMost = 0f;
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents,
                new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
            Vector3 d = casing.transform.TransformPoint(corner) - center;
            halfW = Mathf.Max(halfW, Mathf.Abs(Vector3.Dot(d, right)));
            halfH = Mathf.Max(halfH, Mathf.Abs(Vector3.Dot(d, up)));
            frontMost = Mathf.Max(frontMost, Vector3.Dot(d, front));
        }

        //distance at which the casing fits both vertically and horizontally (the other direction gets black bars)
        float tanHalf = Mathf.Tan(0.5f * fieldOfView * Mathf.Deg2Rad);
        float aspect = Mathf.Max(1e-3f, consoleCamera.aspect);
        float dist = margin * Mathf.Max(halfH / tanHalf, halfW / (tanHalf * aspect)) + frontMost;

        consoleCamera.orthographic = false;
        consoleCamera.fieldOfView = fieldOfView;
        consoleCamera.nearClipPlane = Mathf.Max(0.01f, 0.5f * (dist - frontMost));
        consoleCamera.farClipPlane = dist + 2f;
        consoleCamera.transform.SetPositionAndRotation(center + front * dist, Quaternion.LookRotation(-front, up));
    }

    void OnDestroy()
    {
        if (builder != null) builder.Rebuilt -= OnRebuilt;
        if (screenInstance != null) Destroy(screenInstance);
        if (overlay != null) Destroy(overlay);
    }
}
