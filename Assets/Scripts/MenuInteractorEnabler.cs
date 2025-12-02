using UnityEngine;

/// <summary>
/// Enables/disables hand ray or poke interactors only while a target Canvas is active.
/// Assign your Canvas (world-space) and the interactor GameObjects (e.g., Distance Hand Ray prefabs, Poke Interactors).
/// This prevents rays from interfering with gesture detection when the menu is hidden.
/// </summary>
public class MenuInteractorEnabler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("World-space menu Canvas whose active state controls interactors.")]
    public Canvas menuCanvas;

    [Tooltip("Interactor GameObjects to toggle (hand rays, poke interactors, etc.)")] 
    public GameObject[] interactors;

    [Header("Behavior")]
    [Tooltip("Also disable line visuals on disable by hiding child LineRenderer components.")]
    public bool hideLineVisuals = true;

    private bool lastActive;

    void Start()
    {
        if (menuCanvas == null)
        {
            menuCanvas = GetComponentInChildren<Canvas>(true);
        }
        Apply(menuCanvas != null && menuCanvas.gameObject.activeSelf);
    }

    void Update()
    {
        if (menuCanvas == null) return;
        bool isActive = menuCanvas.gameObject.activeSelf;
        if (isActive != lastActive)
        {
            Apply(isActive);
        }
    }

    private void Apply(bool enable)
    {
        lastActive = enable;
        if (interactors == null) return;
        foreach (var go in interactors)
        {
            if (go == null) continue;
            go.SetActive(enable);
            if (hideLineVisuals)
            {
                var lines = go.GetComponentsInChildren<LineRenderer>(true);
                foreach (var lr in lines) lr.enabled = enable; // show only when menu visible
            }
        }
    }
}
