using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// После полной сборки активирует кнопку. По нажатию:
/// 1) ракета выкатывается в rolloutPoint,
/// 2) отсчёт, зажигается огонь из сопла, ракета взлетает,
/// 3) сборка начинается заново (автоматически или по кнопке Restart).
/// </summary>
public class RocketRollout : MonoBehaviour
{
    [Header("Сборка")]
    [Tooltip("Менеджер: onAllCompleted включает кнопку, onReset сбрасывает состояние")]
    public PlacementManager manager;

    [Header("Куда выкатывать")]
    public Transform rolloutPoint;
    [Tooltip("Разворачивать ракету по повороту точки")]
    public bool matchPointRotation = false;

    [Header("Выкатывание")]
    public float duration = 6f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Взлёт")]
    public bool launchAfterRollout = true;
    [Tooltip("Сколько секунд после выката до старта")]
    public float launchDelay = 3f;
    [Tooltip("За сколько секунд до старта зажигается огонь")]
    public float ignitionLead = 1.5f;
    [Tooltip("Сколько секунд длится полёт")]
    public float flightTime = 8f;
    public float startSpeed = 0f;
    [Tooltip("Ускорение вверх, м/с²")]
    public float acceleration = 6f;

    [Header("Огонь из сопла")]
    [Tooltip("Свой префаб огня (ParticleSystem, направлен вниз). Если пусто — огонь создаётся из кода")]
    public GameObject flamePrefab;
    [Tooltip("Смещение огня от низа ракеты (центра основания)")]
    public Vector3 flameOffset = Vector3.zero;
    [Tooltip("Параметры огня из кода")]
    public float flameRadius = 0.15f;
    public float flameSize = 0.4f;
    public float flameSpeed = 6f;

    [Header("Кнопка запуска")]
    [Tooltip("UI-кнопка (необязательно). Для физической/XR-кнопки вызови Rollout() через UnityEvent")]
    public Button uiButton;
    [Tooltip("Объект кнопки: скрыт до конца сборки (необязательно)")]
    public GameObject buttonObject;
    public bool hideButtonAfterUse = true;

    [Header("Заново")]
    [Tooltip("Автоматически начать сборку заново после взлёта")]
    public bool autoRestart = true;
    public float restartDelay = 3f;
    [Tooltip("Кнопка «Собрать заново» (если autoRestart выключен). Появляется после взлёта")]
    public Button restartButton;

    [Header("События")]
    public UnityEvent onRolloutStarted;
    public UnityEvent onRolloutFinished;
    public UnityEvent onLaunchStarted;
    public UnityEvent onLaunchFinished;

    Transform root;
    GameObject flameGO;
    bool ready, rolling, done;

    void Awake()
    {
        if (manager != null)
        {
            manager.onAllCompleted.AddListener(Enable);
            manager.onReset.AddListener(ResetState);
        }
        if (uiButton != null) uiButton.onClick.AddListener(Rollout);
        if (restartButton != null) restartButton.onClick.AddListener(Restart);

        SetButton(false);
        SetRestartButton(false);
    }

    // ---------- публичное ----------

    /// <summary>Вызывается автоматически по onAllCompleted.</summary>
    public void Enable()
    {
        ready = true;
        SetButton(true);
    }

    /// <summary>Повесь на кнопку (UI Button / XR Simple Interactable и т.п.).</summary>
    public void Rollout()
    {
        if (!ready || rolling || done) return;
        if (rolloutPoint == null)
        {
            Debug.LogError("[RocketRollout] Не задан rolloutPoint.", this);
            return;
        }
        if (!BuildRoot()) return;

        StartCoroutine(RolloutRoutine());
    }

    /// <summary>Начать сборку заново (можно повесить на любую кнопку).</summary>
    public void Restart()
    {
        if (manager != null) manager.ResetAssembly(); // он вызовет onReset -> ResetState
        else ResetState();
    }

    // ---------- состояние ----------

    void ResetState()
    {
        StopAllCoroutines();

        if (root != null) Destroy(root.gameObject);
        root = null;
        if (flameGO != null) Destroy(flameGO);
        flameGO = null;

        ready = rolling = done = false;
        SetButton(false);
        SetRestartButton(false);
    }

