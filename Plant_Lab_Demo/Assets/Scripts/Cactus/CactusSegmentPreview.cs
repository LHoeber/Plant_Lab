using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows one cactus segment in the scene, built from a CactusShape, for tuning its look: every slider change is shown
/// immediately, also outside Play mode. The segment's attachment point is at this object's position, its axis along
/// this object's up (+y) to the tip. Tufts (your Blender model, or white dots until there is one) sit on the rib ridges.
/// Generated objects are not saved with the scene; they're rebuilt whenever needed.
/// </summary>
[ExecuteAlways]
public class CactusSegmentPreview : MonoBehaviour
{
    public CactusShape shape = new CactusShape();

    [Header("State")]
    [Tooltip("Size during growth: 1 = full grown.")]
    [Range(0.05f, 1f)] public float growth = 1f;
    [Tooltip("0 = fresh, 1 = fully withered (deflated toward a thin core).")]
    [Range(0f, 1f)] public float wither = 0f;
    [Tooltip("Seed for the small random variation of the tufts (position along the rib, turn).")]
    public int seed = 0;

    [Header("Look")]
    public Material bodyMaterial;
    [Tooltip("Mesh resolution: rings from base to tip, and columns around the axis (more columns = sharper ribs).")]
    [Range(8, 96)] public int rings = 40;
    [Range(12, 256)] public int columns = 96;

    [Header("Tufts")]
    [Tooltip("The tuft model (Blender). Its first mesh and material are used; +y of the model points along the spines. " +
             "Empty: small white dots.")]
    public GameObject tuftPrefab;
    [Tooltip("Scale of the tuft model (1 = as modeled). Without a model: dot diameter = 0.012 m x this.")]
    [Range(0.05f, 5f)] public float tuftScale = 1f;
    [Tooltip("Direction of the tufts' spines: 0 = straight out of the surface, 1 = all along the segment's axis (base to tip).")]
    [Range(0f, 1f)] public float tuftAxisAlignment = 1f;
    [Tooltip("How far the tufts sit out of the surface (m).")]
    [Range(-0.01f, 0.02f)] public float tuftOffset = 0f;
    [Tooltip("Random turn of each tuft around its own axis (degrees).")]
    [Range(0f, 180f)] public float tuftRandomTurn = 180f;

    [Header("Gizmos")]
    public bool showAxis = true;

    Mesh bodyMesh, tuftMesh, dotMesh;
    Material defaultBody, defaultTuft;
    MeshFilter bodyFilter, tuftFilter;
    MeshRenderer bodyRenderer, tuftRenderer;
    bool dirty = true;

    void OnValidate() => dirty = true;
    void OnEnable() => dirty = true;

    void Update()
    {
        if (!dirty) return;
        dirty = false;
        Rebuild();
    }

    void Rebuild()
    {
        EnsureChildren();
        bodyMesh = shape.BuildMesh(bodyMesh, rings, columns, growth, wither);
        bodyFilter.sharedMesh = bodyMesh;
        bodyRenderer.sharedMaterial = bodyMaterial != null ? bodyMaterial : DefaultMaterial(ref defaultBody, new Color(0.35f, 0.6f, 0.25f));

        //tufts: one combined mesh (hundreds of separate objects would be slow)
        Mesh source = null;
        Material tuftMat = null;
        Matrix4x4 sourceToRoot = Matrix4x4.identity;
        if (tuftPrefab != null)
        {
            MeshFilter mf = tuftPrefab.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                source = mf.sharedMesh;
                //the mesh's own placement inside the prefab (without the prefab root's position)
                sourceToRoot = tuftPrefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                tuftMat = mf.GetComponent<MeshRenderer>() != null ? mf.GetComponent<MeshRenderer>().sharedMaterial : null;
            }
        }
        if (source == null)
        {
            source = dotMesh != null ? dotMesh : (dotMesh = Dot());
            sourceToRoot = Matrix4x4.Scale(Vector3.one * 0.012f);
        }
        if (tuftMat == null) tuftMat = DefaultMaterial(ref defaultTuft, new Color(0.92f, 0.92f, 0.85f));

        List<CactusShape.SurfacePoint> areoles = shape.Areoles(seed, growth, wither);
        var rng = new System.Random(seed * 7919 + 1);
        var parts = new CombineInstance[areoles.Count];
        for (int i = 0; i < areoles.Count; i++)
        {
            CactusShape.SurfacePoint p = areoles[i];
            Vector3 dir = Vector3.Slerp(p.normal, Vector3.up, tuftAxisAlignment);
            float turn = ((float)rng.NextDouble() * 2f - 1f) * tuftRandomTurn;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.AngleAxis(turn, Vector3.up);
            parts[i] = new CombineInstance
            {
                mesh = source,
                transform = Matrix4x4.TRS(p.position + p.normal * tuftOffset, rot, Vector3.one * tuftScale) * sourceToRoot,
            };
        }
        if (tuftMesh == null) tuftMesh = new Mesh { name = "Cactus tufts", hideFlags = HideFlags.DontSave };
        tuftMesh.Clear();
        tuftMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        if (parts.Length > 0) tuftMesh.CombineMeshes(parts, true, true);
        tuftFilter.sharedMesh = tuftMesh;
        tuftRenderer.sharedMaterial = tuftMat;
    }

    void EnsureChildren()
    {
        if (bodyFilter == null) (bodyFilter, bodyRenderer) = Child("Segment (generated)");
        if (tuftFilter == null) (tuftFilter, tuftRenderer) = Child("Tufts (generated)");
        //after a script reload the fields are empty, but the children still hold their meshes: reuse those
        if (bodyMesh == null) bodyMesh = bodyFilter.sharedMesh != null ? bodyFilter.sharedMesh : new Mesh { name = "Cactus segment", hideFlags = HideFlags.DontSave };
        if (tuftMesh == null && tuftFilter.sharedMesh != null) tuftMesh = tuftFilter.sharedMesh;
    }

    (MeshFilter, MeshRenderer) Child(string name)
    {
        Transform t = transform.Find(name);
        if (t == null)
        {
            //not saved with the scene: generated again whenever the scene is opened
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            t = go.transform;
        }
        return (t.GetComponent<MeshFilter>(), t.GetComponent<MeshRenderer>());
    }

    static Material DefaultMaterial(ref Material m, Color c)
    {
        if (m != null) return m;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(sh != null ? sh : Shader.Find("Standard")) { hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    /// <summary>A small low-poly ball of diameter 1 (the tuft until there's a model).</summary>
    static Mesh Dot()
    {
        const int lat = 6, lon = 8;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= lat; i++)
        {
            float phi = Mathf.PI * i / lat;
            for (int j = 0; j <= lon; j++)
            {
                float th = 2f * Mathf.PI * j / lon;
                verts.Add(0.5f * new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), -Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)));
            }
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, b = a + lon + 1;
                tris.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }
        var m = new Mesh { name = "Tuft dot", hideFlags = HideFlags.DontSave };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        return m;
    }

    void OnDrawGizmosSelected()
    {
        if (!showAxis) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 tip = shape.Tip(growth, wither);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(Vector3.zero, tip);
        Gizmos.DrawWireSphere(Vector3.zero, 0.005f);//attachment
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(tip, 0.008f);//tip: where the flower sits
    }

    void OnDestroy()
    {
        DestroySafe(bodyMesh); DestroySafe(tuftMesh); DestroySafe(dotMesh);
        DestroySafe(defaultBody); DestroySafe(defaultTuft);
        if (bodyFilter != null) DestroySafe(bodyFilter.gameObject);
        if (tuftFilter != null) DestroySafe(tuftFilter.gameObject);
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
