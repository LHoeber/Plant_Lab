using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the control panel at Play start from a PanelConfig (how many buttons and trials) and a PanelLayout (where
/// things sit): casing, screen, Start, "?", timer bar, the effect buttons in sockets, the LED grid, the shutter and
/// the camera arrows. Every part is a simple block for now; a prefab assigned below (e.g. a part modeled in Blender)
/// is used instead and fitted into the same place. Changing the config or layout while playing rebuilds the panel.
///
/// Panel space: x right, y up, origin = center of the panel face; parts stand out toward -z (the console camera
/// looks at the panel along +z).
/// </summary>
[DefaultExecutionOrder(-100)]//before Console3D and TrialController, which use the parts
public class PanelBuilder : MonoBehaviour
{
    public PanelConfig config;
    public PanelLayout layout;

    [Header("Materials (assigned by Botanic Lab > Build Trial Panel)")]
    public Material casingMaterial;
    [Tooltip("Effect buttons (red, with emission for the 'on' glow).")]
    public Material effectButtonMaterial;
    [Tooltip("Start, ?, arrows (with emission for the 'available' glow).")]
    public Material controlMaterial;
    [Tooltip("Dark parts: sockets, screen bezel, LED and timer backgrounds.")]
    public Material darkMaterial;
    [Tooltip("Unlit, transparent (BotanicLab/PanelUnlit): LEDs, labels, timer fill, shutter glass.")]
    public Material unlitMaterial;

    [Header("Optional parts (fitted into the place of the simple block)")]
    public GameObject effectButtonPrefab;
    public GameObject arrowButtonPrefab;
    public GameObject startPrefab;
    public GameObject helpPrefab;

    [Header("Colors")]
    public Color glassColor = new Color(0.75f, 0.85f, 1f, 0.22f);
    public Color labelColor = new Color(0.95f, 0.95f, 0.9f, 1f);
    public Color timerColor = new Color(1f, 0.75f, 0.25f, 1f);

    // --- the parts, for TrialController ---
    public PanelButton[] EffectButtons { get; private set; } = new PanelButton[0];
    public PanelButton[] Arrows { get; private set; } = new PanelButton[4];//index = LabActions camera action
    public PanelButton StartButton { get; private set; }
    public PanelButton HelpButton { get; private set; }
    public PanelButton Shutter { get; private set; }
    public Renderer[,] ButtonLeds { get; private set; } = new Renderer[0, 0];//[trial, button]
    public Renderer[] DoneLeds { get; private set; } = new Renderer[0];//[trial]
    public Vector3 ShutterOpen { get; private set; }
    public Vector3 ShutterClosed { get; private set; }

    /// <summary>Called after every (re)build.</summary>
    public event Action Rebuilt;

    public static readonly int ColorId = Shader.PropertyToID("_Color"), MainTexId = Shader.PropertyToID("_MainTex");

    Transform root;
    Transform timerFill;
    Rect timerRect;
    readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();//runtime textures, meshes, materials
    Mesh quad;
    Texture2D ledTexture, arrowTexture;
    int configVersion = -1, layoutVersion = -1;
    MaterialPropertyBlock block;

    void Awake() => Build();

    void Update()
    {
        //config or layout edited while playing: build again
        if (config != null && layout != null && (config.Version != configVersion || layout.Version != layoutVersion)) Build();
    }

    public void Build()
    {
        if (config == null || layout == null) { Debug.LogWarning("PanelBuilder: config or layout missing.", this); return; }
        configVersion = config.Version;
        layoutVersion = layout.Version;
        Clear();
        EnsureResources();
        root = new GameObject("Panel").transform;
        root.SetParent(transform, false);
        PanelLayout L = layout;
        float bh = L.buttonHeight, sh = L.socketHeight;

        //casing: a block behind the face (front face at z = 0)
        Block("Casing", new Rect(-0.5f * L.casingSize.x, -0.5f * L.casingSize.y, L.casingSize.x, L.casingSize.y),
              0f, L.casingDepth, casingMaterial, behind: true);

        //screen: a flat quad (Console3D puts the camera image on it), with a dark bezel around it
        Flat("Screen", L.screen, -0.0005f, null, Color.white, keepMaterial: false);
        Frame("ScreenBezel", L.screen, 0.012f, 0f, 0.006f, darkMaterial);

        //Start, ?
        StartButton = Button("Start", L.start, sh, bh, controlMaterial, startPrefab, "START");
        HelpButton = Button("Help", L.help, sh, bh, controlMaterial, helpPrefab, "?");

        //timer bar: dark background, fill shrinks toward the left end
        timerRect = L.timer;
        Block("TimerBackground", L.timer, 0f, 0.002f, darkMaterial);
        timerFill = Flat("TimerFill", Shrink(L.timer, 0.0015f), -0.0025f, null, timerColor).transform;
        SetTimer(1f);

        //effect buttons in a grid, each in a dark socket, numbered
        int n = config.effectButtons;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(n)), rows = Mathf.CeilToInt(n / (float)cols);
        float cell = Mathf.Min(L.buttons.width / cols, L.buttons.height / rows);
        Vector2 gridOrigin = L.buttons.center - 0.5f * new Vector2(cols * cell, -rows * cell);//top-left
        EffectButtons = new PanelButton[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 c = gridOrigin + new Vector2((i % cols + 0.5f) * cell, -(i / cols + 0.5f) * cell);
            Block($"Socket{i + 1}", Centered(c, 0.92f * cell), 0f, sh, darkMaterial);
            EffectButtons[i] = Button($"Button{i + 1}", Centered(c, 0.76f * cell), sh, bh, effectButtonMaterial, effectButtonPrefab, (i + 1).ToString());
        }

        //LED grid: rows = trials (first trial at the top), columns = buttons, then a gap and the 'trial done' column
        int trials = config.trials;
        float colW = L.history.width / (n + 1.6f), rowH = L.history.height / trials;
        float led = 0.7f * Mathf.Min(colW, rowH);
        Block("HistoryBackground", L.history, 0f, 0.002f, darkMaterial);
        ButtonLeds = new Renderer[trials, n];
        DoneLeds = new Renderer[trials];
        for (int t = 0; t < trials; t++)
        {
            float y = L.history.yMax - (t + 0.5f) * rowH;
            for (int b = 0; b < n; b++)
                ButtonLeds[t, b] = Flat($"Led{t + 1}_{b + 1}", Centered(new Vector2(L.history.xMin + (b + 0.5f) * colW, y), led),
                                        -0.0025f, ledTexture, Color.black);
            DoneLeds[t] = Flat($"LedDone{t + 1}", Centered(new Vector2(L.history.xMax - 0.5f * colW, y), led), -0.0025f, ledTexture, Color.black);
        }
        //column numbers above the grid
        for (int b = 0; b < n; b++)
            Label((b + 1).ToString(), new Vector2(L.history.xMin + (b + 0.5f) * colW, L.history.yMax + 0.6f * colW), 0.55f * colW, -0.0005f);

        //shutter: glass pane with a frame, as big as the button area; closed over the buttons, open slid up over Start
        Rect pane = Grow(L.buttons, 0.006f);
        var shutter = new GameObject("Shutter");
        shutter.transform.SetParent(root, false);
        float paneZ = sh + bh + 0.006f;
        Flat("ShutterGlass", Centered(Vector2.zero, pane.size), -paneZ, null, glassColor, parent: shutter.transform);
        Frame("ShutterFrame", Centered(Vector2.zero, pane.size), 0.004f, paneZ - 0.002f, 0.004f, controlMaterial, shutter.transform, outside: false);
        var box = shutter.AddComponent<BoxCollider>();
        box.size = new Vector3(pane.width, pane.height, 0.01f);
        box.center = new Vector3(0f, 0f, -paneZ);
        ShutterClosed = new Vector3(pane.center.x, pane.center.y, 0f);
        ShutterOpen = ShutterClosed + new Vector3(0f, L.shutterTravel, 0f);
        shutter.transform.localPosition = ShutterOpen;
        Shutter = shutter.AddComponent<PanelButton>();

        //camera arrows as a cross
        float s = Mathf.Min(L.arrows.width, L.arrows.height) / 3f;
        Vector2 ac = L.arrows.center;
        Arrows = new PanelButton[4];
        Arrows[LabActions.CameraUp] = Arrow("CamUp", ac + new Vector2(0f, s), s, 0f);
        Arrows[LabActions.CameraDown] = Arrow("CamDown", ac - new Vector2(0f, s), s, 180f);
        Arrows[LabActions.CameraLeft] = Arrow("CamLeft", ac - new Vector2(s, 0f), s, 90f);
        Arrows[LabActions.CameraRight] = Arrow("CamRight", ac + new Vector2(s, 0f), s, -90f);
        for (int a = 0; a < 4; a++) Arrows[a].action = a;

        //pressing pushes a button into the panel (+z)
        Vector3 push = transform.TransformDirection(Vector3.forward) * 0.003f;
        foreach (PanelButton b in GetComponentsInChildren<PanelButton>(true)) if (b != Shutter) b.pressOffset = push;

        //everything on this object's layer (the console camera's)
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        Rebuilt?.Invoke();
    }

    /// <summary>Timer bar: 1 = full, 0 = empty (shrinks toward its left end).</summary>
    public void SetTimer(float fraction)
    {
        if (timerFill == null) return;
        Rect r = Shrink(timerRect, 0.0015f);
        float w = Mathf.Max(1e-5f, r.width * Mathf.Clamp01(fraction));
        timerFill.localPosition = new Vector3(r.xMin + 0.5f * w, r.center.y, timerFill.localPosition.z);
        timerFill.localScale = new Vector3(w, r.height, 1f);
    }

    /// <summary>Color of an unlit part (LED, label, glass).</summary>
    public void SetColor(Renderer r, Color c)
    {
        if (r == null) return;
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(ColorId, c);
        r.SetPropertyBlock(block);
    }

    /// <summary>A part of the current panel by name (e.g. "Casing", "Screen").</summary>
    public Transform Part(string name) => root != null ? FindDeep(root, name) : null;

    // --- building blocks ---

    GameObject Block(string name, Rect r, float zFrom, float height, Material m, bool behind = false, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());//right away: Button() adds its own collider afterwards
        go.transform.SetParent(parent != null ? parent : root, false);
        float z = behind ? zFrom + 0.5f * height : -(zFrom + 0.5f * height);
        go.transform.localPosition = new Vector3(r.center.x, r.center.y, z);
        go.transform.localScale = new Vector3(r.width, r.height, height);
        if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    /// <summary>A flat part facing the viewer (unlit), at depth z (negative = in front of the face).</summary>
    Renderer Flat(string name, Rect r, float z, Texture tex, Color color, Transform parent = null, bool keepMaterial = true)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent != null ? parent : root, false);
        go.transform.localPosition = new Vector3(r.center.x, r.center.y, z);
        go.transform.localScale = new Vector3(r.width, r.height, 1f);
        go.GetComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (keepMaterial)
        {
            mr.sharedMaterial = unlitMaterial;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetColor(ColorId, color);
            if (tex != null) block.SetTexture(MainTexId, tex);
            mr.SetPropertyBlock(block);
        }
        return mr;
    }

    /// <summary>Four bars around (outside = true) or along the inside of a rectangle.</summary>
    void Frame(string name, Rect r, float width, float zFrom, float height, Material m, Transform parent = null, bool outside = true)
    {
        Rect o = outside ? Grow(r, width) : r;
        Block(name + "Top", Rect.MinMaxRect(o.xMin, o.yMax - width, o.xMax, o.yMax), zFrom, height, m, parent: parent);
        Block(name + "Bottom", Rect.MinMaxRect(o.xMin, o.yMin, o.xMax, o.yMin + width), zFrom, height, m, parent: parent);
        Block(name + "Left", Rect.MinMaxRect(o.xMin, o.yMin + width, o.xMin + width, o.yMax - width), zFrom, height, m, parent: parent);
        Block(name + "Right", Rect.MinMaxRect(o.xMax - width, o.yMin + width, o.xMax, o.yMax - width), zFrom, height, m, parent: parent);
    }

    /// <summary>A pressable block (or the given prefab, fitted into the same box) with a label on top.</summary>
    PanelButton Button(string name, Rect r, float zFrom, float height, Material m, GameObject prefab, string label)
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, root, false);
            go.name = name;
            FitInto(go, r, zFrom, height);
        }
        else
        {
            go = Block(name, r, zFrom, height, m);
            Renderer text = Label(label, r.center, 0.45f * Mathf.Min(r.width, r.height) / Mathf.Max(1, label.Length) * (label.Length > 1 ? 1.6f : 1f),
                                  -(zFrom + height) - 0.0005f);
            if (text != null) text.transform.SetParent(go.transform, true);//moves along when pressed
        }
        if (go.GetComponent<Collider>() == null)
        {
            var box = go.AddComponent<BoxCollider>();
            if (prefab != null) FitColliderToRenderers(go, box);
        }
        return go.AddComponent<PanelButton>();
    }

    PanelButton Arrow(string name, Vector2 center, float size, float angle)
    {
        Rect r = Centered(center, 0.9f * size);
        PanelButton b = Button(name, r, 0f, layout.buttonHeight, controlMaterial, arrowButtonPrefab, "");
        if (arrowButtonPrefab == null)
        {
            Renderer glyph = Flat(name + "Glyph", Centered(center, 0.5f * size), -layout.buttonHeight - 0.0005f, arrowTexture, labelColor);
            glyph.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            glyph.transform.SetParent(b.transform, true);//moves along when pressed
        }
        else b.transform.localRotation *= Quaternion.Euler(0f, 0f, angle);
        return b;
    }

    /// <summary>Text label (pixel font) centered at c; `charSize` = height of one character in meters.</summary>
    Renderer Label(string text, Vector2 c, float charSize, float z)
    {
        if (string.IsNullOrEmpty(text)) return null;
        Texture2D tex = PixelFont.Label(text);
        owned.Add(tex);
        float pxSize = charSize / PixelFont.GlyphHeight;
        return Flat("Label " + text, Centered(c, new Vector2(tex.width * pxSize, tex.height * pxSize)), z, tex, labelColor);
    }

    void FitInto(GameObject go, Rect r, float zFrom, float height)
    {
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        Bounds b = LocalBounds(go);
        Vector3 size = new Vector3(r.width, r.height, height);
        Vector3 scale = new Vector3(size.x / Mathf.Max(1e-5f, b.size.x), size.y / Mathf.Max(1e-5f, b.size.y), size.z / Mathf.Max(1e-5f, b.size.z));
        go.transform.localScale = scale;
        go.transform.localPosition = new Vector3(r.center.x, r.center.y, -(zFrom + 0.5f * height)) - Vector3.Scale(b.center, scale);
    }

    Bounds LocalBounds(GameObject go)
    {
        bool any = false;
        var result = new Bounds();
        foreach (Renderer rr in go.GetComponentsInChildren<Renderer>())
        {
            Bounds wb = rr.bounds;
            Vector3 lo = root.InverseTransformPoint(wb.min), hi = root.InverseTransformPoint(wb.max);
            var lb = new Bounds((lo + hi) * 0.5f, Vector3.zero);
            lb.Encapsulate(lo); lb.Encapsulate(hi);
            if (!any) { result = lb; any = true; } else result.Encapsulate(lb);
        }
        return any ? result : new Bounds(Vector3.zero, Vector3.one);
    }

    static void FitColliderToRenderers(GameObject go, BoxCollider box)
    {
        var b = new Bounds();
        bool any = false;
        foreach (Renderer rr in go.GetComponentsInChildren<Renderer>())
        {
            Bounds wb = rr.bounds;
            Vector3 lo = go.transform.InverseTransformPoint(wb.min), hi = go.transform.InverseTransformPoint(wb.max);
            var lb = new Bounds((lo + hi) * 0.5f, Vector3.zero);
            lb.Encapsulate(lo); lb.Encapsulate(hi);
            if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
        }
        if (any) { box.center = b.center; box.size = b.size; }
    }

    static Rect Centered(Vector2 c, float size) => Centered(c, new Vector2(size, size));
    static Rect Centered(Vector2 c, Vector2 size) => new Rect(c - 0.5f * size, size);
    static Rect Grow(Rect r, float d) => Rect.MinMaxRect(r.xMin - d, r.yMin - d, r.xMax + d, r.yMax + d);
    static Rect Shrink(Rect r, float d) => Grow(r, -d);

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { Transform f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    void EnsureResources()
    {
        if (quad == null)
        {
            //a 1x1 quad facing -z (toward the console camera); readable, so Console3D can read its normal
            quad = new Mesh { name = "PanelQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.RecalculateBounds();
        }
        if (ledTexture == null) ledTexture = PixelFont.Circle(16);
        if (arrowTexture == null) arrowTexture = PixelFont.Triangle(32);
        if (unlitMaterial == null)
        {
            var sh = Shader.Find("BotanicLab/PanelUnlit");
            if (sh != null) { unlitMaterial = new Material(sh) { name = "PanelUnlit (runtime)" }; owned.Add(unlitMaterial); }
        }
    }

    void Clear()
    {
        //destroyed only at the end of the frame: hide and rename it right away, so nothing finds the old parts
        if (root != null) { root.gameObject.SetActive(false); root.name = "Panel (old)"; Destroy(root.gameObject); }
        root = null;
        foreach (UnityEngine.Object o in owned) if (o != null) Destroy(o);
        owned.Clear();
    }

    void OnDestroy()
    {
        Clear();
        if (quad != null) Destroy(quad);
        if (ledTexture != null) Destroy(ledTexture);
        if (arrowTexture != null) Destroy(arrowTexture);
    }
}