    // собираем все поставленные детали под один корень, чтобы двигать как одно целое
    bool BuildRoot()
    {
        var items = new List<PlacementItem>();
        foreach (var it in PlacementItem.All)
            if (it != null && it.isPlaced) items.Add(it);

        if (items.Count == 0)
        {
            Debug.LogWarning("[RocketRollout] Нет поставленных деталей.", this);
            return false;
        }

        bool has = false;
        Bounds b = new Bounds();
        foreach (var it in items)
        {
            foreach (var r in it.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        }

        Vector3 basePoint = has
            ? new Vector3(b.center.x, b.min.y, b.center.z)
            : items[0].transform.position;

        root = new GameObject("Rocket_Root").transform;
        root.SetPositionAndRotation(basePoint, Quaternion.identity);

        foreach (var it in items)
            it.transform.SetParent(root, true); // позиции в мире сохраняются

        return true;
    }

    // ---------- выкатывание ----------

    IEnumerator RolloutRoutine()
    {
        rolling = true;
        SetButtonInteractable(false);
        onRolloutStarted?.Invoke();

        Vector3 startPos = root.position;
        Quaternion startRot = root.rotation;
        Vector3 endPos = rolloutPoint.position;
        Quaternion endRot = matchPointRotation ? rolloutPoint.rotation : startRot;

        float dur = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = curve.Evaluate(Mathf.Clamp01(t / dur));
            root.SetPositionAndRotation(
                Vector3.LerpUnclamped(startPos, endPos, k),
                Quaternion.SlerpUnclamped(startRot, endRot, k));
            yield return null;
        }

        root.SetPositionAndRotation(endPos, endRot);

        rolling = false;
        done = true;
        if (hideButtonAfterUse) SetButton(false);
        onRolloutFinished?.Invoke();

        if (launchAfterRollout)
            yield return LaunchRoutine();
        else
            AfterFlight();
    }

    // ---------- взлёт ----------

    IEnumerator LaunchRoutine()
    {
        float lead = Mathf.Clamp(ignitionLead, 0f, Mathf.Max(0f, launchDelay));
        yield return new WaitForSeconds(Mathf.Max(0f, launchDelay - lead));

        StartFlame();
        onLaunchStarted?.Invoke();
        yield return new WaitForSeconds(lead);

        float speed = startSpeed;
        float t = 0f;
        while (t < flightTime)
        {
            float dt = Time.deltaTime;
            t += dt;
            speed += acceleration * dt;
            root.position += Vector3.up * speed * dt;
            yield return null;
        }

        DetachFlame();
        onLaunchFinished?.Invoke();
        AfterFlight();
    }

    void AfterFlight()
    {
        if (autoRestart) StartCoroutine(RestartAfterDelay());
        else SetRestartButton(true);
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, restartDelay));
        Restart(); // последний вызов: ResetState остановит эту корутину
    }

    // ---------- огонь ----------

    void StartFlame()
    {
        Vector3 pos = root.position + flameOffset;

        if (flamePrefab != null)
            flameGO = Instantiate(flamePrefab, pos, flamePrefab.transform.rotation);
        else
            flameGO = BuildProceduralFlame(pos);

        flameGO.transform.SetParent(root, true); // летит вместе с ракетой

        foreach (var ps in flameGO.GetComponentsInChildren<ParticleSystem>())
            ps.Play();
    }

    // выключаем огонь: новые частицы не рождаются, старые догорают
    void DetachFlame()
    {
        if (flameGO == null) return;

        foreach (var ps in flameGO.GetComponentsInChildren<ParticleSystem>())
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        flameGO.transform.SetParent(null, true);
        Destroy(flameGO, 5f);
        flameGO = null;
    }

    GameObject BuildProceduralFlame(Vector3 pos)
    {
        var go = new GameObject("Rocket_Flame");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, 0f, 0f)); // конус смотрит вниз

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // duration нельзя менять на лету

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // след остаётся позади
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(flameSpeed * 0.8f, flameSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(flameSize * 0.6f, flameSize);
        main.startColor = Color.white;
        main.maxParticles = 600;

        var em = ps.emission;
        em.rateOverTime = 140f;

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 6f;
        sh.radius = flameRadius;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.05f), 0.4f),
                new GradientColorKey(new Color(0.6f, 0.1f, 0f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.7f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.material = CreateFlameMaterial();

        return go;
    }

    Material CreateFlameMaterial()
    {
        // в билде шейдер может вырезаться: для VR/Android лучше задать свой flamePrefab
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        bool urp = sh != null;
        if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        var m = new Material(sh);
        var tex = CreateSoftTexture();

        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);

        if (urp)
        {
            // прозрачный, аддитивный
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
        }
        return m;
    }

    // мягкий круг, чтобы частицы не были квадратами
    Texture2D CreateSoftTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    // ---------- кнопки ----------

    void SetButton(bool on)
    {
        if (buttonObject != null) buttonObject.SetActive(on);
        SetButtonInteractable(on);
    }

    void SetButtonInteractable(bool on)
    {
        if (uiButton != null) uiButton.interactable = on;
    }

    void SetRestartButton(bool on)
    {
        if (restartButton != null) restartButton.gameObject.SetActive(on);
    }
}
