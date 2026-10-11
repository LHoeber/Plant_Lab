using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One console button. Reacts on press down (immediately, also on touch). Its only feedback is a brief
/// press (slightly smaller and darker, shorter than one tick) and only if the action was accepted:
/// it confirms the action, never shows its effect, and nothing about it persists.
/// </summary>
[RequireComponent(typeof(Image))]
public class PanelButton : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Action index sent when pressed (0-3 camera, 4-7 intervention buttons A-D).")]
    public int action;
    public ControlPanel panel;
    [Tooltip("Seconds the press feedback lasts (shorter than a tick).")]
    public float feedbackTime = 0.12f;

    Image image;
    Color baseColor;
    Vector3 baseScale;
    Coroutine running;

    void Awake()
    {
        image = GetComponent<Image>();
        baseColor = image.color;
        baseScale = transform.localScale;
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
        transform.localScale = baseScale * 0.9f;
        image.color = baseColor * 0.75f;
        yield return new WaitForSeconds(feedbackTime);
        transform.localScale = baseScale;
        image.color = baseColor;
        running = null;
    }
}
