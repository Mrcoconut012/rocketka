using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Вешается на любой объект. По нажатию кнопки поворачивает ВСЕ поставленные детали
/// (и активных призраков) как одно целое вокруг общей точки.
/// После каждого шага сборки (PlacementManager.onStepCompleted), ещё до появления
/// следующего призрака, возвращает ракету в исходное вращение.
/// Работает и с UI-кнопкой (Button), и с физической/XR-кнопкой:
/// у XR-кнопки вызови RotateButtonPressed() через UnityEvent.
/// </summary>
public class RotatePlacedParts : MonoBehaviour
{
    public enum Mode
    {
        [Tooltip("Каждое нажатие - поворот на заданный угол")]
        Step,
        [Tooltip("Нажатие включает вращение, повторное нажатие выключает")]
        ContinuousToggle
    }

    [Header("Кнопка")]
    [Tooltip("UI-кнопка (необязательно). Для XR-кнопки вызови RotateButtonPressed() из UnityEvent")]
    public Button uiButton;

    [Header("Поворот")]
    public Mode mode = Mode.Step;
    [Tooltip("Ось вращения")]
    public Vector3 axis = Vector3.up;
    [Tooltip("Ось задана в локальных координатах pivot (если pivot задан). Иначе - мировая")]
    public bool axisInPivotSpace = false;
    [Tooltip("Step: угол за одно нажатие, градусы")]
    public float angle = 90f;
    [Tooltip("Step: длительность поворота, сек")]
    public float duration = 0.6f;
    [Tooltip("ContinuousToggle: скорость, градусов в секунду")]
    public float speed = 45f;

    [Header("Вокруг чего вращать")]
    [Tooltip("Центр вращения. Если пусто - центр деталей в момент первого поворота (дальше точка не меняется до возврата)")]
    public Transform pivot;

    [Header("Призраки")]
    [Tooltip("Вращать и активные призраки (GhostSnapZone) вместе с деталями")]
    public bool rotateGhosts = true;

    [Header("Возврат после шага")]
    [Tooltip("После каждого завершённого шага вернуть начальное вращение (мгновенно, до появления нового призрака)")]
    public bool returnAfterEachStep = true;

    [Header("Дополнительно")]
    [Tooltip("Другие объекты, которые тоже нужно вращать вместе с деталями (например, основание)")]
    public List<Transform> extraTargets = new List<Transform>();
    [Tooltip("Менеджер: по onStepCompleted возвращает вращение, по onReset сбрасывает состояние")]
    public PlacementManager manager;

    bool rotating;
    bool continuousOn;
    Coroutine stepRoutine;

    // суммарный поворот, применённый ко всей сборке (мировая система), и точка вращения
    Quaternion total = Quaternion.identity;
    Vector3 sessionPivot;
    bool sessionPivotSet;

    readonly List<Transform> ghostCache = new List<Transform>();
    readonly HashSet<int> knownGhosts = new HashSet<int>();
    float nextGhostRefresh;

    void Awake()
    {
        if (uiButton != null) uiButton.onClick.AddListener(RotateButtonPressed);
        if (manager != null)
        {
            manager.onReset.AddListener(ResetState);
            if (returnAfterEachStep) manager.onStepCompleted.AddListener(SnapToStart);
        }
    }

    void OnDestroy()
    {
        if (uiButton != null) uiButton.onClick.RemoveListener(RotateButtonPressed);
        if (manager != null)
        {
            manager.onReset.RemoveListener(ResetState);
            manager.onStepCompleted.RemoveListener(SnapToStart);
        }
    }

    // ---------- публичное ----------

    /// <summary>Повесь на кнопку (UI Button / XR Simple Interactable и т.п.).</summary>
    public void RotateButtonPressed()
    {
        if (rotating) return; // предыдущий поворот ещё идёт

        if (rotateGhosts) RefreshGhosts();

        if (mode == Mode.Step)
        {
            stepRoutine = StartCoroutine(StepRoutine());
        }
        else
        {
            continuousOn = !continuousOn;
        }
    }

    /// <summary>
    /// Мгновенно вернуть исходное вращение. Вызывается автоматически по onStepCompleted:
    /// менеджер сразу после этого создаёт призрак следующего шага, поэтому возврат синхронный.
    /// </summary>
    public void SnapToStart()
    {
        StopRotation();

        if (!IsIdentity(total))
            ApplyDelta(Quaternion.Inverse(total));

        ClearSession();
    }

    /// <summary>Остановить вращение (без возврата).</summary>
    public void StopRotation()
    {
        continuousOn = false;
        if (stepRoutine != null) StopCoroutine(stepRoutine);
        stepRoutine = null;
        rotating = false;
    }

    // сборка сброшена: объекты пересоздаются, запоминать ничего не нужно
    void ResetState()
    {
        StopRotation();
        ClearSession();
    }

