using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 3D console (on the root of the imported console model). Everything it needs is set up here at start,
/// from the parts' names, so nothing is stored on the model itself and a re-exported model only has to keep
/// the names: Casing, Screen, CamLeft, CamRight, CamUp, CamDown, ButtonA, ButtonB, ...
/// - buttons get a collider (sized to their current mesh) and a PanelButton with their action
/// - the screen gets the observation (unlit) with the static overlay
/// - the console camera is placed in front of the screen and framed so the whole casing is visible
/// - all parts are put on the console's rendering layer, so only the console light lights them
///   (the terrarium's lights, which interventions may change, never change how the console looks)
/// </summary>
public class Console3D : MonoBehaviour
{
    public ControlPanel panel;
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

    [Header("Buttons")]
    [Tooltip("How far a button moves into the casing while pressed (m).")]
    [Range(0f, 0.02f)] public float pressDepth = 0.003f;

    [Header("Lighting")]
    [Tooltip("Rendering layer (bit index) of the console. The console light lights only this layer, the scene's lights don't.")]
    [Range(1, 31)] public int renderingLayer = 1;

    [Header("Screen glass (static)")]
    [Range(0f, 1f)] public float vignette = 0.45f;
    [Range(0f, 1f)] public float glare = 0.12f;
    [Range(0f, 1f)] public float scanlines = 0.06f;
    [Range(2, 8)] public int scanlinePeriod = 3;

    static readonly string[] CameraButtonNames = { "CamLeft", "CamRight", "CamUp", "CamDown" };//index = action

    Renderer casing, screen;
    Material screenInstance;
    Texture2D overlay;

    void Awake()
    {
        var parts = new Dictionary<string, Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) parts.TryAdd(t.name, t);
        Transform Part(string name)
        {
            if (parts.TryGetValue(name, out Transform t)) return t;
            Debug.LogWarning($"Console3D: the console model has no part named '{name}'.", this);
            return null;
        }

        casing = Part("Casing")?.GetComponent<Renderer>();
        screen = Part("Screen")?.GetComponent<Renderer>();

        //all parts: on this object's layer (seen by the console camera only) and the console's rendering layer
        uint mask = 1u << renderingLayer;
        foreach (Transform t in parts.Values) t.gameObject.layer = gameObject.layer;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = mask;

        Vector3 push = -Front() * pressDepth;
        var known = new HashSet<string>();
        for (int a = 0; a < CameraButtonNames.Length; a++)
        {
            known.Add(CameraButtonNames[a]);
            SetUpButton(Part(CameraButtonNames[a]), a, push);
        }
        for (int i = 0; i < LabActions.InterventionCount; i++)
        {
            string name = "Button" + LabActions.ButtonLetter(i);
            known.Add(name);
            SetUpButton(Part(name), LabActions.ButtonA + i, push);
        }
        foreach (string name in parts.Keys)
            if ((name.StartsWith("Button") || name.StartsWith("Cam")) && !known.Contains(name))
                Debug.LogWarning($"Console3D: '{name}' looks like a button but has no action " +
                                 $"(there are {LabActions.InterventionCount} intervention buttons). It does nothing.", this);
    }

    void SetUpButton(Transform t, int action, Vector3 push)
    {
        if (t == null) return;
        if (t.GetComponent<Collider>() == null)
        {
            var box = t.gameObject.AddComponent<BoxCollider>();
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size; }
        }
        var button = t.GetComponent<PanelButton>() ?? t.gameObject.AddComponent<PanelButton>();
        button.action = action;
        button.panel = panel;
        button.pressOffset = push;
    }

    void Start()
    {
        //in Start: the observation texture is created in ObservationCamera.Awake
        if (screen == null) return;
        if (screenMaterial == null) { Debug.LogWarning("Console3D: no screen material assigned.", this); return; }
        screenInstance = new Material(screenMaterial) { name = "ConsoleScreen (runtime)" };
        if (observationCamera != null && observationCamera.Observation != null)
        {
            screenInstance.SetTexture("_MainTex", observationCamera.Observation);
            overlay = StaticScreenOverlay.Generate(observationCamera.width, observationCamera.height, vignette, glare, scanlines, scanlinePeriod);
            screenInstance.SetTexture("_OverlayTex", overlay);
        }
        var mats = new Material[screen.sharedMaterials.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = screenInstance;
        screen.sharedMaterials = mats;
    }

    /// <summary>The direction the console's front faces (out of the screen), in world space.</summary>
    public Vector3 Front()
    {
        Vector3 n = -transform.forward;
        var mf = screen != null ? screen.GetComponent<MeshFilter>() : null;
        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.normals.Length > 0)
            n = screen.transform.TransformDirection(mf.sharedMesh.normals[0]).normalized;
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
        if (screenInstance != null) Destroy(screenInstance);
        if (overlay != null) Destroy(overlay);
    }
}
