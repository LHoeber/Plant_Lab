using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One console button, 2D (UI image) or 3D (mesh with a collider). Reacts on press down (immediately, also on touch).
/// Its only feedback is a brief press, shorter than one tick and only if the action was accepted:
/// 2D: slightly smaller and darker; 3D: pushed in by pressOffset and darker.
/// It confirms the action, never shows its effect, and nothing about it persists.
/// </summary>
public class PanelButton : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Action index sent when pressed (0-3 camera, 4.. intervention buttons A, B, ...).")]
    public int action;
    public ControlPanel panel;
    [Tooltip("Seconds the press feedback lasts (shorter than a tick).")]
    public float feedbackTime = 0.12f;
    [Tooltip("3D buttons: how far the button moves while pressed, in world space (direction included).")]
    public Vector3 pressOffset;
    [Tooltip("Color factor while pressed.")]
    [Range(0f, 1f)] public float pressDarkening = 0.75f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

    Graphic graphic;
    Renderer rend;
    MaterialPropertyBlock block;
    Color baseColor;
    Vector3 baseScale, basePosition;
    Coroutine running;

    void Awake()
    {
        graphic = GetComponent<Graphic>();
        if (graphic != null) baseColor = graphic.color;
        else rend = GetComponent<Renderer>();
        baseScale = transform.localScale;
        basePosition = transform.localPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (panel != null && panel.Press(action)) Feedback();
    }

    public void Feedback()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Flash());
    }

    IEnumerator Flash()
    {
        SetPressed(true);
        yield return new WaitForSeconds(feedbackTime);
        SetPressed(false);
        running = null;
    }

    void SetPressed(bool pressed)
    {
        if (graphic != null)
        {
            transform.localScale = pressed ? baseScale * 0.9f : baseScale;
            graphic.color = pressed ? baseColor * pressDarkening : baseColor;
            return;
        }
        Vector3 offset = transform.parent != null ? transform.parent.InverseTransformVector(pressOffset) : pressOffset;
        transform.localPosition = pressed ? basePosition + offset : basePosition;
        if (rend == null) return;
        block ??= new MaterialPropertyBlock();
        //per material slot, so e.g. a differently colored arrow glyph keeps its own color (just darker)
        Material[] mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            block.Clear();
            if (pressed && mats[i] != null)
            {
                if (mats[i].HasProperty(BaseColorId)) block.SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * pressDarkening);
                else if (mats[i].HasProperty(ColorId)) block.SetColor(ColorId, mats[i].GetColor(ColorId) * pressDarkening);
            }
            rend.SetPropertyBlock(block, i);
        }
    }
}
