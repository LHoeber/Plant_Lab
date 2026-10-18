using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu "Botanic Lab > Use 3D Console": replaces the 2D console canvas with the 3D console model in the open scene.
/// Needs the existing setup (LabConsole, ObservationCamera, ConsoleCamera from "Build Control Panel In Scene").
///   - places the model (Assets/Models/control_panel.fbx) as "Console3D" on its own "Console" layer
///   - console camera: renders only that layer, clicks on 3D buttons via a Physics Raycaster
///   - observation camera: never sees that layer
///   - "ConsoleLight": lights only the console's rendering layer; the scene's lights are excluded from it
///   - the 2D canvas is switched off, not deleted (switch it back on and the console camera's culling mask to go back)
/// The model is only placed: buttons, colliders and the screen are set up at Play start by Console3D, from the
/// part names, so re-exporting the model from Blender keeps working as long as the names stay the same.
/// Everything can be undone with Edit > Undo.
/// </summary>
public static class Console3DSetup
{
    const string ModelPath = "Assets/Models/control_panel.fbx";
    const string ScreenMaterialPath = "Assets/Materials/ConsoleScreen.mat";
    const string LayerName = "Console";
    const int RenderingLayer = 1;//bit 0 = Default (everything else in the scene)

    [MenuItem("Botanic Lab/Use 3D Console")]
    static void Use3DConsole()
    {
        //Console3D reads the Screen mesh's normal at runtime: the model must be readable (Read/Write)
        if (AssetImporter.GetAtPath(ModelPath) is ModelImporter importer && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var cp = Object.FindAnyObjectByType<ControlPanel>();
        var observation = Object.FindAnyObjectByType<ObservationCamera>();
        var conGo = GameObject.Find("ConsoleCamera");
        if (model == null || cp == null || observation == null || conGo == null || conGo.GetComponent<Camera>() == null)
        {
            EditorUtility.DisplayDialog("Botanic Lab", model == null
                ? $"No console model at {ModelPath}."
                : "This scene has no complete control panel setup (LabConsole, ObservationCamera, ConsoleCamera). " +
                  "Run Botanic Lab > Build Control Panel In Scene first.", "OK");
            return;
        }
        var existing = Object.FindAnyObjectByType<Console3D>();
        if (existing != null && !EditorUtility.DisplayDialog("Botanic Lab",
                "This scene already has a 3D console. Replace it with a fresh one?", "Replace", "Cancel")) return;

        int layer = EnsureLayer(LayerName);
        if (layer < 0)
        {
            EditorUtility.DisplayDialog("Botanic Lab", "No free layer for 'Console' (Project Settings > Tags and Layers).", "OK");
            return;
        }
        Undo.SetCurrentGroupName("Use 3D Console");
        int undoGroup = Undo.GetCurrentGroup();
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        var oldLight = GameObject.Find("ConsoleLight");
        if (oldLight != null) Undo.DestroyObjectImmediate(oldLight);

        // --- the model, far away from the terrarium (only the console camera sees its layer anyway) ---
        var console = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(console, "Create Console3D");
        console.name = "Console3D";
        console.transform.position = new Vector3(0f, -100f, 0f);
        foreach (Transform t in console.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        var c3d = console.AddComponent<Console3D>();
        c3d.panel = cp;
        c3d.observationCamera = observation;
        c3d.consoleCamera = conGo.GetComponent<Camera>();
        c3d.screenMaterial = ScreenMaterial();
        c3d.renderingLayer = RenderingLayer;

        // --- cameras ---
        var conCam = conGo.GetComponent<Camera>();
        Undo.RecordObject(conCam, "Console camera");
        conCam.cullingMask = 1 << layer;
        conCam.orthographic = false;
        conCam.clearFlags = CameraClearFlags.SolidColor;
        conCam.backgroundColor = Color.black;
        var raycaster = conGo.GetComponent<PhysicsRaycaster>() ?? Undo.AddComponent<PhysicsRaycaster>(conGo);
        raycaster.eventMask = 1 << layer;
        var obsCam = observation.GetComponent<Camera>();
        Undo.RecordObject(obsCam, "Observation camera");
        obsCam.cullingMask &= ~(1 << layer);

        // --- light: only the console's rendering layer; all other lights leave that layer out ---
        uint consoleBit = 1u << RenderingLayer;
        foreach (var l in Object.FindObjectsByType<Light>())
        {
            var data = l.GetUniversalAdditionalLightData();
            Undo.RecordObject(data, "Light layers");
            data.renderingLayers = (uint)data.renderingLayers & ~consoleBit;
            data.shadowRenderingLayers = (uint)data.shadowRenderingLayers & ~consoleBit;
        }
        var lightGo = new GameObject("ConsoleLight");
        Undo.RegisterCreatedObjectUndo(lightGo, "Create ConsoleLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.Soft;
        var lightData = light.GetUniversalAdditionalLightData();
        lightData.renderingLayers = consoleBit;
        lightData.shadowRenderingLayers = consoleBit;
        //from above, slightly left and in front: buttons get a lit top edge and a short shadow below
        Vector3 front = ConsoleFront(console);
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, front).normalized;
        Vector3 right = Vector3.Cross(up, front);
        lightGo.transform.rotation = Quaternion.LookRotation(-front - 0.8f * up + 0.4f * right, up);

        // --- the 2D canvas: off, kept ---
        var canvas = GameObject.Find("ConsoleCanvas");
        if (canvas != null) { Undo.RecordObject(canvas, "Disable 2D console"); canvas.SetActive(false); }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(console.scene);
        Selection.activeGameObject = console;
        Debug.Log($"Botanic Lab: 3D console placed (layer '{LayerName}', rendering layer {RenderingLayer}). Press Play.");
    }

    /// <summary>Front direction from the Screen mesh's normal, as Console3D computes it at runtime.</summary>
    static Vector3 ConsoleFront(GameObject console)
    {
        foreach (var mf in console.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name == "Screen" && mf.sharedMesh != null && mf.sharedMesh.normals.Length > 0)
                return mf.transform.TransformDirection(mf.sharedMesh.normals[0]).normalized;
        return -console.transform.forward;
    }

    static Material ScreenMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialPath);
        if (mat != null) return mat;
        var shader = Shader.Find("BotanicLab/ConsoleScreen");
        if (shader == null) { Debug.LogError("Botanic Lab: shader BotanicLab/ConsoleScreen not found."); return null; }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScreenMaterialPath));
        mat = new Material(shader);
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
