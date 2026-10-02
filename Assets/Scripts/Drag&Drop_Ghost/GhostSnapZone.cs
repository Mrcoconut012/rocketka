using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: удалить эту строку

/// <summary>
/// Добавляется на призрака менеджером. Ждёт объект с PlacementItem нужного id,
/// и когда тот в триггере (и отпущен) плавно притягивает его в центр призрака.
/// </summary>
public class GhostSnapZone : MonoBehaviour
{
    public event Action<bool> OnTargetInside;      // подсветка призрака
    public event Action<GameObject> OnPlaced;      // объект встал на место

    string requiredId;
    float duration;
    bool pullWhileHeld;

    PlacementItem candidate;
    int contacts;
    bool snapping;

    public void Init(string requiredId, float duration, bool pullWhileHeld)
    {
        this.requiredId = requiredId;
        this.duration = Mathf.Max(0.01f, duration);
        this.pullWhileHeld = pullWhileHeld;
    }

    bool IsValid(PlacementItem item)
    {
        return item != null && !item.isPlaced && item.id == requiredId && item.Grab != null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (snapping) return;
        var item = other.GetComponentInParent<PlacementItem>();
        if (!IsValid(item)) return;
        if (candidate != null && candidate != item) return; // уже есть другой кандидат

        candidate = item;
        contacts++;
        if (contacts == 1) OnTargetInside?.Invoke(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (snapping || candidate == null) return;
        if (other.GetComponentInParent<PlacementItem>() != candidate) return;

        contacts = Mathf.Max(0, contacts - 1);
        if (contacts == 0)
        {
            candidate = null;
            OnTargetInside?.Invoke(false);
        }
    }

    void Update()
    {
        if (snapping || candidate == null) return;

        // кандидата могли удалить (например destroyAfter в спавнере)
        if (!IsValid(candidate))
        {
            candidate = null;
            contacts = 0;
            OnTargetInside?.Invoke(false);
            return;
        }

        if (pullWhileHeld || !candidate.Grab.isSelected)
            StartCoroutine(SnapRoutine(candidate));
    }

    IEnumerator SnapRoutine(PlacementItem item)
    {
        snapping = true;
        item.isPlaced = true;          // другие призраки его больше не возьмут
        item.Grab.enabled = false;     // отпускает из руки и запрещает хват

        var rb = item.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Transform t = item.transform;
        Vector3 startPos = t.position;
        Quaternion startRot = t.rotation;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, time / duration);
            t.SetPositionAndRotation(
                Vector3.Lerp(startPos, transform.position, k),
                Quaternion.Slerp(startRot, transform.rotation, k));
            yield return null;
        }

        t.SetPositionAndRotation(transform.position, transform.rotation);
        OnPlaced?.Invoke(item.gameObject);
    }
}