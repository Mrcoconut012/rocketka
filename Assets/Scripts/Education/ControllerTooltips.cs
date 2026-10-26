using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ControllerTooltips : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string text = "Захват";
        [Tooltip("Пустой объект на модели контроллера в месте кнопки")]
        public Transform anchor;
        [Tooltip("Куда сместить подпись от кнопки (в осях контроллера, метры)")]
        public Vector3 offset = new Vector3(0.06f, 0.03f, 0f);
        [Tooltip("Размер именно этой подсказки относительно общего (1 = как у всех)")]
        [Min(0.1f)] public float sizeMultiplier = 1f;
    }

    public ControllerTooltip tooltipPrefab;
    public List<Entry> entries = new List<Entry>();

    [Header("Размер")]
    [Tooltip("Общий размер всех подсказок. Можно двигать прямо в Play Mode")]
    [Range(0.3f, 3f)] public float size = 1f;
    public float minSize = 0.3f;
    public float maxSize = 3f;
    [Tooltip("Шаг для IncreaseSize / DecreaseSize")]
    public float sizeStep = 0.1f;
    [Tooltip("Расстояние от кнопки до подписи растёт вместе с размером")]
    public bool scaleOffsetWithSize = true;
    [Tooltip("UI-ползунок размера (необязательно)")]
    public Slider sizeSlider;

    [Header("Поведение")]
    public bool showOnStart = true;
    [Tooltip("0 = не скрывать автоматически")]
    public float autoHideAfter = 0f;

    [Header("Показать/скрыть по кнопке (необязательно)")]
    public InputActionProperty toggleAction;   // например Secondary Button левого контроллера

    class Item
    {
        public ControllerTooltip tooltip;
        public Entry entry;
    }

    readonly List<Item> created = new List<Item>();
    bool visible;
    bool initialized;
    float appliedSize = -1f;
    bool appliedOffsetFlag;

    void Start()
    {
        foreach (var e in entries)
        {
            if (e.anchor == null) continue;
            var t = Instantiate(tooltipPrefab);
            t.Init(e.anchor, e.text, e.offset);
            created.Add(new Item { tooltip = t, entry = e });
        }
        initialized = true;

        if (sizeSlider != null)
        {
            sizeSlider.minValue = minSize;
            sizeSlider.maxValue = maxSize;
            sizeSlider.SetValueWithoutNotify(size);
            sizeSlider.onValueChanged.AddListener(SetSize);
        }

        ApplySize(true);

        if (toggleAction.action != null)
        {
            toggleAction.action.Enable();
            toggleAction.action.performed += OnToggle;
        }

        if (showOnStart) Show();
    }

    void OnDestroy()
    {
        if (toggleAction.action != null)
            toggleAction.action.performed -= OnToggle;
        if (sizeSlider != null)
            sizeSlider.onValueChanged.RemoveListener(SetSize);
    }

    void Update()
    {
        // ловит изменение размера из инспектора в Play Mode
        if (initialized) ApplySize(false);
    }

    void OnToggle(InputAction.CallbackContext _)
    {
        if (visible) Hide(); else Show();
    }

    // ---------- размер ----------

    public void SetSize(float value)
    {
        size = Mathf.Clamp(value, minSize, maxSize);
        if (sizeSlider != null) sizeSlider.SetValueWithoutNotify(size);
        ApplySize(false);
    }

    public void IncreaseSize() { SetSize(size + sizeStep); }
    public void DecreaseSize() { SetSize(size - sizeStep); }

    void ApplySize(bool force)
    {
        if (!force && Mathf.Approximately(size, appliedSize) && appliedOffsetFlag == scaleOffsetWithSize)
            return;

        appliedSize = size;
        appliedOffsetFlag = scaleOffsetWithSize;

        foreach (var it in created)
            if (it.tooltip != null)
                it.tooltip.SetScale(size * it.entry.sizeMultiplier, scaleOffsetWithSize);
    }

    // ---------- показ ----------

    // эти методы можно вешать на UnityEvent / кнопку
    public void Show()
    {
        if (!initialized) return; // Start() сам покажет, если showOnStart включён
        visible = true;
        foreach (var it in created) it.tooltip.SetVisible(true);
        StopAllCoroutines();
        if (autoHideAfter > 0f) StartCoroutine(HideLater());
    }

    public void Hide()
    {
        visible = false;
        foreach (var it in created) it.tooltip.SetVisible(false);
        StopAllCoroutines();
    }

    IEnumerator HideLater()
    {
        yield return new WaitForSecondsRealtime(autoHideAfter);
        Hide();
    }
}