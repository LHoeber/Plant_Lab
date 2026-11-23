using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Switches a lamp on and off: swaps materials (e.g. Led_on -> Led_off, glowing inner box -> plain glass) and turns
/// its RectLampLights (spot light + visible beam) on/off. Put it on the lamp's root object.
/// In the Inspector: the "On" box. From scripts: lamp.IsOn = false, lamp.Toggle(), event Switched.
///
/// Which parts switch: every material slot of the renderers below this object that uses one of the "on" materials
/// in Materials. They are found while the lamp is on (each time it's switched on, so newly added parts are picked up)
/// and remembered, so switching back on restores exactly those slots (other parts that happen to use an "off"
/// material, e.g. other glass, are never touched).
/// </summary>
[ExecuteAlways]
public class GrowLamp : MonoBehaviour
{
    [Tooltip("Lamp on or off.")]
    [SerializeField] bool on = true;

    [Serializable]
    public class MaterialPair
    {
        [Tooltip("Material while the lamp is on (as the parts are set up in the prefab).")]
        public Material on;
        [Tooltip("Material it is replaced with while the lamp is off.")]
        public Material off;
    }
    [Tooltip("Material swaps: e.g. Led_on / Led_off, lamp_inside_on / your glass material.")]
    public MaterialPair[] materials = new MaterialPair[0];

    [Serializable]
    class Slot { public Renderer renderer; public int index; public int pair; }
    [SerializeField, HideInInspector] List<Slot> slots = new List<Slot>();

    /// <summary>Called after every switch (true = on).</summary>
    public event Action<bool> Switched;

    public bool IsOn
    {
        get => on;
        set { if (value == on && applied) return; on = value; Apply(); }
    }

    public void Toggle() => IsOn = !on;

    bool applied;      //Apply has run since enable / the last Inspector change
    bool appliedState; //what was applied

    void OnEnable() => applied = false;
    void OnValidate() => applied = false;//applied in the next Update (changing components inside OnValidate isn't allowed)

    void Update()
    {
        if (!applied || appliedState != on) Apply();
    }

    void Apply()
    {
        bool changed = applied && appliedState != on;
        SetSlots(on);
        if (on) Collect();//all switchable slots show their "on" material now: find them again (new parts included)
        foreach (RectLampLight l in GetComponentsInChildren<RectLampLight>(true))
        {
            l.enabled = on;
            Light light = l.GetComponent<Light>();
            if (light != null) light.enabled = on;
        }
        applied = true;
        appliedState = on;
        if (changed) Switched?.Invoke(on);
    }

    void SetSlots(bool toOn)
    {
        foreach (Slot s in slots)
        {
            if (s.renderer == null || s.pair >= materials.Length || materials[s.pair] == null) continue;
            Material m = toOn ? materials[s.pair].on : materials[s.pair].off;
            if (m == null) continue;
            Material[] mats = s.renderer.sharedMaterials;
            if (s.index >= mats.Length || mats[s.index] == m) continue;
            mats[s.index] = m;
            s.renderer.sharedMaterials = mats;
        }
    }

    void Collect()
    {
        slots.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                for (int p = 0; p < materials.Length; p++)
                    if (materials[p] != null && materials[p].on != null && mats[i] == materials[p].on)
                    {
                        slots.Add(new Slot { renderer = r, index = i, pair = p });
                        break;
                    }
        }
    }
}
