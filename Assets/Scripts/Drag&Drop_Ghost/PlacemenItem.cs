using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: удалить эту строку

/// <summary>
/// Вешается на ПРЕФАБ, который спавнится из меню (ButtonSpawnerList).
/// id должен совпадать с itemId нужного шага в PlacementManager.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class PlacementItem : MonoBehaviour
{
    public string id = "item";

    [HideInInspector] public bool isPlaced;

    public XRGrabInteractable Grab => GetComponent<XRGrabInteractable>();
}