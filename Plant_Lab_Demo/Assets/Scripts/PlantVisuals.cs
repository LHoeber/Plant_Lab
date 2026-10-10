using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns the simulation into what you see: one mesh for all branches, one GameObject per leaf and per flower.
/// Reads the simulation, never changes it. Once the simulation says a leaf/flower has fully withered,
/// it's handed over to Unity's physics and falls (not deterministic, on purpose).
/// </summary>
public class PlantVisuals
{
    readonly Transform root;                                   //the plant; leaves and flowers become its children
    readonly Mesh mesh;
    readonly MeshRenderer stemRenderer;
    readonly List<OrganVisual> leafVisuals = new List<OrganVisual>();   //same order as PlantSimulation.Leaves (null = no prefab)
    readonly List<OrganVisual> flowerVisuals = new List<OrganVisual>(); //same order as PlantSimulation.Flowers (null = no prefab)

    //buffers, reused every rebuild to avoid newly allocating memory every frame
    readonly List<Vector3> pts = new List<Vector3>();//center line of one branch
    readonly List<float> radii = new List<float>();  //radius at each of those points
    readonly List<Color> pointColors = new List<Color>();//stem color at each of those points
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector3> norms = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<Color> colors = new List<Color>();
    readonly List<int> tris = new List<int>();
    readonly List<float> arc = new List<float>();

    const float LostBelowY = -100f;//fallen organs below this (no ground in the scene) get removed

    public PlantVisuals(Transform root, MeshFilter meshFilter, MeshRenderer meshRenderer)
    {
        this.root = root;
        stemRenderer = meshRenderer;
        mesh = new Mesh { name = "Plant" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;//allows more vertices than Int16 if needed
        mesh.MarkDynamic();//optimizes performance, because mesh gets rewritten very often
        meshFilter.sharedMesh = mesh;//hands the mesh over meshFilter to the meshRenderer for drawing it
    }

    /// <summary>Removes everything drawn (before a regrow), including fallen leaves/flowers on the ground.</summary>
    public void Clear()
    {
        foreach (var v in leafVisuals) v?.Destroy();
        leafVisuals.Clear();
        foreach (var v in flowerVisuals) v?.Destroy();
        flowerVisuals.Clear();
        mesh.Clear();
    }

    public void RebuildMesh(PlantSimulation sim)
    {
        PlantSettings s = sim.Settings;
        //fresh stem color comes from the stem material; withering changes it per point via vertex colors
        //(only visible with a material whose shader uses vertex colors, see the PlantStem shader graph)
        Material stemMat = stemRenderer != null ? stemRenderer.sharedMaterial : null;
        Color stemColor = stemMat != null && stemMat.HasProperty("_BaseColor") ? stemMat.GetColor("_BaseColor") : Color.white;
        bool linearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;

        verts.Clear(); norms.Clear(); uvs.Clear(); colors.Clear(); tris.Clear();
        foreach (Branch b in sim.Branches)
        {
            if (!Valid(b.TipPosition) || !Valid(b.nodes[b.nodes.Count - 1])) { WarnInvalid("branch"); continue; }
            pts.Clear(); radii.Clear(); pointColors.Clear();
            pts.AddRange(b.nodes);
            radii.AddRange(b.nodeRadius);
            for (int k = 0; k < b.nodes.Count; k++)
            {
                Color c = s.WitherColor(stemColor, sim.StemWither(b, b.nodeArc[k]));
                //material colors get converted to linear space for the shader automatically, vertex colors don't
                pointColors.Add(linearSpace ? c.linear : c);
            }
            Color endColor = pointColors[pointColors.Count - 1];
            //all completed nodes and the still growing tip position (radius 0: brand-new tissue -> pointy)
            //right after segment was committed, the if avoids zero-vector problems
            if (b.tipSegLen > 1e-4f) { pts.Add(b.TipPosition); radii.Add(0f); pointColors.Add(endColor); }
            else if (b.SegmentCount > 0)
            {
                //finished branch: its end has a radius > 0, so close the open tube end with a short cone cap
                float rEnd = radii[radii.Count - 1];
                pts.Add(b.LastNode + b.segDir[b.SegmentCount - 1] * rEnd);
                radii.Add(0f);
                pointColors.Add(endColor);
            }
            TubeMesh.Append(pts, radii, pointColors, PlantSettings.RadialSegments, arc, verts, norms, uvs, colors, tris);
        }
        mesh.Clear();//before setting new data, in case the plant was reset or has fewer vertices now
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);//also recalculates the bounds (calculateBounds defaults to true)
    }

