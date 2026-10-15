using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Панель на Canvas: слева кнопки деталей (по PlacementItem.id), справа палитра цветов.
/// Выбрал деталь -> нажал цвет -> все детали с этим id перекрашены.
/// Краска идёт через MaterialPropertyBlock, материалы-ассеты не портятся.
/// </summary>
public class PartPaintPanel : MonoBehaviour
{
    [Serializable]
    public class PartEntry
    {
        public string itemId;
        [Tooltip("Текст на кнопке (если пусто — itemId)")]
        public string label;
    }

    [Header("Какие детали показывать")]
    [Tooltip("Если задан — кнопки создаются из шагов менеджера (уникальные itemId)")]
    public PlacementManager manager;
    [Tooltip("Дополнительные / ручные детали")]
    public List<PartEntry> manualParts = new List<PartEntry>();

    [Header("UI")]
    [Tooltip("Префаб кнопки: Button + Image, внутри (необязательно) TMP_Text")]
    public Button buttonPrefab;
    public RectTransform partsContainer;
    public RectTransform colorsContainer;
    public TMP_Text selectedLabel;
    [Tooltip("Необязательно: сброс цвета выбранной детали")]
    public Button resetButton;

    [Header("Цвета")]
    public List<Color> palette = new List<Color>
    {
        Color.white, Color.black, Color.red, new Color(1f, 0.5f, 0f),
        Color.yellow, Color.green, Color.cyan, Color.blue, Color.magenta
    };
    public Color normalButtonColor = Color.white;
    public Color selectedButtonColor = new Color(1f, 0.9f, 0.4f);

    [Header("Прочее")]
    [Tooltip("Красить только уже поставленные на место детали")]
    public bool onlyPlaced = true;

    readonly Dictionary<string, Button> partButtons = new Dictionary<string, Button>();
    readonly Dictionary<string, string> labels = new Dictionary<string, string>();
    readonly Dictionary<string, Color> painted = new Dictionary<string, Color>();
    MaterialPropertyBlock block; // создаётся в Awake: в конструкторе MonoBehaviour нельзя
    string selectedId;

    void Awake()
    {
        block = new MaterialPropertyBlock();
    }

    void Start()
    {
        BuildPartButtons();
        BuildColorButtons();

        if (resetButton != null) resetButton.onClick.AddListener(ResetSelected);

        // новая деталь встала на место -> применяем уже выбранные цвета
        if (manager != null) manager.onStepCompleted.AddListener(ReapplyAll);

        UpdateSelectionVisuals();
    }

    // ---------- построение UI ----------

    void BuildPartButtons()
    {
        if (manager != null)
        {
            foreach (var s in manager.steps)
                AddPart(s.itemId, string.IsNullOrEmpty(s.name) ? s.itemId : s.name);
        }
        foreach (var p in manualParts)
            AddPart(p.itemId, string.IsNullOrEmpty(p.label) ? p.itemId : p.label);
    }

    void AddPart(string id, string label)
    {
        if (string.IsNullOrEmpty(id) || partButtons.ContainsKey(id)) return;

        var btn = Instantiate(buttonPrefab, partsContainer, false);
        SetLabel(btn, label);
        string captured = id;
        btn.onClick.AddListener(() => Select(captured));

        partButtons[id] = btn;
        labels[id] = label;
    }

    void BuildColorButtons()
    {
        foreach (var c in palette)
        {
            var btn = Instantiate(buttonPrefab, colorsContainer, false);
            var img = btn.targetGraphic as Image;
            if (img != null) img.color = c;

            var txt = btn.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.gameObject.SetActive(false);

            Color captured = c;
            btn.onClick.AddListener(() => Paint(captured));
        }
    }

    static void SetLabel(Button btn, string text)
    {
        var t = btn.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = text;
    }

    // ---------- логика ----------

    public void Select(string id)
    {
        selectedId = id;
        UpdateSelectionVisuals();
    }

    public void Paint(Color color)
    {
        if (string.IsNullOrEmpty(selectedId)) return;
        painted[selectedId] = color;
        Apply(selectedId, color);
    }

    public void ResetSelected()
    {
        if (string.IsNullOrEmpty(selectedId)) return;
        painted.Remove(selectedId);

        foreach (var item in PlacementItem.All)
        {
            if (!Matches(item, selectedId)) continue;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(null);
        }
    }

    public void ReapplyAll()
    {
        foreach (var kv in painted) Apply(kv.Key, kv.Value);
    }

    bool Matches(PlacementItem item, string id)
    {
        return item != null && item.id == id && (!onlyPlaced || item.isPlaced);
    }

    void Apply(string id, Color color)
    {
        block.Clear();
        block.SetColor("_BaseColor", color); // URP
        block.SetColor("_Color", color);     // Built-in

        foreach (var item in PlacementItem.All)
        {
            if (!Matches(item, id)) continue;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(block);
        }
    }

    void UpdateSelectionVisuals()
    {
        foreach (var kv in partButtons)
        {
            var img = kv.Value.targetGraphic as Image;
            if (img != null)
                img.color = kv.Key == selectedId ? selectedButtonColor : normalButtonColor;
        }

        if (selectedLabel != null)
        {
            selectedLabel.text = string.IsNullOrEmpty(selectedId)
                ? "Выберите деталь"
                : "Деталь: " + (labels.TryGetValue(selectedId, out var l) ? l : selectedId);
        }
    }
}