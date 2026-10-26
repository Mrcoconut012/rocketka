using System.Collections;
using TMPro;
using UnityEngine;

public class AssemblyHint : MonoBehaviour
{
    [Header("Связи")]
    public PlacementManager manager;

    [Header("Плашка (World Space Canvas)")]
    public CanvasGroup panel;
    public TMP_Text text;
    public string stepFormat = "Шаг {0}/{1}: установи «{2}»";
    public string allDoneText = "Ракета собрана! Нажми кнопку запуска.";

    [Header("Расположение")]
    [Tooltip("Один раз поставить плашку перед игроком при старте сцены")]
    public bool placeInFrontOnStart = true;
    public float showDelay = 1.5f;
    public float distance = 1.6f;
    public float heightOffset = -0.1f;
    public float fadeTime = 0.4f;

    [Header("Звук (необязательно, 2D)")]
    public AudioSource sfxSource;
    public AudioClip stepDoneClip;
    public AudioClip allDoneClip;

    int stepsDone;
    bool allDone;

    void Start()
    {
        if (manager != null)
        {
            manager.onStepCompleted.AddListener(OnStepCompleted);
            manager.onAllCompleted.AddListener(OnAllCompleted);
            manager.onReset.AddListener(OnReset);
        }

        SetGroup(0f);
        UpdateText();
        StartCoroutine(ShowRoutine());
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.onStepCompleted.RemoveListener(OnStepCompleted);
        manager.onAllCompleted.RemoveListener(OnAllCompleted);
        manager.onReset.RemoveListener(OnReset);
    }

    IEnumerator ShowRoutine()
    {
        yield return new WaitForSecondsRealtime(showDelay); // пока экран открывается из чёрного
        if (placeInFrontOnStart) PlaceInFront(panel.transform);
        yield return Fade(1f);
    }

    void OnStepCompleted() { stepsDone++; Sfx(stepDoneClip); UpdateText(); }
    void OnAllCompleted() { allDone = true; Sfx(allDoneClip); UpdateText(); }

    void OnReset()
    {
        stepsDone = 0;
        allDone = false;
        UpdateText();
    }

    void UpdateText()
    {
        if (manager == null || text == null) return;
        if (allDone || stepsDone >= manager.steps.Count) { text.text = allDoneText; return; }

        text.text = string.Format(stepFormat, stepsDone + 1, manager.steps.Count, manager.steps[stepsDone].name);
    }

    void Sfx(AudioClip c)
    {
        if (sfxSource != null && c != null) sfxSource.PlayOneShot(c);
    }

    void PlaceInFront(Transform t)
    {
        var cam = Camera.main;
        if (cam == null || t == null) return;

        Vector3 f = cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.01f) f = cam.transform.forward;
        f.Normalize();

        Vector3 pos = cam.transform.position + f * distance;
        pos.y = cam.transform.position.y + heightOffset;

        t.position = pos;
        t.rotation = Quaternion.LookRotation(pos - cam.transform.position);
    }

    void SetGroup(float a)
    {
        if (panel == null) return;
        panel.alpha = a;
        panel.interactable = panel.blocksRaycasts = a > 0.5f;
    }

    IEnumerator Fade(float target)
    {
        if (panel == null) yield break;
        float start = panel.alpha, t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            panel.alpha = Mathf.Lerp(start, target, t / Mathf.Max(0.01f, fadeTime));
            yield return null;
        }
        SetGroup(target);
    }
}