using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Menu "Botanic Lab > Build Control Panel In Scene": creates the two-camera console setup in the open scene.
///   ObservationCamera (everything except the UI layer) -> RenderTexture = the observation
///   ConsoleCamera (UI layer only, fixed, orthographic) -> what the participant sees: screen + buttons
/// The previous Main Camera is switched off (not deleted). Everything can be undone with Edit > Undo.
/// </summary>
public static class LabSetup
{
    const string ArrowPath = "Assets/Scripts/Lab/Art/arrow.png";

    static readonly Color PanelColor = new Color(0.13f, 0.14f, 0.15f);
    static readonly Color FrameColor = new Color(0.05f, 0.05f, 0.055f);
    static readonly Color ArrowButtonColor = new Color(0.32f, 0.34f, 0.36f);
    static readonly Color InterventionColor = new Color(0.55f, 0.6f, 0.65f);//identical for all, no icon, no text

    static readonly string[] Parts = { "LabConsole", "ConsoleCanvas", "ConsoleCamera", "ObservationCamera" };

    [MenuItem("Botanic Lab/Rebuild Control Panel In Scene")]
    static void Rebuild()
    {
        if (!EditorUtility.DisplayDialog("Botanic Lab", "Rebuilding deletes the current control panel and builds the " +
                                         "default one. All changes made to it by hand (positions, sizes, colors) are lost.",
                                         "Rebuild", "Cancel")) return;
        //remove the existing panel (undoable), then build a fresh one
        foreach (string name in Parts)
        {
            var go = GameObject.Find(name);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }
        Build();
    }

    [MenuItem("Botanic Lab/Build Control Panel In Scene")]
    static void Build()
    {
        if (GameObject.Find("LabConsole") != null)
        {
            EditorUtility.DisplayDialog("Botanic Lab", "This scene already has a control panel. " +
                                        "Use Botanic Lab > Rebuild Control Panel In Scene to replace it.", "OK");
            return;
        }
        int uiLayer = LayerMask.NameToLayer("UI");
        Undo.SetCurrentGroupName("Build Control Panel");
        int undoGroup = Undo.GetCurrentGroup();
        var plant = Object.FindAnyObjectByType<PlantGrowth>();

        // --- observation camera: sees the terrarium, never the console ---
        var obsGo = Create("ObservationCamera", null);
        var obsCam = obsGo.AddComponent<Camera>();
        obsCam.cullingMask = ~(1 << uiLayer);
        obsCam.fieldOfView = 40f;
        obsCam.nearClipPlane = 0.05f;
        var observation = obsGo.AddComponent<ObservationCamera>();
        observation.target = plant != null ? plant.transform : null;

        //the old main camera would also draw to the display: switch it off (kept, in case it's needed again)
        foreach (var cam in Object.FindObjectsByType<Camera>())
            if (cam != obsCam && cam.gameObject.activeSelf)
            {
                Undo.RecordObject(cam.gameObject, "Disable camera");
                cam.gameObject.SetActive(false);
            }

        // --- console camera: only the UI layer, fixed and straight on the screen ---
        var conGo = Create("ConsoleCamera", null);
        conGo.tag = "MainCamera";
        conGo.transform.position = new Vector3(0f, -100f, 0f);//far away from the terrarium (it only renders UI anyway)
        var conCam = conGo.AddComponent<Camera>();
        conCam.clearFlags = CameraClearFlags.SolidColor;
        conCam.backgroundColor = Color.black;
        conCam.cullingMask = 1 << uiLayer;
        conCam.orthographic = true;
        conCam.depth = 1;
        conGo.AddComponent<AudioListener>();

        // --- canvas ---
        var canvasGo = Create("ConsoleCanvas", null, uiLayer, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = conCam;
        canvas.planeDistance = 1f;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = UIImage("Panel", canvasGo.transform, uiLayer, PanelColor, null);
        Stretch(panel.rectTransform);
        panel.raycastTarget = false;

        // screen: frame, observation image, static overlay
        var frame = UIImage("ScreenFrame", panel.transform, uiLayer, FrameColor, Rounded());
        Place(frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(620f, 0f), new Vector2(1120f, 760f));//screen 3:2 plus a 20 px border
        frame.raycastTarget = false;
        var screenGo = Create("Screen", frame.transform, uiLayer, typeof(RectTransform), typeof(RawImage));
        var screen = screenGo.GetComponent<RawImage>();
        Place(screen.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 720f));//same 3:2 aspect as the observation (768x512)
        screen.raycastTarget = false;
        var overlayGo = Create("ScreenOverlay", screenGo.transform, uiLayer, typeof(RectTransform), typeof(RawImage), typeof(StaticScreenOverlay));
        Stretch((RectTransform)overlayGo.transform);

        // controls on the right
        var controls = Create("Controls", panel.transform, uiLayer, typeof(RectTransform));
        Place((RectTransform)controls.transform, new Vector2(1f, 0.5f), new Vector2(-430f, 0f), new Vector2(600f, 900f));

        // --- the logic object ---
        var lab = Create("LabConsole", null);
        var clock = lab.AddComponent<TickClock>();
        var interventions = lab.AddComponent<Interventions>();
        var cp = lab.AddComponent<ControlPanel>();
        cp.clock = clock;
        cp.interventions = interventions;
        cp.observationCamera = observation;
        cp.screen = screen;

        // camera arrows: conventional shapes in a cross
        Sprite arrow = ArrowSprite();
        ArrowButton("Up", controls.transform, uiLayer, cp, LabActions.CameraUp, new Vector2(0f, 330f), 0f, arrow);
        ArrowButton("Down", controls.transform, uiLayer, cp, LabActions.CameraDown, new Vector2(0f, 130f), 180f, arrow);
        ArrowButton("Left", controls.transform, uiLayer, cp, LabActions.CameraLeft, new Vector2(-170f, 230f), 90f, arrow);
        ArrowButton("Right", controls.transform, uiLayer, cp, LabActions.CameraRight, new Vector2(170f, 230f), -90f, arrow);

        // intervention buttons A, B, ... in fixed positions (rows of three); their channels are assigned randomly at start
        for (int i = 0; i < LabActions.InterventionCount; i++)
        {
            var b = UIImage($"Button{LabActions.ButtonLetter(i)}", controls.transform, uiLayer, InterventionColor, Rounded());
            Place(b.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-40f + 170f * (i % 3), -120f - 190f * (i / 3)), new Vector2(150f, 150f));
            var pb = b.gameObject.AddComponent<PanelButton>();
            pb.action = LabActions.ButtonA + i;
            pb.panel = cp;
        }

