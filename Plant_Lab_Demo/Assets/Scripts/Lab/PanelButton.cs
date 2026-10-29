using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One pressable part of the control panel (needs a collider; the console camera's Physics Raycaster finds it).
/// Reacts on press down (immediately, also on touch) by calling onPress. If that accepts the press, the part
/// gives a brief press feedback (pushed in by pressOffset and darker, shorter than one tick). Independently it can
/// glow (emission), e.g. while an effect button is switched on or Start can be pressed.
/// </summary>
public class PanelButton : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Identifies the press in logs (camera arrows: the LabActions camera action).")]
    public int action = -1;
    [Tooltip("Seconds the press feedback lasts.")]
    public float feedbackTime = 0.12f;
    [Tooltip("How far the part moves while pressed, in world space (direction included).")]
    public Vector3 pressOffset;
    [Tooltip("Color factor while pressed.")]
    [Range(0f, 1f)] public float pressDarkening = 0.75f;

    /// <summary>Called on press; return true if the press was accepted (then the button shows its feedback).</summary>
    public Func<bool> onPress;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"),
                        EmissionId = Shader.PropertyToID("_EmissionColor");

    Renderer rend;
    MaterialPropertyBlock block;
    Vector3 basePosition;
    Coroutine running;
    bool pressed;
    Color glow = Color.black;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        basePosition = transform.localPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (onPress != null && onPress()) Feedback();
    }

    public void Feedback()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Flash());
    }

    /// <summary>Lets the part glow in this color (black = off). Kept until changed.</summary>
    public void SetGlow(Color c)
    {
        if (c == glow) return;
        glow = c;
        Apply();
    }

    IEnumerator Flash()
    {
        pressed = true;
        Apply();
        yield return new WaitForSeconds(feedbackTime);
        pressed = false;
        Apply();
        running = null;
    }

    void Apply()
    {
        Vector3 offset = transform.parent != null ? transform.parent.InverseTransformVector(pressOffset) : pressOffset;
        transform.localPosition = pressed ? basePosition + offset : basePosition;
        if (rend == null) return;
        block ??= new MaterialPropertyBlock();
        //per material slot, so e.g. a differently colored glyph keeps its own color (just darker)
        Material[] mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            block.Clear();
            Material m = mats[i];
            if (m != null)
            {
                if (pressed)
                {
                    if (m.HasProperty(BaseColorId)) block.SetColor(BaseColorId, m.GetColor(BaseColorId) * pressDarkening);
                    else if (m.HasProperty(ColorId)) block.SetColor(ColorId, m.GetColor(ColorId) * pressDarkening);
                }
                if (m.HasProperty(EmissionId)) block.SetColor(EmissionId, glow);
            }
            rend.SetPropertyBlock(block, i);
        }
    }
}
