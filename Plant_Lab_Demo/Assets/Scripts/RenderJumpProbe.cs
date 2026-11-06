using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostic (delete after the "spasm" bug is found). Add it to any object in the scene and press Play.
/// Every frame it checks every renderer under every PlantGrowth: if one is suddenly far larger than an organ can be,
/// or jumps by more than jumpLimit in one frame, it logs what it is (object path, scale, position, renderer type)
/// and saves a screenshot to the project's Logs folder (at most maxShots).
/// </summary>
public class RenderJumpProbe : MonoBehaviour
{
    const float sizeLimit = 1.5f;   //m: no single leaf/flower/segment renderer should be larger (the glass is ~1 m)
    const float jumpLimit = 0.05f;  //m: bounds center or size change in one frame
    const int maxShots = 10;
    const int maxLogs = 60;

    readonly Dictionary<Renderer, Bounds> last = new Dictionary<Renderer, Bounds>();
    int shots, logs, frame, regular;
    float nextRegular;

    void LateUpdate()
    {
        frame++;
        bool bad = false;
        foreach (PlantGrowth p in FindObjectsByType<PlantGrowth>(FindObjectsSortMode.None))
        {
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;//not drawn
                if (r.name.EndsWith("(generated)")) continue;//editor previews (CactusSegmentPreview, LSystemPreview)
                bool stem = r.transform == p.transform;//the stem mesh: the whole plant, so larger limits
                float limitSize = stem ? 2.5f : sizeLimit, limitJump = stem ? 0.2f : jumpLimit;
                Bounds b = r.bounds;
                string why = null;
                if (stem && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                {
                    Mesh m = mf.sharedMesh;
                    if (!Valid(m.bounds.center) || !Valid(m.bounds.extents)) why = "stem mesh bounds NaN/infinite";
                    else if (m.vertexCount > 0 && m.GetIndexCount(0) > 0 && !IndicesOk(m)) why = $"stem mesh index out of range ({m.vertexCount} verts)";
                }
                if (why == null && b.size.magnitude > limitSize) why = $"size {b.size.magnitude:0.00} m";
                else if (why == null && last.TryGetValue(r, out Bounds o))
                {
                    float d = Mathf.Max((b.center - o.center).magnitude, Mathf.Abs(b.size.magnitude - o.size.magnitude));
                    if (d > limitJump) why = $"jump {d * 100f:0} cm";
                }
                last[r] = b;
                if (why == null) continue;
                bad = true;
                if (logs++ < maxLogs)
                    Debug.LogWarning($"RenderJumpProbe f{frame} t{Time.time:0.00} dt{Time.deltaTime * 1000f:0}ms {why}: {Path(r.transform)} " +
                                     $"({r.GetType().Name}) localScale {r.transform.localScale} lossyScale {r.transform.lossyScale} " +
                                     $"localPos {r.transform.localPosition} center {b.center} size {b.size} plant load: {p.simLoad}", r);
            }
        }
        if (bad && shots < maxShots)
        {
            string file = $"{Application.dataPath}/../Logs/probe_f{frame:00000}.png";
            ScreenCapture.CaptureScreenshot(file);
            shots++;
        }
        //plus a regular screenshot every ~2 s (to compare with what the log says at that moment)
        if (Time.time >= nextRegular && regular < 20)
        {
            nextRegular = Time.time + 2f;
            regular++;
            ScreenCapture.CaptureScreenshot($"{Application.dataPath}/../Logs/probe_regular_{Time.time:000.0}s_f{frame:00000}.png");
        }
    }

    static bool Valid(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                                      float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    readonly List<int> idx = new List<int>();
    bool IndicesOk(Mesh m)
    {
        if (!m.isReadable) return true;
        m.GetTriangles(idx, 0);
        int n = m.vertexCount;
        foreach (int i in idx) if (i < 0 || i >= n) return false;
        return true;
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
