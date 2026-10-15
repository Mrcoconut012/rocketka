using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum SpawnPointMode
{
    [Tooltip("Над предыдущим поставленным объектом (PlacementAnchor или верх по Bounds)")]
    AfterPrevious,
    [Tooltip("В точке, заданной здесь, в шаге (customPoint)")]
    CustomPoint
}

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

    [Header("Где появится призрак")]
    public SpawnPointMode spawnMode = SpawnPointMode.AfterPrevious;

    [Tooltip("Используется при spawnMode = CustomPoint")]
    public Transform customPoint;

    [Tooltip("Ставить низ призрака на точку (для CustomPoint; для AfterPrevious берётся галка в менеджере)")]
    public bool alignBottomToCustomPoint = false;

    [Tooltip("Доп. смещение от точки спавна")]
    public Vector3 spawnOffset;

    [Header("Несколько одинаковых объектов")]
    [Min(1)]
    [Tooltip("Сколько одинаковых объектов нужно поставить в этом шаге (появятся сразу все призраки)")]
    public int count = 1;

    [Tooltip("Смещение каждой следующей копии относительно предыдущей (например (0.5,0,0))")]
    public Vector3 copyOffset = new Vector3(0.5f, 0f, 0f);

    [Tooltip("Необязательно: точные позиции И повороты копий (берутся у Transform). Если заполнено, count и copyOffset игнорируются")]
    public Transform[] copyPoints;

    public int TotalCount => (copyPoints != null && copyPoints.Length > 0) ? copyPoints.Length : Mathf.Max(1, count);
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
    [Tooltip("Ставить низ следующего призрака на точку спавна (режим AfterPrevious)")]
    public bool alignGhostBottomToSpawn = true;
    public UnityEvent onStepCompleted;
    public UnityEvent onAllCompleted;

    class GhostData
    {
        public GameObject go;
        public Renderer[] renderers;
    }

    int index = -1;
    Vector3 nextSpawnPos;

    readonly List<GhostData> ghosts = new List<GhostData>();
    readonly List<GameObject> placedInStep = new List<GameObject>();
    int remaining;

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
        SpawnStep(steps[index]);
    }

    void SpawnStep(PlacementStep step)
    {
        ghosts.Clear();
        placedInStep.Clear();
        remaining = 0;

        if (step.ghostPrefab == null)
        {
            Debug.LogError($"[PlacementManager] В шаге '{step.name}' не задан ghostPrefab.", this);
            return;
        }

        // базовая точка шага
        bool custom = step.spawnMode == SpawnPointMode.CustomPoint && step.customPoint != null;
        if (step.spawnMode == SpawnPointMode.CustomPoint && step.customPoint == null)
            Debug.LogWarning($"[PlacementManager] В шаге '{step.name}' выбран CustomPoint, но точка не задана — берётся позиция над предыдущим.", this);

        Vector3 basePos = custom ? step.customPoint.position : nextSpawnPos;
        bool align = custom ? step.alignBottomToCustomPoint
                            : (index > 0 && alignGhostBottomToSpawn);

        int total = step.TotalCount;
        bool usePoints = step.copyPoints != null && step.copyPoints.Length > 0;

        for (int i = 0; i < total; i++)
        {
            Vector3 pos;
            Quaternion rot = Quaternion.Euler(step.ghostEuler);
            if (usePoints)
            {
                var p = step.copyPoints[i];
                if (p == null) continue;
                pos = p.position + step.spawnOffset;
                // поворот берётся у точки; ghostEuler добавляется сверху как поправка
                rot = p.rotation * Quaternion.Euler(step.ghostEuler);
            }
            else
            {
                pos = basePos + step.spawnOffset + step.copyOffset * i;
            }

            if (SpawnGhost(step, pos, rot, align, i)) remaining++;
        }

        if (remaining == 0)
        {
            Debug.LogError($"[PlacementManager] Шаг '{step.name}': ни один призрак не создан, шаг пропущен.", this);
            NextStep();
        }
    }

    bool SpawnGhost(PlacementStep step, Vector3 pos, Quaternion rot, bool alignBottom, int copyIndex)
    {
        var go = Instantiate(step.ghostPrefab, pos, rot);
        go.name = step.TotalCount > 1
            ? $"Ghost_{step.name}_{copyIndex + 1}"
            : "Ghost_" + step.name;
        StripToVisualOnly(go);

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0)
        {
            Debug.LogError($"[PlacementManager] У призрака '{step.name}' нет Renderer.", this);
            Destroy(go);
            return false;
        }

        if (alignBottom)
        {
            Bounds b = GetBounds(rends);
            go.transform.position += Vector3.up * (pos.y - b.min.y);
        }

        foreach (var r in rends)
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = new Material(ghostMaterial);
            r.materials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        var data = new GhostData { go = go, renderers = rends };
        ghosts.Add(data);
        SetGhostColor(data, ghostColor);

        SetupTrigger(go);

        var zone = go.AddComponent<GhostSnapZone>();
        zone.Init(step.itemId, snapDuration, pullWhileHeld);
        zone.OnTargetInside += inside => SetGhostColor(data, inside ? ghostInsideColor : ghostColor);
        zone.OnPlaced += placed => OnGhostPlaced(data, placed);
        return true;
    }

    void OnGhostPlaced(GhostData data, GameObject placed)
    {
        placedInStep.Add(placed);
        ghosts.Remove(data);
        Destroy(data.go);
        remaining--;

        if (remaining > 0) return; // ждём остальные копии

        nextSpawnPos = CalcNextSpawnPos(placedInStep);
        onStepCompleted?.Invoke();
        NextStep();
    }

    // Для нескольких объектов: X/Z — среднее, Y — самая высокая точка.
    Vector3 CalcNextSpawnPos(List<GameObject> placed)
    {
        Vector3 sum = Vector3.zero;
        float maxY = float.MinValue;
        int n = 0;

        foreach (var p in placed)
        {
            if (p == null) continue;
            Vector3 pt = GetTopPoint(p);
            sum += pt;
            maxY = Mathf.Max(maxY, pt.y);
            n++;
        }

        if (n == 0) return nextSpawnPos;
        Vector3 avg = sum / n;
        return new Vector3(avg.x, maxY, avg.z);
    }

    Vector3 GetTopPoint(GameObject placed)
    {
        var anchor = placed.GetComponent<PlacementAnchor>();
        if (anchor != null) return anchor.GetNextSpawnPosition();

        var rends = placed.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = GetBounds(rends);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }
        return placed.transform.position;
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

    void SetGhostColor(GhostData data, Color c)
    {
        if (data == null || data.renderers == null) return;
        foreach (var r in data.renderers)
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