    public void UpdateLeaves(PlantSimulation sim)
    {
        PlantSettings s = sim.Settings;
        //create visuals for leaves spawned since the last frame
        for (int i = leafVisuals.Count; i < sim.Leaves.Count; i++)
        {
            Leaf leaf = sim.Leaves[i];
            GameObject prefab = leaf.variant >= 0 && s.leafPrefabs != null && leaf.variant < s.leafPrefabs.Length
                ? s.leafPrefabs[leaf.variant] : null;
            OrganVisual v = null;
            if (prefab != null)//a slot in the array can still be empty
            {
                //child of the plant, so localPosition/localRotation are in the same space as the nodes
                Transform t = Object.Instantiate(prefab, root).transform;
                t.localScale = Vector3.zero;//starting off invisible ensures it doesn't flicker up in full size before rescaled
                v = new OrganVisual(t);
            }
            leafVisuals.Add(v);
        }

        for (int i = 0; i < leafVisuals.Count; i++)
        {
            OrganVisual v = leafVisuals[i];
            if (v == null || v.t == null) continue;
            if (v.detached) { if (v.t.position.y < LostBelowY) { v.Destroy(); leafVisuals[i] = null; } continue; }//physics moves it now

            Leaf leaf = sim.Leaves[i];
            //leaf base sits on its branch's surface; it moves along when the branch bends
            //and outward as the branch thickens
            Vector3 leafPos = sim.LeafBasePos(leaf);
            if (!Valid(leafPos)) { WarnInvalid("leaf"); continue; }
            v.t.localPosition = leafPos;

            //blade outward, tilted up toward the branch direction by the elevation angle (same as in the collision check)
            v.t.localRotation = sim.LeafRotationNow(leaf);

            //the only place where growth turns into visuals -> replace this line for the morphing leaf later
            v.t.localScale = Vector3.one * sim.LeafSize(leaf);

            float w = sim.LeafWither(leaf);
            v.ApplyWither(w, s);
            if (w >= 1f) v.Detach(s);//fully withered: falls off
        }
    }

    public void UpdateFlowers(PlantSimulation sim)
    {
        PlantSettings s = sim.Settings;
        //create visuals for flowers spawned since the last frame
        for (int i = flowerVisuals.Count; i < sim.Flowers.Count; i++)
        {
            Flower flower = sim.Flowers[i];
            GameObject prefab = flower.variant >= 0 && s.flowerPrefabs != null && flower.variant < s.flowerPrefabs.Length
                ? s.flowerPrefabs[flower.variant] : null;
            OrganVisual v = null;
            if (prefab != null)
            {
                //FlowerMorph.Awake runs inside Instantiate and remembers the prefab's own scale from there
                Transform t = Object.Instantiate(prefab, root).transform;
                v = new OrganVisual(t);
                if (v.morph == null) Debug.LogWarning($"Flower prefab '{prefab.name}' has no FlowerMorph on its root: it won't open.", prefab);
            }
            flowerVisuals.Add(v);
        }

        for (int i = 0; i < flowerVisuals.Count; i++)
        {
            OrganVisual v = flowerVisuals[i];
            if (v == null || v.t == null) continue;
            if (v.detached) { if (v.t.position.y < LostBelowY) { v.Destroy(); flowerVisuals[i] = null; } continue; }

            Flower flower = sim.Flowers[i];
            //base at the branch's end point
            Vector3 flowerPos = sim.FlowerPosition(flower);
            if (!Valid(flowerPos)) { WarnInvalid("flower"); continue; }
            v.t.localPosition = flowerPos;
            //prefab's +Y turned to the last growth direction, plus the flower's own random turn around that axis
            v.t.localRotation = sim.FlowerRotation(flower);
            //opening and size are FlowerMorph's job (blend shapes + scale); a new bud first grows in from size 0
            if (v.morph != null)
                v.morph.SetGrowth(v.morph.GrowthFor(sim.FlowerGrowth(flower), sim.FruitProgress(flower)),
                                  sim.FlowerSize(flower));

            float w = sim.FlowerWither(flower);
            v.ApplyWither(w, s);
            if (w >= 1f) v.Detach(s);
        }
    }

