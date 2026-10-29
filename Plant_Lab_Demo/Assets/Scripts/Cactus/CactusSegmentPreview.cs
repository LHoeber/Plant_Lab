using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows one cactus segment in the scene, for tuning its look: every slider change is shown immediately, also outside
/// Play mode. The segment's attachment point is at this object's position, its axis along this object's up (+y) to the tip.
/// Tufts (your Blender model, or white dots until there is one) sit on the rib ridges.
///
/// With a Segment asset assigned, that asset is shown (edit it in its own Inspector; L-system rules use it with S(s)).
/// Without one, the fields below are used; "Save as segment asset" in this component's menu (⋮) turns them into an asset.
/// Generated objects are not saved with the scene; they're rebuilt whenever needed.
/// </summary>
[ExecuteAlways]
public class CactusSegmentPreview : MonoBehaviour
{
    [Tooltip("The segment type to show. Empty: the fields below.")]
    public CactusSegmentSettings segment;

    [Header("State")]
    [Tooltip("Size during growth: 1 = full grown.")]
    [Range(0.05f, 1f)] public float growth = 1f;
    [Tooltip("0 = fresh, 1 = fully withered (deflated toward a thin core).")]
    [Range(0f, 1f)] public float wither = 0f;
    [Tooltip("Seed for the small random variation of the tufts (position along the rib, turn).")]
    public int seed = 0;

    [Header("Gizmos")]
    public bool showAxis = true;

    [Header("Own values (used without a Segment asset)")]
    public CactusShape shape = new CactusShape();
    public Material bodyMaterial;
    [Range(8, 96)] public int rings = 40;
    [Range(12, 256)] public int columns = 96;
    public GameObject tuftPrefab;
    [Range(0.05f, 5f)] public float tuftScale = 1f;
    [Range(0f, 1f)] public float tuftAxisAlignment = 1f;
    [Range(-0.01f, 0.02f)] public float tuftOffset = 0f;
    [Range(0f, 180f)] public float tuftRandomTurn = 180f;

    Mesh bodyMesh, tuftMesh;
    MeshFilter bodyFilter, tuftFilter;
    MeshRenderer bodyRenderer, tuftRenderer;
    CactusSegmentSettings ownValues;//a temporary asset holding the fields above (the same code then builds both cases)
    bool dirty = true;
    int segmentVersion = -1;

    void OnValidate() => dirty = true;
    void OnEnable() => dirty = true;

    /// <summary>The segment type being shown: the asset, or the own values.</summary>
    CactusSegmentSettings Source
    {
        get
        {
            if (segment != null) return segment;
            if (ownValues == null) ownValues = ScriptableObject.CreateInstance<CactusSegmentSettings>();
            ownValues.hideFlags = HideFlags.DontSave;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), ownValues);//same field names
            ownValues.bodyMaterial = bodyMaterial;//object references: set directly (JsonUtility only handles them in the editor)
            ownValues.tuftPrefab = tuftPrefab;
            return ownValues;
        }
    }

    void Update()
    {
        if (segment != null && segment.Version != segmentVersion) { segmentVersion = segment.Version; dirty = true; }
        if (!dirty) return;
        dirty = false;
        Rebuild();
    }

    void Rebuild()
    {
        CactusSegmentSettings s = Source;
        if (bodyFilter == null) (bodyFilter, bodyRenderer) = CactusSegmentSettings.GeneratedChild(transform, "Segment (generated)");
        if (tuftFilter == null) (tuftFilter, tuftRenderer) = CactusSegmentSettings.GeneratedChild(transform, "Tufts (generated)");
        //after a script reload the fields are empty, but the children still hold their meshes: reuse those
        if (bodyMesh == null) bodyMesh = bodyFilter.sharedMesh != null ? bodyFilter.sharedMesh : new Mesh { name = "Cactus segment", hideFlags = HideFlags.DontSave };
        if (tuftMesh == null) tuftMesh = tuftFilter.sharedMesh != null ? tuftFilter.sharedMesh : new Mesh { name = "Cactus tufts", hideFlags = HideFlags.DontSave };

        bodyMesh = s.shape.BuildMesh(bodyMesh, s.rings, s.columns, growth, wither);
        bodyFilter.sharedMesh = bodyMesh;
        bodyRenderer.sharedMaterial = s.BodyMaterial;

        //tufts: one combined mesh (hundreds of separate objects would be slow)
        var parts = new List<CombineInstance>();
        s.AddTufts(parts, s.shape.Areoles(seed, growth, wither), Matrix4x4.identity, seed);
        tuftMesh.Clear();
        tuftMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        if (parts.Count > 0) tuftMesh.CombineMeshes(parts.ToArray(), true, true);
        tuftFilter.sharedMesh = tuftMesh;
        tuftRenderer.sharedMaterial = s.TuftSource().material;
    }

    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        //the asset was edited in its own Inspector: Update doesn't run by itself outside Play mode, so ask for one
        if (segment != null && segment.Version != segmentVersion && !Application.isPlaying)
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }

    void OnDrawGizmosSelected()
    {
        if (!showAxis) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 tip = (segment != null ? segment.shape : shape).Tip(growth, wither);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(Vector3.zero, tip);
        Gizmos.DrawWireSphere(Vector3.zero, 0.005f);//attachment
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(tip, 0.008f);//tip: where the flower sits
    }

#if UNITY_EDITOR
    /// <summary>Saves the own values as a segment asset (for L-system rules) and shows that asset from now on.</summary>
    [ContextMenu("Save as segment asset")]
    void SaveAsAsset()
    {
        string path = UnityEditor.EditorUtility.SaveFilePanelInProject("Save cactus segment", "CactusSegment", "asset",
                                                                        "Where to save the segment type", "Assets/Settings");
        if (string.IsNullOrEmpty(path)) return;
        var asset = ScriptableObject.CreateInstance<CactusSegmentSettings>();
        UnityEditor.EditorJsonUtility.FromJsonOverwrite(UnityEditor.EditorJsonUtility.ToJson(this), asset);//same field names
        UnityEditor.AssetDatabase.CreateAsset(asset, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.Undo.RecordObject(this, "Use segment asset");
        segment = asset;
        dirty = true;
        UnityEditor.EditorGUIUtility.PingObject(asset);
    }
#endif

    void OnDestroy()
    {
        DestroySafe(bodyMesh); DestroySafe(tuftMesh); DestroySafe(ownValues);
        if (bodyFilter != null) DestroySafe(bodyFilter.gameObject);
        if (tuftFilter != null) DestroySafe(tuftFilter.gameObject);
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
