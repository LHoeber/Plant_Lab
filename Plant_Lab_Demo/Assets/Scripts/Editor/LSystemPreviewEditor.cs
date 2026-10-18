using UnityEditor;
using UnityEngine;

/// <summary>Inspector of LSystemPreview: the usual fields, plus the result (module counts) or the error in the rules.</summary>
[CustomEditor(typeof(LSystemPreview))]
public class LSystemPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var preview = (LSystemPreview)target;
        preview.Refresh();
        if (preview.error != null) EditorGUILayout.HelpBox(preview.error, MessageType.Error);
        else if (preview.summary != null) EditorGUILayout.HelpBox(preview.summary, MessageType.Info);

        //with plant settings assigned, the preview's own L-system fields aren't used: hide them
        bool fromSettings = preview.fromSettings != null;
        if (fromSettings)
            EditorGUILayout.HelpBox($"Showing the L-system of '{preview.fromSettings.name}'. Edit its rules and constants there.",
                                    MessageType.None);
        serializedObject.Update();
        SerializedProperty prop = serializedObject.GetIterator();
        for (bool enter = true; prop.NextVisible(enter); enter = false)
        {
            if (prop.name == "m_Script") continue;
            if (fromSettings && (prop.name == "axiom" || prop.name == "rules" || prop.name == "constants" ||
                                 prop.name == "angle" || prop.name == "scale")) continue;
            EditorGUILayout.PropertyField(prop, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>An L-system constant in one line: name, slider between min and max, min, max (preview and plant settings).</summary>
[CustomPropertyDrawer(typeof(LConstant))]
public class LSystemConstantDrawer : PropertyDrawer
{
    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        var name = property.FindPropertyRelative("name");
        var value = property.FindPropertyRelative("value");
        var min = property.FindPropertyRelative("min");
        var max = property.FindPropertyRelative("max");
        float w = rect.width, x = rect.x;
        const float gap = 4f, small = 44f;
        float nameW = Mathf.Min(110f, w * 0.3f);
        float sliderW = w - nameW - 2 * small - 3 * gap;
        Field(new Rect(x, rect.y, nameW, rect.height), name, "Name used in the rules");
        x += nameW + gap;
        var sliderRect = new Rect(x, rect.y, sliderW, rect.height);
        value.floatValue = EditorGUI.Slider(sliderRect, value.floatValue, min.floatValue, max.floatValue);
        GUI.Label(sliderRect, new GUIContent("", "Value"));//tooltip only
        x += sliderW + gap;
        Field(new Rect(x, rect.y, small, rect.height), min, "Slider minimum (Randomize stays above it)");
        x += small + gap;
        Field(new Rect(x, rect.y, small, rect.height), max, "Slider maximum (Randomize stays below it)");
    }

    static void Field(Rect r, SerializedProperty p, string tooltip)
    {
        EditorGUI.PropertyField(r, p, GUIContent.none);
        GUI.Label(r, new GUIContent("", tooltip));//shows the tooltip when hovering over the field
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;
}
