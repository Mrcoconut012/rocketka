using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PartPaintPanel : MonoBehaviour
{
    [Serializable]
    public class ColorOption
    {
        public string name;
        [Tooltip("Готовая кнопка на сцене. Если пусто - создастся из colorButtonPrefab (рекомендуется)")]
        public Button button;
        [Tooltip("Спрайт на кнопку. Если пусто - остаётся тот, что уже стоит на кнопке")]
        public Sprite sprite;

        [Header("Цвет")]
        [Tooltip("Цвет, которым красится деталь")]
        public Color color = Color.white;

        [Header("Свойства материала")]
        [Tooltip("Включено: металлик берётся отсюда. Выключено: остаётся как в материале детали")]
        public bool setMetallic = false;
        [Range(0f, 1f)] public float metallic = 0f;

        [Tooltip("Включено: гладкость берётся отсюда. Выключено: остаётся как в материале детали")]
        public bool setSmoothness = false;
        [Range(0f, 1f)] public float smoothness = 0.5f;

        [Tooltip("Включено: деталь светится этим цветом. Выключено: свечение не трогается")]
        public bool setEmission = false;
        [ColorUsage(false, true)] public Color emissionColor = Color.black;
    }

    [Header("Источник деталей")]
    public PlacementManager manager;

    [Header("Список деталей (слева)")]
    public Transform partsContainer;     // родитель (Vertical Layout Group добавится сам, если нет)
    public Button partButtonPrefab;      // кнопка с TMP_Text внутри

    [Header("Размер списка деталей")]
    [Tooltip("Высота кнопки детали (в единицах канваса)")]
    public float partButtonHeight = 60f;
    [Tooltip("Расстояние между кнопками деталей")]
    public float partSpacing = 8f;
    [Tooltip("Текст на кнопке сам подгоняет размер под кнопку")]
    public bool autoSizePartText = true;
    public float partTextMinSize = 12f;
    public float partTextMaxSize = 36f;

    [Header("Палитра (справа)")]
    public Transform colorsContainer;    // родитель (Grid Layout Group добавится сам, если нет)
    public Button colorButtonPrefab;     // кнопка с Image
    [Tooltip("Готовые кнопки из списка переносятся в Colors Container, чтобы Grid их разложил")]
    public bool moveReadyButtonsToContainer = true;
    public List<ColorOption> colors = new List<ColorOption>();

    [Header("Размер палитры")]
    [Tooltip("Включено: кнопки цветов сами подстраиваются под ширину контейнера. Выключено: берётся Color Button Size")]
    public bool autoFitColors = true;
    [Min(1)] public int colorColumns = 4;
    public float colorSpacing = 10f;
    [Tooltip("Максимальный размер кнопки цвета при авто-подгонке")]
    public float colorMaxCellSize = 120f;
    [Tooltip("Размер кнопки цвета, если авто-подгонка выключена")]
    public Vector2 colorButtonSize = new Vector2(80f, 80f);

    [Header("Выделение выбранной детали")]
    public Color selectedPartTint = new Color(0.6f, 0.9f, 1f);
    public Color normalPartTint = Color.white;

    [Header("Применение к новым деталям")]
    [Tooltip("Как часто проверять новые заспавненные детали, сек")]
    public float rescanInterval = 0.25f;

    class PartEntry
    {
        public string id;
        public string title;
        public Button button;
    }

    readonly List<PartEntry> parts = new List<PartEntry>();
    readonly Dictionary<string, ColorOption> painted = new Dictionary<string, ColorOption>();
    readonly Dictionary<PlacementItem, ColorOption> applied = new Dictionary<PlacementItem, ColorOption>();

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP / HDRP
    static readonly int ColorId = Shader.PropertyToID("_Color");           // Built-in
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness"); // URP / HDRP
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness"); // Built-in Standard
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    MaterialPropertyBlock block;
    PartEntry selected;
    float timer;

    GridLayoutGroup colorGrid;
    RectTransform colorsRect;
    Vector2 lastColorsSize = new Vector2(-1f, -1f);

    void Start()
    {
        block = new MaterialPropertyBlock();
        SetupPartsLayout();
        SetupColorsLayout();
        BuildParts();
        BuildColors();
        if (parts.Count > 0) SelectPart(parts[0]);
    }

    // ---------- раскладка ----------

    void SetupPartsLayout()
    {
        if (partsContainer == null) return;

        var existing = partsContainer.GetComponent<LayoutGroup>();
        if (existing != null && !(existing is VerticalLayoutGroup))
        {
            Debug.LogWarning($"[PartPaintPanel] На '{partsContainer.name}' уже есть {existing.GetType().Name}, а нужен VerticalLayoutGroup. Удалите его или задайте другой контейнер.", partsContainer);
            return;
        }

        var vlg = existing as VerticalLayoutGroup;
        if (vlg == null) vlg = partsContainer.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.spacing = partSpacing;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
    }

    void SetupColorsLayout()
    {
        if (colorsContainer == null) return;

        colorsRect = colorsContainer as RectTransform;

        var existing = colorsContainer.GetComponent<LayoutGroup>();
        if (existing != null && !(existing is GridLayoutGroup))
        {
            Debug.LogWarning($"[PartPaintPanel] На '{colorsContainer.name}' уже есть {existing.GetType().Name}, а нужен GridLayoutGroup. Авто-подгонка цветов отключена. Удалите его или задайте другой контейнер.", colorsContainer);
            return;
        }

        colorGrid = existing as GridLayoutGroup;
        if (colorGrid == null) colorGrid = colorsContainer.gameObject.AddComponent<GridLayoutGroup>();

        colorGrid.spacing = new Vector2(colorSpacing, colorSpacing);
        colorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        colorGrid.constraintCount = Mathf.Max(1, colorColumns);
    }

    // Пересчёт размера кнопок цвета под контейнер
    void FitColors(bool force = false)
    {
        if (colorGrid == null || colorsRect == null) return;

        Vector2 size = colorsRect.rect.size;
        if (!force && size == lastColorsSize) return;
        lastColorsSize = size;

        int cols = Mathf.Max(1, colorColumns);
        colorGrid.spacing = new Vector2(colorSpacing, colorSpacing);
        colorGrid.constraintCount = cols;

        float free = size.x - colorGrid.padding.horizontal - colorSpacing * (cols - 1);

        // авто-подгонка выключена или ширина контейнера нулевая - берём ручной размер
        if (!autoFitColors || free <= cols * 10f)
        {
            colorGrid.cellSize = colorButtonSize;
            return;
        }

        float cell = Mathf.Clamp(free / cols, 10f, colorMaxCellSize);
        colorGrid.cellSize = new Vector2(cell, cell);
    }

    void LateUpdate()
    {
        FitColors(); // дёшево: пересчёт только при смене размера контейнера
    }

    // ---------- список деталей ----------

    void BuildParts()
    {
        if (manager == null)
        {
            Debug.LogError("[PartPaintPanel] Не задан PlacementManager.", this);
            return;
        }
        if (partButtonPrefab == null || partsContainer == null)
        {
            Debug.LogError("[PartPaintPanel] Не задан partButtonPrefab или partsContainer.", this);
            return;
        }

        var seen = new HashSet<string>();
        foreach (var step in manager.steps)
        {
            if (string.IsNullOrEmpty(step.itemId) || !seen.Add(step.itemId)) continue;

            var entry = new PartEntry
            {
                id = step.itemId,
                title = string.IsNullOrEmpty(step.name) ? step.itemId : step.name
            };

            var btn = Instantiate(partButtonPrefab, partsContainer);
            btn.gameObject.SetActive(true);

            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = partButtonHeight;
            le.minHeight = partButtonHeight;

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = entry.title;
                if (autoSizePartText)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMin = partTextMinSize;
                    label.fontSizeMax = partTextMaxSize;
                }
            }
            else
            {
                Debug.LogWarning($"[PartPaintPanel] В префабе кнопки детали нет TMP_Text (деталь '{entry.title}'). Замените Text (Legacy) на TextMeshPro.", partButtonPrefab);
            }

            entry.button = btn;
            btn.onClick.AddListener(() => SelectPart(entry));
            parts.Add(entry);
        }

        var partsRect = partsContainer as RectTransform;
        if (partsRect != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(partsRect);
        }
    }

    // ---------- палитра ----------

    void BuildColors()
    {
        Debug.Log($"[PartPaintPanel] BuildColors: элементов в Colors = {colors.Count}, " +
                  $"prefab = {(colorButtonPrefab != null ? colorButtonPrefab.name : "НЕ ЗАДАН")}, " +
                  $"container = {(colorsContainer != null ? colorsContainer.name : "НЕ ЗАДАН")}", this);

        if (colors.Count == 0)
        {
            Debug.LogWarning("[PartPaintPanel] Список Colors пуст - добавьте цвета в инспекторе.", this);
            return;
        }

        var used = new HashSet<Button>();
        int created = 0, reused = 0;

        foreach (var opt in colors)
        {
            Button btn = opt.button;

            if (btn == null)
            {
                if (colorButtonPrefab == null || colorsContainer == null)
                {
                    Debug.LogWarning($"[PartPaintPanel] Для цвета '{opt.name}' нет кнопки и не задан Color Button Prefab / Colors Container.", this);
                    continue;
                }
                btn = Instantiate(colorButtonPrefab, colorsContainer);
                btn.gameObject.SetActive(true);
                created++;
            }
            else if (!used.Add(btn))
            {
                // одна и та же кнопка назначена нескольким цветам - делаем копию
                Debug.LogWarning($"[PartPaintPanel] Кнопка '{btn.name}' назначена нескольким цветам, для '{opt.name}' создана копия.", btn);
                var parent = colorsContainer != null ? colorsContainer : btn.transform.parent;
                btn = Instantiate(btn, parent);
                btn.gameObject.SetActive(true);
                created++;
            }
            else
            {
                btn.gameObject.SetActive(true);
                if (moveReadyButtonsToContainer && colorsContainer != null && btn.transform.parent != colorsContainer)
                    btn.transform.SetParent(colorsContainer, false);
                reused++;
            }

            var img = btn.targetGraphic as Image;
            if (img == null) img = btn.GetComponent<Image>();
            if (img != null)
            {
                if (opt.sprite != null) img.sprite = opt.sprite;
                img.color = Color.white; // не искажаем спрайт
                img.preserveAspect = true;
            }

            var captured = opt;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => PaintSelected(captured));
        }

        if (colorsRect != null)
        {
            FitColors(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(colorsRect);
        }

        Debug.Log($"[PartPaintPanel] Цветов: создано {created}, использовано готовых {reused}.", this);
    }

    // ---------- выбор и покраска ----------

    void SelectPart(PartEntry entry)
    {
        selected = entry;
        foreach (var p in parts)
        {
            var img = p.button.targetGraphic as Image;
            if (img != null) img.color = (p == entry) ? selectedPartTint : normalPartTint;
        }
    }

    void PaintSelected(ColorOption opt)
    {
        if (selected == null) return;
        painted[selected.id] = opt;
        ApplyToAll();
    }

    void Update()
    {
        if (painted.Count == 0) return;
        timer += Time.unscaledDeltaTime;
        if (timer < rescanInterval) return;
        timer = 0f;
        ApplyToAll(); // подхватывает новые заспавненные детали
    }

    void ApplyToAll()
    {
        foreach (var item in PlacementItem.All)
        {
            if (item == null) continue;
            if (!painted.TryGetValue(item.id, out var opt)) continue;
            if (applied.TryGetValue(item, out var old) && ReferenceEquals(old, opt)) continue;

            Paint(item.gameObject, opt);
            applied[item] = opt;
        }

        // чистим удалённые детали из кэша
        var dead = new List<PlacementItem>();
        foreach (var kv in applied) if (kv.Key == null) dead.Add(kv.Key);
        foreach (var d in dead) applied.Remove(d);
    }

    void Paint(GameObject go, ColorOption opt)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            r.GetPropertyBlock(block);

            block.SetColor(BaseColorId, opt.color);
            block.SetColor(ColorId, opt.color);

            if (opt.setMetallic)
                block.SetFloat(MetallicId, opt.metallic);

            if (opt.setSmoothness)
            {
                block.SetFloat(SmoothnessId, opt.smoothness);
                block.SetFloat(GlossinessId, opt.smoothness);
            }

            if (opt.setEmission)
                block.SetColor(EmissionId, opt.emissionColor);

            r.SetPropertyBlock(block);
        }
    }
}