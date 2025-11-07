using System;
using System.Linq;
using UnityEngine;

// Diagnostic helper: attach this to a GameObject in the scene (or leave as-is)
// and run Play. It will find GameObjects named "Panel_CH*" and print a short
// diagnostics summary about Rigidbody, Colliders, and Grabbable/HandGrab components.

public class PanelGrabValidator : MonoBehaviour
{
    void Start()
    {
        var panels = FindObjectsOfType<Transform>()
            .Where(t => t.name.StartsWith("Panel_CH", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.gameObject)
            .ToList();

        if (panels.Count == 0)
        {
            Debug.Log("PanelGrabValidator: No panels named 'Panel_CH*' found in scene.");
            return;
        }

        foreach (var p in panels)
        {
            Debug.Log($"\n--- Panel diagnostics: {p.name} ---");
            var rb = p.GetComponent<Rigidbody>();
            Debug.Log($"Rigidbody: {(rb != null ? "present" : "MISSING")}{(rb!=null?" (isKinematic="+rb.isKinematic+")":"")}");

            var colliders = p.GetComponentsInChildren<Collider>(true);
            Debug.Log($"Colliders in hierarchy: {colliders.Length}");
            foreach (var c in colliders)
            {
                Debug.Log($" - {c.gameObject.name} : {c.GetType().Name} trigger={c.isTrigger} layer={LayerMask.LayerToName(c.gameObject.layer)}");
            }

            // Find any HandGrabInteractable-like component
            var handGrab = FindComponentByName(p, "HandGrabInteractable");
            if (handGrab != null)
            {
                Debug.Log($"HandGrabInteractable: present ({handGrab.GetType().FullName})");
                // Try to read Colliders property via reflection
                var collProp = handGrab.GetType().GetProperty("Colliders");
                if (collProp != null)
                {
                    var arr = collProp.GetValue(handGrab) as Collider[];
                    Debug.Log($" -> HandGrab.Colliders length: {(arr!=null?arr.Length.ToString():"null")} ");
                }

                // Check Rigidbody field/property
                var rbProp = handGrab.GetType().GetProperty("Rigidbody");
                if (rbProp != null)
                {
                    var assigned = rbProp.GetValue(handGrab) as Rigidbody;
                    Debug.Log($" -> HandGrab.Rigidbody assigned: {(assigned!=null?"yes":"no")}");
                }
            }
            else
            {
                Debug.Log("HandGrabInteractable: NOT found on this panel.");
            }

            // Also look for GrabInteractable / Grabbable / OVRGrabbable
            var grab = FindComponentByName(p, "GrabInteractable") ?? FindComponentByName(p, "Grabbable");
            if (grab != null)
            {
                Debug.Log($"Grab-like component present: {grab.GetType().FullName}");
            }
            else
            {
                Debug.Log("No GrabInteractable/Grabbable found on panel.");
            }
        }
    }

    Component FindComponentByName(GameObject root, string typeName)
    {
        foreach (var comp in root.GetComponents<Component>())
        {
            if (comp == null) continue;
            if (comp.GetType().Name.Contains(typeName)) return comp;
        }
        foreach (var child in root.GetComponentsInChildren<Component>(true))
        {
            if (child == null) continue;
            if (child.GetType().Name.Contains(typeName)) return child;
        }
        return null;
    }
}
