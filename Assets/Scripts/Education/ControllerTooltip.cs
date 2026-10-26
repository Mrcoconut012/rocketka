using TMPro;
using UnityEngine;

public class ControllerTooltip : MonoBehaviour
{
    [Header("Префаб")]
    public Transform labelRoot;    // World Space Canvas с фоном и текстом
    public TMP_Text label;
    public CanvasGroup group;
    public LineRenderer line;      // линия от кнопки до подписи
    public float fadeSpeed = 6f;

    Transform anchor;
    Vector3 localOffset;
    Vector3 baseScale = Vector3.one;
    float baseLineWidth = 0.002f;
    float scale = 1f;
    bool scaleOffset = true;
    float target;

    public void Init(Transform anchorPoint, string text, Vector3 offsetFromAnchor)
    {
        anchor = anchorPoint;
        localOffset = offsetFromAnchor;
        label.text = text;

        baseScale = labelRoot.localScale;
        baseLineWidth = line.widthMultiplier;

        line.useWorldSpace = true;
        line.positionCount = 2;

        group.alpha = 0f;
        gameObject.SetActive(false);
    }

    // s = итоговый размер (1 = как в префабе)
    public void SetScale(float s, bool scaleOffsetToo)
    {
        scale = Mathf.Max(0.01f, s);
        scaleOffset = scaleOffsetToo;

        labelRoot.localScale = baseScale * scale;
        line.widthMultiplier = baseLineWidth * scale;
    }

    public void SetVisible(bool visible)
    {
        target = visible ? 1f : 0f;
        if (visible) gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        if (anchor == null) return;

        // подпись висит рядом с кнопкой и едет вместе с контроллером
        Vector3 off = scaleOffset ? localOffset * scale : localOffset;
        labelRoot.position = anchor.TransformPoint(off);

        var cam = Camera.main;
        if (cam != null)
            labelRoot.rotation = Quaternion.LookRotation(labelRoot.position - cam.transform.position);

        line.SetPosition(0, anchor.position);
        line.SetPosition(1, labelRoot.position);

        group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        line.enabled = group.alpha > 0.01f;
        if (target == 0f && group.alpha <= 0.01f) gameObject.SetActive(false);
    }
}