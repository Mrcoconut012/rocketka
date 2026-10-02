using UnityEngine;

/// <summary>
/// Вешается на оригинал. Задаёт точку, над которой появится следующий призрак.
/// Если nextSpawnPoint не задан, берётся центр верхней грани по Bounds.
/// </summary>
public class PlacementAnchor : MonoBehaviour
{
    public Transform nextSpawnPoint;

    public Vector3 GetNextSpawnPosition()
    {
        if (nextSpawnPoint != null) return nextSpawnPoint.position;

        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return transform.position;

        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }
}