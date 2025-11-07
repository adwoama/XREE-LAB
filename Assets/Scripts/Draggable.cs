using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Adds basic draggable behaviour to a panel. If XR Interaction Toolkit's XRGrabInteractable
/// exists in the project, this script will add that component at runtime to enable hand-ray grabbing.
/// Falls back to simple editor mouse drag (OnMouseDown/OnMouseDrag) for quick testing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Draggable : MonoBehaviour
{
    Camera mainCam;
    bool hasXRGrab = false;

    // mouse drag state (editor / fallback)
    Vector3 offset;
    float distanceToCamera;

    void Awake()
    {
        mainCam = Camera.main;
        // If the GameObject already has a HandGrabInteractable / Grabbable-like component
        // (added above by WaveformManager), respect it and do not add vendor grabbables.
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    var n = t.Name.ToLowerInvariant();
                    if (n.Contains("handgrabinteractable") || n.Contains("handgrab") || n == "grabbable" || n.Contains("grabinteractable"))
                    {
                        try
                        {
                            var existing = GetComponent(t);
                            if (existing != null)
                            {
                                hasXRGrab = true;
                                Debug.Log($"Draggable: detected existing interactable {t.FullName}, skipping vendor add.");
                                break;
                            }
                        }
                        catch { }
                    }
                }
                if (hasXRGrab) break;
            }
        }
        catch { }
        // try to add XRGrabInteractable if available
        var xrType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable, Unity.XR.Interaction.Toolkit");
        if (xrType != null)
        {
            // ensure there's a Rigidbody for XRGrabInteractable to use
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            if (GetComponent(xrType) == null)
            {
                gameObject.AddComponent(xrType);
            }
            hasXRGrab = true;
            Debug.Log("Draggable: XRGrabInteractable added for better hand interactions if XR Interactors exist.");
        }

        // If XR Interaction Toolkit is not present, try to detect other vendor grab components (OVR/Oculus or Meta)
        if (!hasXRGrab)
        {
            // search loaded assemblies for a type with 'Grabbable' in the name (common in Oculus/OVR integrations)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (!t.IsClass) continue;
                        var n = t.Name.ToLowerInvariant();
                        // prefer Meta/interaction grabbable types and avoid the legacy OVRGrabbable which
                        // can throw during Awake when dynamically added (it dereferences fields that may be null).
                        if (n.Contains("grabbable") || n.Contains("gripgrab") || n.Contains("grabbableobject") || n.Contains("grabbler"))
                        {
                            // skip known problematic types (OVR legacy)
                            if (t.Name.Equals("OVRGrabbable", System.StringComparison.OrdinalIgnoreCase)) continue;
                            var asmName = t.Assembly.GetName().Name.ToLowerInvariant();
                            if (asmName.Contains("oculus") || asmName.Contains("ovr")) continue;
                            if (GetComponent(t) == null)
                            {
                                try { gameObject.AddComponent(t); Debug.Log($"Draggable: added vendor grabbable component {t.FullName}"); }
                                catch { }
                            }
                            hasXRGrab = true;
                            break;
                        }
                    }
                    if (hasXRGrab) break;
                }
                catch { }
            }
        }
    }

    // Fallback editor drag with mouse/gaze
    void OnMouseDown()
    {
        if (hasXRGrab) return;
        if (!mainCam) mainCam = Camera.main;
        distanceToCamera = Vector3.Distance(transform.position, mainCam.transform.position);
        var screenPoint = mainCam.WorldToScreenPoint(transform.position);
        var worldPoint = mainCam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z));
        offset = transform.position - worldPoint;
    }

    void OnMouseDrag()
    {
        if (hasXRGrab) return;
        if (!mainCam) return;
        var screenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, distanceToCamera);
        var worldPoint = mainCam.ScreenToWorldPoint(screenPoint);
        transform.position = worldPoint + offset;
    }
}
