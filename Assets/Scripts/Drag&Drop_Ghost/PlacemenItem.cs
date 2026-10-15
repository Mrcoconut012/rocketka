using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: удалить эту строку

/// <summary>
/// Вешается на ПРЕФАБ, который спавнится из меню (ButtonSpawnerList).
/// id должен совпадать с itemId нужного шага в PlacementManager.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class PlacementItem : MonoBehaviour
{
    /// <summary>Все активные детали на сцене (для панели покраски и т.п.).</summary>
    public static readonly List<PlacementItem> All = new List<PlacementItem>();

    public string id = "item";

    [HideInInspector] public bool isPlaced;

    public XRGrabInteractable Grab => GetComponent<XRGrabInteractable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => All.Clear(); // на случай отключённого Domain Reload

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}