using System;
using System.Linq;
using UnityEngine;

// Runtime scanner: finds common interactor types used by Meta/Oculus building blocks
// and reports their presence and some key settings via reflection.
// Attach this to an active scene GameObject and run Play to get diagnostics.

public class InteractorScanner : MonoBehaviour
{
    void Start()
    {
        Debug.Log("InteractorScanner: Scanning for interactors...");

        // Search scene for components whose type name contains keywords
        var allComponents = FindObjectsOfType<Component>(true);

        var interactorTypes = new[] { "HandGrabInteractor", "GrabInteractor", "HandInteractor", "OVRGrabber", "DistanceHandGrabInteractor" };

        foreach (var typeKeyword in interactorTypes)
        {
            var found = allComponents.Where(c => c != null && c.GetType().Name.Contains(typeKeyword)).ToList();
            Debug.Log($"Found {found.Count} components matching '{typeKeyword}'");
            foreach (var comp in found)
            {
                try
                {
                    Debug.Log($" - {comp.GetType().FullName} on GameObject: {comp.gameObject.name} (activeInHierarchy={comp.gameObject.activeInHierarchy})");

                    // Attempt to reflectively get a layer mask or includeMasks field if present
                    var type = comp.GetType();
                    var maskField = type.GetField("_layerMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                 ?? type.GetField("layerMask", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (maskField != null)
                    {
                        var maskVal = maskField.GetValue(comp);
                        Debug.Log($"   layerMask field found: {maskVal}");
                    }

                    // Try to get a 'Enabled' or 'enabled' property
                    var enabledProp = type.GetProperty("enabled");
                    if (enabledProp != null)
                    {
                        var enabled = enabledProp.GetValue(comp);
                        Debug.Log($"   component.enabled = {enabled}");
                    }

                    // If it's a HandGrabInteractor, attempt to print whether pinch/palm are configured
                    if (type.Name.Contains("HandGrabInteractor"))
                    {
                        var pinchField = type.GetField("_pinch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (pinchField != null)
                        {
                            Debug.Log("   HandGrabInteractor has _pinch field (inspected)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"InteractorScanner: failed to inspect component {comp.GetType().Name}: {ex.Message}");
                }
            }
        }

        // Also quick check: are there any Input/HandTracking providers active?
        var inputProviders = allComponents.Where(c => c != null && c.GetType().Name.Contains("HandSubsystem") || c.GetType().Name.Contains("HandTracking") || c.GetType().Name.Contains("HandTrackingManager")).ToList();
        Debug.Log($"InteractorScanner: found {inputProviders.Count} hand-tracking related components.");

        Debug.Log("InteractorScanner: scan complete.");
    }
}
