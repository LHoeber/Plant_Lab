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
    [Tooltip("The tuft model (Blender). All its meshes and materials are used (e.g. spikes + fuzz joined into one object); " +
             "+y of the model points along the spines. " +
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

    /// <summary>
    /// One part of the tuft model: one submesh of one of its meshes, with the mesh's placement inside the prefab and the
    /// material it's drawn with. A model joined from objects with different materials (e.g. spikes + fuzz) has one part
    /// per material; each part becomes its own submesh in the combined tuft mesh, so each keeps its own material.
    /// </summary>
    public struct TuftPart
    {
        public Mesh mesh;
        public int subMesh;
        public Matrix4x4 meshToRoot;
        public Material material;
    }

    readonly List<TuftPart> tuftParts = new List<TuftPart>();//reused: filled again on every call of TuftParts()

    /// <summary>The parts of the tuft model (all meshes in the prefab, each submesh with its material); a white dot without a model.</summary>
    public List<TuftPart> TuftParts()
    {
        tuftParts.Clear();
        if (tuftPrefab != null)
        {
            foreach (MeshFilter mf in tuftPrefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                //the mesh's own placement inside the prefab (without the prefab root's position)
                Matrix4x4 toRoot = tuftPrefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                Material[] mats = mr != null ? mr.sharedMaterials : System.Array.Empty<Material>();
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    //a renderer with fewer materials than submeshes draws the extra submeshes with its last material
                    Material m = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (m == null) m = DefaultMaterial(ref defaultTuft, new Color(0.92f, 0.92f, 0.85f));
                    tuftParts.Add(new TuftPart { mesh = mf.sharedMesh, subMesh = s, meshToRoot = toRoot, material = m });
                }
            }
        }
        if (tuftParts.Count == 0)
        {
            if (dotMesh == null) dotMesh = Dot();
            tuftParts.Add(new TuftPart { mesh = dotMesh, subMesh = 0, meshToRoot = Matrix4x4.Scale(Vector3.one * 0.012f),
                                         material = DefaultMaterial(ref defaultTuft, new Color(0.92f, 0.92f, 0.85f)) });
        }
        return tuftParts;
    }

    /// <summary>The materials for a tuft mesh built by TuftBuilder.Build (one per part of the model, in the same order).</summary>
    public Material[] TuftMaterials()
    {
        List<TuftPart> ps = TuftParts();
        var mats = new Material[ps.Count];
        for (int i = 0; i < ps.Count; i++) mats[i] = ps[i].material;
        return mats;
    }

    /// <summary>
    /// Adds the tufts of one segment to `tufts` (combined later by tufts.Build): every areole except the ones in `skip`
    /// (indices into `areoles`, e.g. areoles a child segment grows out of). segmentToRoot places the segment (its base,
    /// axis +y) in the combined mesh; scale multiplies the tufts' size (e.g. smaller on a young, still swelling segment).
    /// </summary>
    public void AddTufts(TuftBuilder tufts, List<CactusShape.SurfacePoint> areoles, Matrix4x4 segmentToRoot,
                         int seed, ICollection<int> skip = null, float scale = 1f)
    {
        List<TuftPart> ps = TuftParts();
        for (int i = 0; i < areoles.Count; i++)
        {
            if (skip != null && skip.Contains(i)) continue;
            CactusShape.SurfacePoint p = areoles[i];
            //turn from the areole's id: it keeps its turn when new tufts appear on a growing column
            float turn = (CactusShape.Hash01(seed + 7919, p.rib, p.index) * 2f - 1f) * tuftRandomTurn;
            Vector3 dir = Vector3.Slerp(p.normal, Vector3.up, tuftAxisAlignment);
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.AngleAxis(turn, Vector3.up);
            Matrix4x4 place = segmentToRoot * Matrix4x4.TRS(p.position + p.normal * tuftOffset, rot, Vector3.one * (tuftScale * scale));
            for (int k = 0; k < ps.Count; k++)
                tufts.Part(k).Add(new CombineInstance { mesh = ps[k].mesh, subMeshIndex = ps[k].subMesh, transform = place * ps[k].meshToRoot });
        }
    }

    /// <summary>
    /// Collects tufts (AddTufts) and combines them into one mesh with one submesh per part of the tuft model, so a model
    /// with several materials keeps them all (give the renderer TuftMaterials()). Mesh.CombineMeshes can either merge
    /// everything into one submesh or keep every single tuft as its own submesh; so each part is first merged on its own
    /// into a temporary mesh, and these are then combined as separate submeshes.
    /// </summary>
    public class TuftBuilder
    {
        readonly List<List<CombineInstance>> parts = new List<List<CombineInstance>>();
        readonly List<Mesh> temp = new List<Mesh>();

        public void Clear() { foreach (var p in parts) p.Clear(); }

        /// <summary>The tufts collected for part k of the model.</summary>
        public List<CombineInstance> Part(int k)
        {
            while (parts.Count <= k) parts.Add(new List<CombineInstance>());
            return parts[k];
        }

        /// <summary>Puts all collected tufts into `target` (cleared first), one submesh per part.</summary>
        public void Build(Mesh target)
        {
            target.Clear();
            target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int n = parts.Count;
            while (n > 0 && parts[n - 1].Count == 0) n--;//parts left over from an earlier model with more materials
            if (n == 0) return;
            if (n == 1) { target.CombineMeshes(parts[0].ToArray(), true, true); return; }
            var perPart = new CombineInstance[n];
            for (int k = 0; k < n; k++)
            {
                if (temp.Count <= k) temp.Add(new Mesh { name = "Tuft part", hideFlags = HideFlags.DontSave });
                temp[k].Clear();
                temp[k].indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                temp[k].CombineMeshes(parts[k].ToArray(), true, true);
                perPart[k] = new CombineInstance { mesh = temp[k], transform = Matrix4x4.identity };
            }
            target.CombineMeshes(perPart, false, true);
        }

        /// <summary>Destroys the temporary meshes.</summary>
        public void Release()
        {
            foreach (Mesh m in temp)
            {
                if (m == null) continue;
                if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m);
            }
            temp.Clear();
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