    // ---------- вращение ----------

    void Update()
    {
        bool active = continuousOn || rotating || !IsIdentity(total);
        if (rotateGhosts && active && Time.time >= nextGhostRefresh) RefreshGhosts();

        if (mode != Mode.ContinuousToggle || !continuousOn) return;

        var targets = CollectTargets(out var parts);
        if (targets.Count == 0) return;

        RotateAll(targets, GetSessionPivot(parts, targets), GetAxis(), speed * Time.deltaTime);
    }

    IEnumerator StepRoutine()
    {
        if (CollectTargets(out _).Count == 0) yield break;

        rotating = true;
        Vector3 ax = GetAxis(); // ось фиксируется в начале поворота

        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;
        float prevK = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            ApplyRotation(ax, angle * (k - prevK));
            prevK = k;
            yield return null;
        }

        // добираем остаток, чтобы суммарно получилось ровно angle
        ApplyRotation(ax, angle * (1f - prevK));

        rotating = false;
        stepRoutine = null;
    }

    void ApplyDelta(Quaternion delta)
    {
        delta.ToAngleAxis(out float a, out Vector3 ax);
        if (a > 180f) a -= 360f;
        if (Mathf.Abs(a) < 0.0001f || float.IsNaN(ax.x) || float.IsInfinity(ax.x)) return;
        ApplyRotation(ax, a);
    }

    void ApplyRotation(Vector3 ax, float deg)
    {
        var targets = CollectTargets(out var parts);
        if (targets.Count == 0) return;
        RotateAll(targets, GetSessionPivot(parts, targets), ax, deg);
    }

    void RotateAll(List<Transform> targets, Vector3 center, Vector3 ax, float deg)
    {
        foreach (var t in targets)
            if (t != null) t.RotateAround(center, ax, deg);

        total = Quaternion.AngleAxis(deg, ax) * total;
        total.Normalize();
    }

    void ClearSession()
    {
        total = Quaternion.identity;
        sessionPivotSet = false;
        knownGhosts.Clear();
    }

    // ---------- цели ----------

    // parts - поставленные детали + extraTargets (по ним считается центр вращения)
    // возвращает parts + активные призраки (если включено rotateGhosts)
    List<Transform> CollectTargets(out List<Transform> parts)
    {
        parts = new List<Transform>();

        foreach (var it in PlacementItem.All)
            if (it != null && it.isPlaced) parts.Add(it.transform);

        foreach (var t in extraTargets)
            if (t != null && !parts.Contains(t)) parts.Add(t);

        var all = new List<Transform>(parts);

        if (rotateGhosts)
        {
            foreach (var g in ghostCache)
                if (g != null && !all.Contains(g)) all.Add(g);
        }

        return all;
    }

    // Ищет активных призраков. Призрак, появившийся ПОСЛЕ поворота (новый шаг сборки),
    // сразу разворачивается на накопленный угол (на месте), чтобы деталь, поставленная на него,
    // лежала в той же системе, что и вся сборка - тогда возврат в начало корректен.
    void RefreshGhosts()
    {
        ghostCache.Clear();
        bool rotated = !IsIdentity(total);

        foreach (var z in FindObjectsByType<GhostSnapZone>(FindObjectsSortMode.None))
        {
            if (z == null) continue;
            Transform g = z.transform;
            ghostCache.Add(g);

            if (knownGhosts.Add(g.GetInstanceID()) && rotated)
                g.rotation = total * g.rotation;
        }

        nextGhostRefresh = Time.time + 0.1f;
    }

    // ---------- вспомогательное ----------

    // точка вращения фиксируется при первом повороте и не меняется до возврата/сброса
    Vector3 GetSessionPivot(List<Transform> parts, List<Transform> all)
    {
        if (pivot != null) return pivot.position;

        if (!sessionPivotSet)
        {
            sessionPivot = ComputeCenter(parts, all);
            sessionPivotSet = true;
        }
        return sessionPivot;
    }

    // центр считаем по деталям (parts); если деталей нет - по всем целям (например, только призраки)
    Vector3 ComputeCenter(List<Transform> parts, List<Transform> all)
    {
        var targets = parts.Count > 0 ? parts : all;
        if (targets.Count == 0) return transform.position;

        bool has = false;
        Bounds b = new Bounds();
        foreach (var t in targets)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        if (has) return b.center;

        Vector3 sum = Vector3.zero;
        foreach (var t in targets) sum += t.position;
        return sum / targets.Count;
    }

    Vector3 GetAxis()
    {
        Vector3 a = axis.sqrMagnitude < 0.0001f ? Vector3.up : axis.normalized;
        if (axisInPivotSpace && pivot != null) a = pivot.TransformDirection(a);
        return a;
    }

    static bool IsIdentity(Quaternion q)
    {
        return Quaternion.Angle(q, Quaternion.identity) < 0.01f;
    }
}