using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for PlantSettings assets: the normal parameter list, plus a box at the bottom to rescale the whole
/// plant by any factor (lengths x factor, rates per length / factor, wobble / sqrt(factor); see ParameterUnits).
/// Editor-only: scripts in an "Editor" folder are never part of a built game.
/// </summary>
[CustomEditor(typeof(PlantSettings))]
public class PlantSettingsEditor : Editor
{
    static float factor = 0.5f;//remembered while the editor is open

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Rescale plant size", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Makes the whole plant bigger or smaller with the same shape and behavior: " +
                                "lengths and sizes are multiplied by the factor, rates per length divided by it. " +
                                "Undo works (Edit > Undo).", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            factor = EditorGUILayout.FloatField("Factor", factor);
            GUI.enabled = factor > 0f && !Mathf.Approximately(factor, 1f);
            if (GUILayout.Button("Rescale", GUILayout.Width(80))) Rescale(factor);
            GUI.enabled = true;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("x 0.5")) Rescale(0.5f);
            if (GUILayout.Button("x 2")) Rescale(2f);
        }
    }

    void Rescale(float f)
    {
        foreach (Object t in targets)//all selected assets
        {
            var settings = (PlantSettings)t;
            Undo.RecordObject(settings, "Rescale Plant Size");
            settings.RescaleSize(f);
            EditorUtility.SetDirty(settings);
        }
    }
}
