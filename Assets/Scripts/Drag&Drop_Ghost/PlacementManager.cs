using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class PlacementStep
{
    public string name;

    [Tooltip("Должен совпадать с PlacementItem.id у префаба из меню")]
    public string itemId;

    [Tooltip("Префаб для вида призрака (обычно тот же, что в ButtonSpawnerList)")]
    public GameObject ghostPrefab;

    [Tooltip("Поворот призрака (Euler)")]
    public Vector3 ghostEuler;

    [Tooltip("Доп. смещение от точки спавна")]
    public Vector3 spawnOffset;
}

public class PlacementManager : MonoBehaviour
{
    [Header("Шаги (по порядку)")]
    public List<PlacementStep> steps = new List<PlacementStep>();

    [Header("Первый призрак появится здесь")]
    public Transform firstSpawnPoint;

    [Header("Вид призрака")]
    public Material ghostMaterial;
    public Color ghostColor = new Color(0.3f, 0.7f, 1f, 0.35f);
    public Color ghostInsideColor = new Color(0.3f, 1f, 0.4f, 0.55f);

    [Header("Притягивание")]
    public float snapDuration = 0.35f;
    [Tooltip("Притягивать, даже если объект ещё в руке")]
    public bool pullWhileHeld = false;
    [Tooltip("Запас триггера вокруг призрака, метры")]
    public float triggerPadding = 0.1f;

    [Header("Прочее")]
    [Tooltip("Ставить низ следующего призрака на точку спавна")]
    public bool alignGhostBottomToSpawn = true;
    public UnityEvent onStepCompleted;
    public UnityEvent onAllCompleted;

    int index = -1;
    GameObject ghost;
    Renderer[] ghostRenderers;
    Vector3 nextSpawnPos;

    void Start()
    {
        nextSpawnPos = firstSpawnPoint != null ? firstSpawnPoint.position : transform.position;
        NextStep();
    }

    void NextStep()
    {
        index++;
        if (index >= steps.Count)
        {
            onAllCompleted?.Invoke();
            return;
        }
        SpawnGhost(steps[index]);
    }

    void SpawnGhost(PlacementStep step)
    {
        if (step.ghostPrefab == null)
        {
            Debug.LogError($"[PlacementManager] В шаге '{step.name}' не задан ghostPrefab.", this);
            return;
        }

        Vector3 pos = nextSpawnPos + step.spawnOffset;
        ghost = Instantiate(step.ghostPrefab, pos, Quaternion.Euler(step.ghostEuler));
        ghost.name = "Ghost_" + step.name;
        StripToVisualOnly(ghost);

        ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
        if (ghostRenderers.Length == 0)
        {
            Debug.LogError($"[PlacementManager] У призрака '{step.name}' нет Renderer.", this);
            return;
        }

        // следующие призраки ставим низом на точку над предыдущим объектом
        if (index > 0 && alignGhostBottomToSpawn)
        {
            Bounds b = GetBounds(ghostRenderers);
            ghost.transform.position += Vector3.up * (pos.y - b.min.y);
        }

        foreach (var r in ghostRenderers)
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = new Material(ghostMaterial);
            r.materials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        SetGhostColor(ghostColor);

        SetupTrigger(ghost);

        var zone = ghost.AddComponent<GhostSnapZone>();
        zone.Init(step.itemId, snapDuration, pullWhileHeld);
        zone.OnTargetInside += inside => SetGhostColor(inside ? ghostInsideColor : ghostColor);
        zone.OnPlaced += placed => OnStepPlaced(placed);
    }

    void OnStepPlaced(GameObject placed)
    {
        var anchor = placed.GetComponent<PlacementAnchor>();
        if (anchor != null)
        {
            nextSpawnPos = anchor.GetNextSpawnPosition();
        }
        else
        {
            var rends = placed.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = GetBounds(rends);
                nextSpawnPos = new Vector3(b.center.x, b.max.y, b.center.z);
            }
            else nextSpawnPos = placed.transform.position;
        }

        Destroy(ghost);
        onStepCompleted?.Invoke();
        NextStep();
    }

    // ---------- вспомогательное ----------

    void SetupTrigger(GameObject g)
    {
        var cols = g.GetComponentsInChildren<Collider>();
        if (cols.Length > 0)
        {
            foreach (var c in cols) c.isTrigger = true;
            return;
        }

        Bounds b = GetBounds(g.GetComponentsInChildren<Renderer>());
        Vector3 s = g.transform.lossyScale;
        var box = g.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = g.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(
            (b.size.x + triggerPadding * 2f) / Mathf.Max(0.0001f, s.x),
            (b.size.y + triggerPadding * 2f) / Mathf.Max(0.0001f, s.y),
            (b.size.z + triggerPadding * 2f) / Mathf.Max(0.0001f, s.z));
    }

    // оставляем только визуал; сначала скрипты, потом Rigidbody, потом коллайдеры
    void StripToVisualOnly(GameObject g)
    {
        var scripts = g.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--)
            if (scripts[i] != null) DestroyImmediate(scripts[i]);

        var bodies = g.GetComponentsInChildren<Rigidbody>(true);
        for (int i = bodies.Length - 1; i >= 0; i--)
            if (bodies[i] != null) DestroyImmediate(bodies[i]);

        var cols = g.GetComponentsInChildren<Collider>(true);
        for (int i = cols.Length - 1; i >= 0; i--)
            if (cols[i] != null) DestroyImmediate(cols[i]);
    }

    Bounds GetBounds(Renderer[] rends)
    {
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return b;
    }

    void SetGhostColor(Color c)
    {
        if (ghostRenderers == null) return;
        foreach (var r in ghostRenderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP
                else if (m.HasProperty("_Color")) m.SetColor("_Color", c);    // Built-in
            }
        }
    }
}