using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Один компонент на Canvas. В списке Entries у каждой кнопки свои:
/// звук, задержка, префаб, точка спавна, количество и т.д.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ButtonSpawnerList : MonoBehaviour
{
    public enum ButtonRemoval
    {
        None,       // кнопка остаётся
        Hide,       // кнопка скрывается (SetActive(false)), можно вернуть позже
        Destroy     // кнопка удаляется полностью
    }

    public enum RemoveMoment
    {
        OnClick,    // сразу после нажатия (с учётом Remove Delay)
        AfterSpawn  // после завершения спавна (с учётом Remove Delay)
    }

    [System.Serializable]
    public class SpawnEntry
    {
        [Header("Общее")]
        public string name = "Entry";
        public Button button;

        [Header("Звук")]
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;

        [Header("Спавн")]
        public GameObject prefab;
        public Transform spawnPoint;
        public Vector3 positionOffset = Vector3.zero;
        public bool parentToSpawnPoint = false;
        [Tooltip("Задержка в секундах после нажатия до первого спавна")]
        public float delay = 2f;

        [Header("Несколько объектов")]
        [Min(1)] public int count = 1;
        [Tooltip("Интервал между спавнами, если count > 1")]
        public float interval = 0.5f;

        [Header("Прочее")]
        [Tooltip("Удалить заспавненный объект через N секунд (0 = не удалять)")]
        public float destroyAfter = 0f;
        [Tooltip("Блокировать кнопку, пока идёт цикл (звук + спавн)")]
        public bool lockWhileRunning = true;

        [Header("Удаление кнопки")]
        public ButtonRemoval removeButton = ButtonRemoval.None;
        public RemoveMoment removeWhen = RemoveMoment.AfterSpawn;
        [Tooltip("Задержка в секундах перед удалением/скрытием кнопки")]
        public float removeDelay = 0f;

        [System.NonSerialized] public bool isRunning;
    }

    [SerializeField] private List<SpawnEntry> entries = new List<SpawnEntry>();

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void OnEnable()
    {
        foreach (var entry in entries)
        {
            if (entry.button == null) continue;

            // Копия переменной, чтобы замыкание не ссылалось на изменяющийся foreach
            var e = entry;
            e.button.onClick.AddListener(() => Trigger(e));
        }
    }

    private void OnDisable()
    {
        foreach (var entry in entries)
        {
            if (entry.button != null)
                entry.button.onClick.RemoveAllListeners();
        }
    }

    private void Trigger(SpawnEntry entry)
    {
        bool mustBlock = entry.lockWhileRunning || entry.removeButton != ButtonRemoval.None;
        if (mustBlock && entry.isRunning) return;
        Debug.Log("Первое есть нажатие");
        StartCoroutine(Run(entry));
    }

    private IEnumerator RemoveButton(SpawnEntry entry)
    {
        if (entry.removeDelay > 0f)
            yield return new WaitForSeconds(entry.removeDelay);

        if (entry.button == null) yield break;

        if (entry.removeButton == ButtonRemoval.Hide)
            entry.button.gameObject.SetActive(false);
        else if (entry.removeButton == ButtonRemoval.Destroy)
            Destroy(entry.button.gameObject);
    }

    private IEnumerator Run(SpawnEntry entry)
    {
        Debug.Log("куратина пошла");
        entry.isRunning = true;
        if ((entry.lockWhileRunning || entry.removeButton != ButtonRemoval.None) && entry.button != null)
            entry.button.interactable = false;

        // Удаление сразу после нажатия: отдельная корутина, не мешает спавну
        if (entry.removeButton != ButtonRemoval.None && entry.removeWhen == RemoveMoment.OnClick)
            StartCoroutine(RemoveButton(entry));

        // Звук у каждой записи свой; PlayOneShot не обрывает другие звуки
        if (entry.clip != null)
            audioSource.PlayOneShot(entry.clip, entry.volume);

        if (entry.delay > 0f)
            yield return new WaitForSeconds(entry.delay);

        for (int i = 0; i < entry.count; i++)
        {
            
            Spawn(entry);
            Debug.Log("спавн прошел");
            if (i < entry.count - 1 && entry.interval > 0f)
                yield return new WaitForSeconds(entry.interval);
        }

        entry.isRunning = false;

        if (entry.removeButton == ButtonRemoval.None)
        {
            // Кнопка остаётся: возвращаем возможность нажатия
            if (entry.button != null)
                entry.button.interactable = true;
        }
        else if (entry.removeWhen == RemoveMoment.AfterSpawn)
        {
            yield return RemoveButton(entry);
        }
    }

    private void Spawn(SpawnEntry entry)
    {
        if (entry.prefab == null)
        {
            Debug.LogWarning($"[{name}] У записи '{entry.name}' не назначен префаб.", this);
            return;
        }

        Transform point = entry.spawnPoint != null ? entry.spawnPoint : transform;
        Vector3 pos = point.TransformPoint(entry.positionOffset);

        GameObject obj = Instantiate(entry.prefab, pos, point.rotation);
        obj.name = entry.prefab.name;
        if (entry.parentToSpawnPoint)
            obj.transform.SetParent(point, true);

        if (entry.destroyAfter > 0f)
            Destroy(obj, entry.destroyAfter);
    }
}