    /// <summary>
    /// Draws the collision boxes of all leaves and flowers still on the plant (Scene view, for checking the
    /// voxel resolution). Flowers show the boxes of the morph state they're currently closest to.
    /// </summary>
    public void DrawCollisionShapes(OrganCompound[] leafShapes, OrganCompound[] flowerShapes, PlantSimulation sim)
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
        for (int i = 0; i < leafVisuals.Count && i < sim.Leaves.Count; i++)
        {
            OrganVisual v = leafVisuals[i];
            int variant = sim.Leaves[i].variant;
            if (v == null || v.t == null || v.detached || leafShapes == null || variant < 0 || variant >= leafShapes.Length) continue;
            DrawBoxes(leafShapes[variant]?.union, v.t);
        }
        Gizmos.color = new Color(1f, 0.5f, 0.9f, 0.8f);
        for (int i = 0; i < flowerVisuals.Count && i < sim.Flowers.Count; i++)
        {
            OrganVisual v = flowerVisuals[i];
            int variant = sim.Flowers[i].variant;
            if (v == null || v.t == null || v.detached || flowerShapes == null || variant < 0 || variant >= flowerShapes.Length) continue;
            OrganCompound c = flowerShapes[variant];
            Flower fl = sim.Flowers[i];
            //the same pose the collision check uses (drawn at the flower's actual size via its transform)
            if (c != null) DrawBoxes(c.BoxesFor(sim.FlowerGrowth(fl), sim.FruitProgress(fl), sim.FlowerWither(fl), out _), v.t);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    static void DrawBoxes(OrganBox[] boxes, Transform t)
    {
        if (boxes == null) return;
        //boxes are in the organ's own space at size 1; the organ's transform (incl. its current scale) places them
        Gizmos.matrix = t.localToWorldMatrix;
        foreach (OrganBox b in boxes) Gizmos.DrawWireCube(b.center, b.half * 2f);
    }

    //safety net: an invalid position (infinite/NaN) would crash Unity's renderer; skip it and report once
    static bool Valid(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                                      float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    bool warnedInvalid;
    void WarnInvalid(string what)
    {
        if (warnedInvalid) return;
        warnedInvalid = true;
        Debug.LogWarning($"PlantVisuals: a {what} got an invalid position (NaN/infinite) and isn't drawn. Please report when this happens.");
    }

    //meshes created with new Mesh() live on Unity's native side and aren't garbage collected
    public void Dispose()
    {
        if (mesh != null) Object.Destroy(mesh);
    }

    /// <summary>
    /// The visual side of one leaf or flower: its GameObject, its renderers' fresh colors (for withering),
    /// and whether it has been handed over to physics.
    /// </summary>
    class OrganVisual
    {
        public readonly Transform t;
        public readonly FlowerMorph morph;  //null for leaves
        public bool detached;               //falling/lying on the ground, moved by physics only

        readonly Renderer[] renderers;
        readonly Color[][] freshColors;     //[renderer][material slot]
        float appliedWither = 0f;

        //one shared block: changes colors per object without creating material copies
        static MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public OrganVisual(Transform t)
        {
            this.t = t;
            morph = t.GetComponent<FlowerMorph>();
            renderers = t.GetComponentsInChildren<Renderer>(true);
            freshColors = new Color[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                freshColors[r] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    freshColors[r][m] = mats[m] != null && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId) : Color.white;
            }
        }

        /// <summary>Discolors all materials of this organ (0 = fresh, 1 = fully withered).</summary>
        public void ApplyWither(float w, PlantSettings s)
        {
            //flowers handle their colors themselves (ripening fruit colors + withering on top)
            if (morph != null) { morph.SetWither(w, s); return; }
            if (Mathf.Abs(w - appliedWither) < 0.002f) return;//no visible change
            appliedWither = w;
            if (block == null) block = new MaterialPropertyBlock();
            for (int r = 0; r < renderers.Length; r++)
                for (int m = 0; m < freshColors[r].Length; m++)
                {
                    block.Clear();
                    block.SetColor(BaseColorId, s.WitherColor(freshColors[r][m], w));
                    renderers[r].SetPropertyBlock(block, m);//per material slot
                }
        }

        /// <summary>Hands the organ over to Unity's physics: it falls, tumbles and comes to rest on the ground.</summary>
        public void Detach(PlantSettings s)
        {
            if (detached) return;
            detached = true;
            //off the plant: physics objects shouldn't be children of something that may move
            t.SetParent(null, true);

            //thin box around the organ's visible geometry, as collider
            Bounds b = LocalBounds();
            var box = t.gameObject.AddComponent<BoxCollider>();
            box.center = b.center;
            Vector3 size = b.size;
            float minThickness = 0.05f * Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            box.size = Vector3.Max(size, Vector3.one * minThickness);

            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.01f;
            rb.linearDamping = PlantSettings.FallDamping;        //air resistance: slow, floaty fall
            rb.angularDamping = PlantSettings.FallAngularDamping;
            rb.angularVelocity = Random.insideUnitSphere * 0.4f;//a slight initial tumble (physics part isn't deterministic anyway)
            //if it overlaps something when detaching (e.g. a neighbor that detached at the same moment), separate gently:
            //Unity's default pushes overlapping bodies apart at up to 10 m/s, which made leaves visibly jump
            rb.maxDepenetrationVelocity = 0.1f;
            //smooth motion between physics steps (physics runs at a fixed rate, the screen doesn't)
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>Bounding box of all its renderers, in this organ's own local space.</summary>
        Bounds LocalBounds()
        {
            bool any = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            Matrix4x4 toRoot = t.worldToLocalMatrix;
            foreach (Renderer r in renderers)
            {
                Bounds lb;
                if (r is SkinnedMeshRenderer smr) lb = smr.localBounds;
                else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null) lb = mf.sharedMesh.bounds;
                else continue;
                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)//all 8 corners of the box, transformed into the organ's space
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        public void Destroy()
        {
            if (t != null) Object.Destroy(t.gameObject);
        }
    }
}
