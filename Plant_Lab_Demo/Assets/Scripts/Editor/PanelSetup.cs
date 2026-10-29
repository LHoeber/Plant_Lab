using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu "Botanic Lab > Build Trial Panel": sets up the trial workflow in the open scene.
///   - "TrialPanel" (far below the terrarium, on the "Console" layer): PanelBuilder (builds the panel at Play start from
///     a PanelConfig and a PanelLayout asset), Console3D (screen, lighting, console camera framing), TickClock,
///     TrialController (the workflow)
///   - ObservationCamera (terrarium -> camera image) and ConsoleCamera (sees only the panel), created if missing
///   - ConsoleLight: lights only the panel (URP rendering layer); the scene's lights leave the panel out
///   - removes the previous consoles (2D canvas, LabConsole, imported 3D console model)
/// Materials and the default config/layout assets are created on first use. Everything can be undone (Edit > Undo).
/// </summary>
public static class PanelSetup
{
    const string LayerName = "Console";
    const int RenderingLayer = 1;//bit 0 = Default (everything else in the scene)
    const string MaterialFolder = "Assets/Materials/Panel";
    const string ScreenMaterialPath = "Assets/Materials/ConsoleScreen.mat";
    const string ConfigPath = "Assets/Settings/Panel/PanelConfig_Default.asset";
    const string LayoutPath = "Assets/Settings/Panel/PanelLayout_Default.asset";

    [MenuItem("Botanic Lab/Build Trial Panel")]
    static void Build()
    {
        var existing = Object.FindAnyObjectByType<TrialController>();
        if (existing != null && !EditorUtility.DisplayDialog("Botanic Lab",
                "This scene already has a trial panel. Replace it with a fresh one?", "Replace", "Cancel")) return;
        int layer = EnsureLayer(LayerName);
        if (layer < 0) { EditorUtility.DisplayDialog("Botanic Lab", "No free layer for 'Console'.", "OK"); return; }

        Undo.SetCurrentGroupName("Build Trial Panel");
        int undoGroup = Undo.GetCurrentGroup();

        // --- old consoles out ---
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        //(also inactive ones: GameObject.Find would skip those)
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.parent == null && (t.name == "LabConsole" || t.name == "ConsoleCanvas"))
                Undo.DestroyObjectImmediate(t.gameObject);
        foreach (Console3D c in Object.FindObjectsByType<Console3D>(FindObjectsInactive.Include))
            if (c != null && c.GetComponent<PanelBuilder>() == null) Undo.DestroyObjectImmediate(c.gameObject);

        // --- cameras ---
        var plant = Object.FindAnyObjectByType<PlantGrowth>();
        var observation = Object.FindAnyObjectByType<ObservationCamera>();
        if (observation == null)
        {
            var obsGo = new GameObject("ObservationCamera");
            Undo.RegisterCreatedObjectUndo(obsGo, "Create ObservationCamera");
            var obsCam = obsGo.AddComponent<Camera>();
            obsCam.fieldOfView = 40f;
            obsCam.nearClipPlane = 0.05f;
            observation = obsGo.AddComponent<ObservationCamera>();
            observation.target = plant != null ? plant.transform : null;
        }
        var obsCamera = observation.GetComponent<Camera>();
        Undo.RecordObject(obsCamera, "Observation camera");
        obsCamera.cullingMask &= ~(1 << layer);

