using TMPro;
using UnityEngine;

/// <summary>
/// Таймер сборки ракеты и рекорд (лучшее время хранится в PlayerPrefs).
/// Старт: при первом хвате любой детали (или сразу, если startOnFirstGrab выключен).
/// Стоп: когда поставлена последняя деталь (PlacementManager.onAllCompleted).
/// Сбрасывается по PlacementManager.onReset.
/// </summary>
public class AssemblyTimer : MonoBehaviour
{
    [Header("Связи")]
    public PlacementManager manager;

    [Header("UI (необязательно)")]
    public TMP_Text timeText;
    public TMP_Text bestText;
    [Tooltip("Показывается, когда побит рекорд")]
    public GameObject newRecordBadge;

    [Header("Настройки")]
    [Tooltip("Запускать таймер, когда игрок впервые взял деталь в руку")]
    public bool startOnFirstGrab = true;
    public string prefsKey = "RocketBestTime";

    enum State { Waiting, Running, Stopped }
    State state = State.Waiting;
    float elapsed;

    public float Elapsed => elapsed;
    public float BestTime => PlayerPrefs.GetFloat(prefsKey, -1f);

    void Awake()
    {
        if (manager != null)
        {
            manager.onAllCompleted.AddListener(StopTimer);
            manager.onReset.AddListener(ResetTimer);
        }
    }

    void Start()
    {
        ResetTimer();
    }

    void Update()
    {
        if (state == State.Waiting)
        {
            if (!startOnFirstGrab || AnyPartGrabbed())
                state = State.Running;
        }

        if (state == State.Running)
        {
            elapsed += Time.deltaTime;
            UpdateTexts();
        }
    }

    bool AnyPartGrabbed()
    {
        foreach (var it in PlacementItem.All)
        {
            if (it == null || it.isPlaced) continue;
            var g = it.Grab;
            if (g != null && g.isSelected) return true;
        }
        return false;
    }

    public void ResetTimer()
    {
        state = State.Waiting;
        elapsed = 0f;
        if (newRecordBadge != null) newRecordBadge.SetActive(false);
        UpdateTexts();
    }

    public void StopTimer()
    {
        if (state == State.Stopped) return;

        // если стоп пришёл до старта (например, всё собрано мгновенно)
        state = State.Stopped;

        float best = BestTime;
        if (best < 0f || elapsed < best)
        {
            PlayerPrefs.SetFloat(prefsKey, elapsed);
            PlayerPrefs.Save();
            if (newRecordBadge != null) newRecordBadge.SetActive(true);
        }
        UpdateTexts();
    }

    [ContextMenu("Сбросить рекорд")]
    public void ClearRecord()
    {
        PlayerPrefs.DeleteKey(prefsKey);
        UpdateTexts();
    }

    void UpdateTexts()
    {
        if (timeText != null) timeText.text = "Время: " + Format(elapsed);
        if (bestText != null)
        {
            float best = BestTime;
            bestText.text = "Рекорд: " + (best < 0f ? "--:--.--" : Format(best));
        }
    }

    static string Format(float t)
    {
        int min = (int)(t / 60f);
        int sec = (int)(t % 60f);
        int cs = (int)((t * 100f) % 100f);
        return string.Format("{0:00}:{1:00}.{2:00}", min, sec, cs);
    }
}
