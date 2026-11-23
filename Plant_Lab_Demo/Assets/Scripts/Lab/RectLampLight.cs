using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A rectangular lamp: turns the Spot Light on this object into a rectangular beam that widens like a pyramid,
/// and draws a matching visible beam in the air.
/// Put a Spot Light at the center of the lamp's opening, its blue arrow (z) pointing out of the lamp, and add this.
///   - Light: a cookie (a soft-edged rectangle, generated) cuts the round spot cone to a rectangle. Spot angle and
///     cookie are chosen so the lit rectangle has the beam's size at landingDistance (default: the beam's end). The spot's
///     inner angle is set to its outer angle, so only the cookie shapes the edges. Color and intensity: on the Light.
///   - Beam: a mesh from the full opening (openingSize) to beamLength, widening by spreadDeg on each side, drawn with
///     an additive material (BotanicLab/LightBeam). Light in the air is otherwise invisible in URP.
/// The light itself starts from one point (URP has no real-time rectangular lights), so very close to the lamp the
/// lit area is smaller than the opening; from about the beam's end on, light and beam agree.
/// Sizes are in meters (the lamp's scale doesn't matter). Updates live in the editor.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class RectLampLight : MonoBehaviour
{
    [Tooltip("Size of the lamp's opening (width along the light's x = red arrow, height along y = green arrow), m.")]
    public Vector2 openingSize = new Vector2(0.91f, 0.17f);
    [Tooltip("How much the beam widens: each side leans outward by this angle (x: left/right, y: top/bottom). 0 = parallel.")]
    public Vector2 spreadDeg = new Vector2(8f, 8f);
    [Tooltip("Length of the visible beam (m).")]
    [Min(0.1f)] public float beamLength = 3f;
    [Tooltip("Distance (m) at which the lit rectangle has exactly the beam's size (the beam widened to that distance): " +
             "where the light should land, e.g. the wall or the plant. Nearer, the lit area is smaller than the beam, " +
             "farther it grows faster (the light starts from one point). 0 = at the beam's end.")]
    [Min(0f)] public float landingDistance = 0f;
    [Tooltip("How far the light reaches (m); it fades out toward there. 0 = twice the landing distance.")]
    [Min(0f)] public float range = 0f;
    [Tooltip("Softness of the lit rectangle's edges (fraction of its size).")]
    [Range(0.01f, 0.5f)] public float edgeSoftness = 0.08f;

    [Header("Visible beam")]
    public bool showBeam = true;
    [Tooltip("Material for the beam (shader BotanicLab/LightBeam: color, intensity, edge softness, length fade). " +
             "Created in Assets/Materials if empty (editor).")]
    public Material beamMaterial;

    Light spot;
    Texture2D cookie;
    Mesh beam;
    int builtHash;
    float builtAngle, builtRange;//what was set on the Light (rebuilt if the Light differs, e.g. stale saved values)

    void OnEnable()
    {
        spot = GetComponent<Light>();
        RenderPipelineManager.beginCameraRendering += DrawBeam;
        //right away, not only in Update: in edit mode Update only runs when this scene changes, so after loading the
        //scene or editing the prefab the light would keep old values and have no cookie (it isn't saved) until then
        Rebuild();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= DrawBeam;
        if (spot != null && spot.cookie == cookie) spot.cookie = null;
        DestroySafe(cookie); cookie = null;
        DestroySafe(beam); beam = null;
    }

    void OnValidate()
    {
        builtHash = 0;
#if UNITY_EDITOR
        //(changing other components inside OnValidate isn't allowed: right after it instead)
        UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Rebuild(); };
#endif
    }

    void Update()
    {
#if UNITY_EDITOR
        if (beamMaterial == null) beamMaterial = CreateBeamMaterial();
#endif
        if (Hash() == builtHash && cookie != null && beam != null && spot.cookie == cookie
            && spot.spotAngle == builtAngle && spot.range == builtRange) return;
        Rebuild();
    }

    int Hash() => (openingSize, spreadDeg, beamLength, landingDistance, range, edgeSoftness).GetHashCode();

    /// <summary>Half width and half height of the beam at distance d from the opening.</summary>
    Vector2 HalfSizeAt(float d) => new Vector2(
        0.5f * openingSize.x + d * Mathf.Tan(Mathf.Clamp(spreadDeg.x, 0f, 80f) * Mathf.Deg2Rad),
        0.5f * openingSize.y + d * Mathf.Tan(Mathf.Clamp(spreadDeg.y, 0f, 80f) * Mathf.Deg2Rad));

    void Rebuild()
    {
        if (spot == null) spot = GetComponent<Light>();
        builtHash = Hash();
        float D = Mathf.Max(0.1f, landingDistance > 0f ? landingDistance : beamLength);//where light and beam match
        Vector2 h = HalfSizeAt(D);
        //the spot cone is round and the cookie is stretched over the square around it: the rectangle's corners must
        //stay inside the circle (else they get cut off), so they sit at 95% of its radius
        float diag = Mathf.Sqrt(h.x * h.x + h.y * h.y);
        const float CornerRadius = 0.95f;
        Vector2 fill = h / diag * CornerRadius;//rectangle's half size in the cookie, as a fraction of the cookie's half size
        float angle = 2f * Mathf.Atan(diag / (CornerRadius * D)) * Mathf.Rad2Deg;

        spot.type = LightType.Spot;
        spot.spotAngle = Mathf.Clamp(angle, 1f, 179f);
        spot.innerSpotAngle = spot.spotAngle;
        spot.range = range > 0f ? range : 2f * D;
        BuildCookie(fill);
        spot.cookie = cookie;
        builtAngle = spot.spotAngle;
        builtRange = spot.range;
        BuildBeam(Mathf.Max(0.1f, beamLength));
    }

    void BuildCookie(Vector2 fill)
    {
        const int N = 128;
        if (cookie == null)
        {
            cookie = new Texture2D(N, N, TextureFormat.RGBA32, false, true)
            {
                name = "Rect lamp cookie (generated)", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
        }
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            //-1..1 over the texture
            float u = Mathf.Abs((x + 0.5f) / N * 2f - 1f), v = Mathf.Abs((y + 0.5f) / N * 2f - 1f);
            float a = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fill.x * (1f - edgeSoftness), fill.x, u)))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fill.y * (1f - edgeSoftness), fill.y, v)));
            //the outermost pixels stay black (clamped edges continue them outward)
            if (x == 0 || y == 0 || x == N - 1 || y == N - 1) a = 0f;
            byte b = (byte)Mathf.RoundToInt(a * 255f);
            px[y * N + x] = new Color32(b, b, b, b);
        }
        cookie.SetPixels32(px);
        cookie.Apply(false);
    }

    /// <summary>Four sides of a truncated pyramid (no end caps), in this object's space without its scale.</summary>
    void BuildBeam(float D)
    {
        if (beam == null) beam = new Mesh { name = "Rect lamp beam (generated)", hideFlags = HideFlags.DontSave };
        Vector2 a = 0.5f * openingSize, b = HalfSizeAt(D);
        Vector2[] corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        var verts = new Vector3[8];
        var norms = new Vector3[8];
        var uvs = new Vector2[8];
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = corners[i];
            verts[i] = new Vector3(c.x * a.x, c.y * a.y, 0f);
            verts[i + 4] = new Vector3(c.x * b.x, c.y * b.y, D);
            //normals point diagonally out at the corners, so they blend smoothly over each side (a soft, round look)
            norms[i] = norms[i + 4] = new Vector3(c.x, c.y, 0f).normalized;
            uvs[i] = new Vector2(i / 4f, 0f);
            uvs[i + 4] = new Vector2(i / 4f, 1f);
        }
        var tris = new int[24];
        for (int i = 0, t = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            tris[t++] = i; tris[t++] = j; tris[t++] = j + 4;
            tris[t++] = i; tris[t++] = j + 4; tris[t++] = i + 4;
        }
        beam.Clear();
        beam.vertices = verts;
        beam.normals = norms;
        beam.uv = uvs;
        beam.triangles = tris;
        beam.RecalculateBounds();
    }

    void DrawBeam(ScriptableRenderContext context, Camera cam)
    {
        if (!showBeam || beam == null || beamMaterial == null || !isActiveAndEnabled) return;
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;
        var rp = new RenderParams(beamMaterial)
        {
            camera = cam, layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
        };
        Graphics.RenderMesh(rp, beam, 0, Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one));
    }

    void OnDrawGizmosSelected()
    {
        //outline of the beam (also when the visible beam is off)
        float D = Mathf.Max(0.1f, beamLength);
        Vector2 a = 0.5f * openingSize, b = HalfSizeAt(D);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.8f);
        Vector3[] near = new Vector3[4], far = new Vector3[4];
        Vector2[] c = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
        for (int i = 0; i < 4; i++) { near[i] = new Vector3(c[i].x * a.x, c[i].y * a.y, 0f); far[i] = new Vector3(c[i].x * b.x, c[i].y * b.y, D); }
        for (int i = 0; i < 4; i++)
        {
            Gizmos.DrawLine(near[i], near[(i + 1) % 4]);
            Gizmos.DrawLine(far[i], far[(i + 1) % 4]);
            Gizmos.DrawLine(near[i], far[i]);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

#if UNITY_EDITOR
    Material CreateBeamMaterial()
    {
        const string path = "Assets/Materials/LightBeam.mat";
        var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find("BotanicLab/LightBeam");
        if (shader == null) return null;
        var m = new Material(shader) { name = "LightBeam" };
        UnityEditor.AssetDatabase.CreateAsset(m, path);
        return m;
    }
#endif
}
