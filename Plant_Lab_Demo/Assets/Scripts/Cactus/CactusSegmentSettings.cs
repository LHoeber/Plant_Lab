using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One cactus segment type as an asset: its shape (CactusShape), its look (material, mesh resolution) and its tufts.
/// Used by S(s) in L-system rules (preview and plant), and shown/tuned alone by CactusSegmentPreview.
/// Create: Assets > Create > Botanic Lab > Cactus Segment, or "Save as segment asset" in CactusSegmentPreview's menu (⋮).
/// </summary>
[CreateAssetMenu(fileName = "CactusSegment", menuName = "Botanic Lab/Cactus Segment")]
public class CactusSegmentSettings : ScriptableObject
{
    //field names match CactusSegmentPreview's own fields, so values can be copied between them as they are
    public CactusShape shape = new CactusShape();

    [Header("Look")]
    public Material bodyMaterial;
    [Tooltip("Mesh resolution: rings from base to tip of the ball (a column gets more), and columns around the axis " +
             "(more columns = sharper ribs).")]
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

    /// <summary>Increases whenever a value is changed in the Inspector, so previews know to redraw.</summary>
    public int Version { get; private set; }
    void OnValidate() => Version++;

    static Mesh dotMesh;
    static Material defaultBody, defaultTuft;

    public Material BodyMaterial => bodyMaterial != null ? bodyMaterial : DefaultMaterial(ref defaultBody, new Color(0.35f, 0.6f, 0.25f));

    /// <summary>The tuft mesh with its placement inside the prefab, and its material (white dots without a model).</summary>
    public (Mesh mesh, Matrix4x4 meshToRoot, Material material) TuftSource()
    {
        if (tuftPrefab != null)
        {
            MeshFilter mf = tuftPrefab.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                //the mesh's own placement inside the prefab (without the prefab root's position)
                Matrix4x4 toRoot = tuftPrefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                Material m = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial : DefaultMaterial(ref defaultTuft, new Color(0.92f, 0.92f, 0.85f));
                return (mf.sharedMesh, toRoot, m);
            }
        }
        if (dotMesh == null) dotMesh = Dot();
        return (dotMesh, Matrix4x4.Scale(Vector3.one * 0.012f), DefaultMaterial(ref defaultTuft, new Color(0.92f, 0.92f, 0.85f)));
    }

    /// <summary>
    /// Adds the tufts of one segment to `parts` (for Mesh.CombineMeshes): every areole except the ones in `skip`
    /// (e.g. areoles a child segment grows out of). segmentToRoot places the segment (its base, axis +y) in the combined mesh.
    /// </summary>
    public void AddTufts(List<CombineInstance> parts, List<CactusShape.SurfacePoint> areoles, Matrix4x4 segmentToRoot,
                         int seed, ICollection<int> skip = null)
    {
        var (mesh, meshToRoot, _) = TuftSource();
        var rng = new System.Random(seed * 7919 + 1);
        for (int i = 0; i < areoles.Count; i++)
        {
            float turn = ((float)rng.NextDouble() * 2f - 1f) * tuftRandomTurn;//always drawn: the others keep their turn
            if (skip != null && skip.Contains(i)) continue;
            CactusShape.SurfacePoint p = areoles[i];
            Vector3 dir = Vector3.Slerp(p.normal, Vector3.up, tuftAxisAlignment);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.AngleAxis(turn, Vector3.up);
            parts.Add(new CombineInstance
            {
                mesh = mesh,
                transform = segmentToRoot * Matrix4x4.TRS(p.position + p.normal * tuftOffset, rot, Vector3.one * tuftScale) * meshToRoot,
            });
        }
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

    /// <summary>A child object with a mesh, not saved with the scene (rebuilt whenever needed).</summary>
    public static (MeshFilter, MeshRenderer) GeneratedChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        return (t.GetComponent<MeshFilter>(), t.GetComponent<MeshRenderer>());
    }
}