        // --- input: new Input System UI module (mouse and touch) ---
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            Create("EventSystem", null, -1, typeof(EventSystem), typeof(InputSystemUIInputModule));

        Undo.CollapseUndoOperations(undoGroup);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lab.scene);
        Selection.activeGameObject = lab;
        Debug.Log("Botanic Lab: control panel built. Press Play; the observation camera orbits " +
                  (plant != null ? $"'{plant.name}'." : "the origin (no PlantGrowth found: set its target)."));
    }

    // --- helpers ---

    static GameObject Create(string name, Transform parent, int layer = -1, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        if (parent != null) go.transform.SetParent(parent, false);
        if (layer >= 0) go.layer = layer;
        return go;
    }

    static Image UIImage(string name, Transform parent, int layer, Color color, Sprite sprite)
    {
        var go = Create(name, parent, layer, typeof(RectTransform), typeof(Image));
        var img = go.GetComponent<Image>();
        img.color = color;
        if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
        return img;
    }

    static void ArrowButton(string name, Transform parent, int layer, ControlPanel cp, int action, Vector2 pos, float rotation, Sprite arrow)
    {
        var b = UIImage(name, parent, layer, ArrowButtonColor, Rounded());
        Place(b.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(150f, 150f));
        var pb = b.gameObject.AddComponent<PanelButton>();
        pb.action = action;
        pb.panel = cp;
        var glyph = UIImage("Arrow", b.transform, layer, new Color(0.85f, 0.87f, 0.9f), arrow);
        glyph.type = Image.Type.Simple;
        glyph.raycastTarget = false;
        Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
        glyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Sprite Rounded() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    /// <summary>A simple upward-pointing triangle sprite, created once as a small PNG asset.</summary>
    static Sprite ArrowSprite()
    {
        if (File.Exists(ArrowPath))
        {
            EnsureSingleSprite();
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowPath);
        }
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                //triangle: tip at the top center, base at the bottom; soft edge for anti-aliasing
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float halfWidth = 0.45f * (1f - v);
                float inside = Mathf.Min(halfWidth - Mathf.Abs(u - 0.5f), v - 0.08f, 0.92f - v);
                float a = Mathf.Clamp01(inside * n * 0.7f + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(ArrowPath));
        File.WriteAllBytes(ArrowPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ArrowPath);
        EnsureSingleSprite();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowPath);
    }

    /// <summary>
    /// Import the arrow image as ONE sprite. (Unity may default new sprite textures to "Multiple", a sprite sheet;
    /// then there's no single sprite to load, and the arrow images show up as plain white squares.)
    /// </summary>
    static void EnsureSingleSprite()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(ArrowPath);
        if (importer == null) return;
        if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }
}
