using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class IntroScene : MonoBehaviour
{
    [Serializable]
    public class Page
    {
        public string title;
        [TextArea(2, 6)] public string text;
        [Tooltip("Объект-подсказка (стрелка, рамка), включается только на этой странице")]
        public GameObject highlight;
        [Tooltip("Озвучка страницы (необязательно)")]
        public AudioClip voice;
    }

    [Header("Связи")]
    public ScreenFader fader;
    [Tooltip("Имя сцены сборки (должна быть в Build Settings)")]
    public string mainSceneName = "Main";

    [Header("Панель обучения (World Space Canvas)")]
    public CanvasGroup panel;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public TMP_Text pageCounterText;
    public Button backButton;
    public Button nextButton;
    public TMP_Text nextButtonLabel;

    [Header("Страницы")]
    public List<Page> pages = new List<Page>();
    public string lastPageButtonText = "Начать сборку!";

    [Header("Звук (необязательно, 2D)")]
    public AudioSource voiceSource;
    public AudioSource sfxSource;
    public AudioClip clickClip;

    [Header("Появление и расположение")]
    [Tooltip("Пауза до показа панели (пока экран открывается из чёрного)")]
    public float panelDelay = 1.5f;
    public float distance = 1.6f;
    public float heightOffset = -0.1f;
    public float fadeTime = 0.4f;

    int pageIndex;
    bool starting;

    void Start()
    {
        backButton.onClick.AddListener(() => { Sfx(clickClip); ShowPage(pageIndex - 1); });
        nextButton.onClick.AddListener(OnNext);

        SetGroup(panel, 0f);
        foreach (var pg in pages)
            if (pg.highlight != null) pg.highlight.SetActive(false);

        StartCoroutine(Begin());
    }

    IEnumerator Begin()
    {
        yield return new WaitForSecondsRealtime(panelDelay);
        ShowIntro();
    }

    // можно повесить на кнопку «Сначала»
    public void ShowIntro()
    {
        PlaceInFront(panel.transform);
        ShowPage(0);
        StartCoroutine(Fade(panel, 1f));
    }

    void ShowPage(int i)
    {
        if (pages.Count == 0) { StartGame(); return; }
        pageIndex = Mathf.Clamp(i, 0, pages.Count - 1);
        var p = pages[pageIndex];

        titleText.text = p.title;
        bodyText.text = p.text;
        pageCounterText.text = $"{pageIndex + 1}/{pages.Count}";

        backButton.gameObject.SetActive(pageIndex > 0);
        nextButtonLabel.text = pageIndex == pages.Count - 1 ? lastPageButtonText : "Дальше";

        foreach (var pg in pages)
            if (pg.highlight != null) pg.highlight.SetActive(pg == p);

        Voice(p.voice);
    }

    void OnNext()
    {
        Sfx(clickClip);
        if (pageIndex < pages.Count - 1) ShowPage(pageIndex + 1);
        else StartGame();
    }

    void StartGame()
    {
        if (starting) return;   // защита от двойного нажатия
        starting = true;

        foreach (var pg in pages)
            if (pg.highlight != null) pg.highlight.SetActive(false);

        Voice(null);
        StartCoroutine(Fade(panel, 0f));

        if (fader != null) fader.LoadScene(mainSceneName);
        else UnityEngine.SceneManagement.SceneManager.LoadScene(mainSceneName);
    }

    // ---------- звук ----------

    void Sfx(AudioClip c)
    {
        if (sfxSource != null && c != null) sfxSource.PlayOneShot(c);
    }

    void Voice(AudioClip c)
    {
        if (voiceSource == null) return;
        voiceSource.Stop();
        if (c == null) return;
        voiceSource.clip = c;
        voiceSource.Play();
    }

    // ---------- вспомогательное ----------

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

    void SetGroup(CanvasGroup g, float a)
    {
        if (g == null) return;
        g.alpha = a;
        g.interactable = g.blocksRaycasts = a > 0.5f;
    }

    IEnumerator Fade(CanvasGroup g, float target)
    {
        if (g == null) yield break;
        float start = g.alpha, t = 0f;
        g.blocksRaycasts = true;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(start, target, t / fadeTime);
            yield return null;
        }
        SetGroup(g, target);
    }
}