        var conGo = GameObject.Find("ConsoleCamera");
        if (conGo == null)
        {
            conGo = new GameObject("ConsoleCamera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            Undo.RegisterCreatedObjectUndo(conGo, "Create ConsoleCamera");
        }
        var conCam = conGo.GetComponent<Camera>();
        Undo.RecordObject(conCam, "Console camera");
        conCam.clearFlags = CameraClearFlags.SolidColor;
        conCam.backgroundColor = Color.black;
        conCam.cullingMask = 1 << layer;
        conCam.orthographic = false;
        conCam.depth = 1;
        var raycaster = conGo.GetComponent<PhysicsRaycaster>() ?? Undo.AddComponent<PhysicsRaycaster>(conGo);
        raycaster.eventMask = 1 << layer;
        //other active cameras would also draw to the display
        foreach (Camera cam in Object.FindObjectsByType<Camera>())
            if (cam != conCam && cam != obsCamera && cam.gameObject.activeSelf)
            {
                Undo.RecordObject(cam.gameObject, "Disable camera");
                cam.gameObject.SetActive(false);
            }
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        // --- the panel ---
        var panelGo = new GameObject("TrialPanel") { layer = layer };
        Undo.RegisterCreatedObjectUndo(panelGo, "Create TrialPanel");
        panelGo.transform.position = new Vector3(0f, -100f, 0f);//far away from the terrarium (only the console camera sees it)
        var builder = panelGo.AddComponent<PanelBuilder>();
        builder.config = LoadOrCreate<PanelConfig>(ConfigPath);
        builder.layout = LoadOrCreate<PanelLayout>(LayoutPath);
        builder.casingMaterial = LitMaterial("PanelCasing", new Color(0.32f, 0.33f, 0.34f), false);
        builder.effectButtonMaterial = LitMaterial("PanelEffectButton", new Color(0.6f, 0.07f, 0.05f), true);
        builder.controlMaterial = LitMaterial("PanelControl", new Color(0.55f, 0.56f, 0.58f), true);
        builder.darkMaterial = LitMaterial("PanelDark", new Color(0.06f, 0.06f, 0.065f), false);
        builder.unlitMaterial = UnlitMaterial("PanelUnlit");
        var console = panelGo.AddComponent<Console3D>();
        console.observationCamera = observation;
        console.consoleCamera = conCam;
        console.screenMaterial = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialPath) ?? CreateScreenMaterial();
        console.renderingLayer = RenderingLayer;
        var clock = panelGo.AddComponent<TickClock>();
        var trials = panelGo.AddComponent<TrialController>();
        trials.config = builder.config;
        trials.panel = builder;
        trials.console = console;
        trials.observationCamera = observation;
        trials.clock = clock;

        // --- light: only the panel's rendering layer; all other lights leave that layer out ---
        uint consoleBit = 1u << RenderingLayer;
        foreach (Light l in Object.FindObjectsByType<Light>())
        {
            if (l.gameObject.name == "ConsoleLight") continue;
            var data = l.GetUniversalAdditionalLightData();
            Undo.RecordObject(data, "Light layers");
            data.renderingLayers = (uint)data.renderingLayers & ~consoleBit;
            data.shadowRenderingLayers = (uint)data.shadowRenderingLayers & ~consoleBit;
        }
        var lightGo = GameObject.Find("ConsoleLight");
        if (lightGo == null)
        {
            lightGo = new GameObject("ConsoleLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "Create ConsoleLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
        }
        var lightData = lightGo.GetComponent<Light>().GetUniversalAdditionalLightData();
        Undo.RecordObject(lightData, "Console light layers");
        lightData.renderingLayers = consoleBit;
        lightData.shadowRenderingLayers = consoleBit;
        //from above, slightly left, in front of the panel (it faces -z): buttons get a lit top edge and a short shadow below
        Undo.RecordObject(lightGo.transform, "Console light direction");
        lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(0.4f, -0.8f, 1f), Vector3.up);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(panelGo.scene);
        Selection.activeGameObject = panelGo;
        Debug.Log("Botanic Lab: trial panel set up. Press Play. Buttons and trials: " + ConfigPath + ", positions: " + LayoutPath);
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>A URP Lit material (created once as an asset), optionally with emission switched on for glowing.</summary>
    static Material LitMaterial(string name, Color color, bool emission)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        Directory.CreateDirectory(MaterialFolder);
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.35f);
        if (emission)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", Color.black);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material UnlitMaterial(string name)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        Directory.CreateDirectory(MaterialFolder);
        mat = new Material(Shader.Find("BotanicLab/PanelUnlit"));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material CreateScreenMaterial()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ScreenMaterialPath));
        var mat = new Material(Shader.Find("BotanicLab/ConsoleScreen"));
        AssetDatabase.CreateAsset(mat, ScreenMaterialPath);
        return mat;
    }

    /// <summary>Index of the named layer; created in the first free user layer if missing (-1 if none is free).</summary>
    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++)
        {
            var entry = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(entry.stringValue)) continue;
            entry.stringValue = name;
            tagManager.ApplyModifiedProperties();
            return i;
        }
        return -1;
    }
}
