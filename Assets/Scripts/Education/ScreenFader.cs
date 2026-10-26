using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScreenFader : MonoBehaviour
{
    [Tooltip("Canvas: Screen Space - Camera, Render Camera = камера XR Origin, Plane Distance ~0.3. Внутри чёрный Image на весь экран")]
    public CanvasGroup group;

    [Header("Появление сцены")]
    public bool fadeInOnStart = true;
    public float startDelay = 0.3f;
    public float fadeInTime = 1.2f;

    [Header("Уход в чёрное при смене сцены")]
    public float fadeOutTime = 0.8f;

    bool loading;

    void Awake()
    {
        if (group == null) return;
        group.alpha = 1f;                 // старт с чёрного экрана
        group.blocksRaycasts = false;     // не мешает лучам по UI
        group.interactable = false;
    }

    void Start()
    {
        if (fadeInOnStart) StartCoroutine(FadeInRoutine());
        else if (group != null) group.alpha = 0f;
    }

    IEnumerator FadeInRoutine()
    {
        yield return new WaitForSecondsRealtime(startDelay);
        yield return Fade(0f, fadeInTime);
    }

    public IEnumerator Fade(float target, float time)
    {
        if (group == null) yield break;
        float start = group.alpha, t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, t / Mathf.Max(0.01f, time));
            yield return null;
        }
        group.alpha = target;
    }

    // уйти в чёрное и загрузить сцену (её нужно добавить в Build Settings)
    public void LoadScene(string sceneName)
    {
        if (loading) return;
        loading = true;
        StopAllCoroutines();
        StartCoroutine(LoadRoutine(sceneName));
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        yield return Fade(1f, fadeOutTime);
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (op != null && !op.isDone) yield return null;
    }
}