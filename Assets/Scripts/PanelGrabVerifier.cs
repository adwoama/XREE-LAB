using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Verifies generated panels for expected interaction wiring and attempts safe re-injection
/// of PointableElement into HandGrabInteractable / GrabInteractable if missing.
/// Runs one frame after scene load to avoid timing/order issues.
/// </summary>
public class PanelGrabVerifier : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("_PanelGrabVerifier");
        DontDestroyOnLoad(go);
        go.AddComponent<PanelGrabVerifier>();
    }

    IEnumerator Start()
    {
        // wait one frame to let installers run
        yield return null;

        Debug.Log("PanelGrabVerifier: Starting verification pass for Panel_CH* objects");

        // find interaction SDK types via name (reflection tolerant)
        System.Type grabbableType = null;
        System.Type handGrabType = null;
        System.Type grabInteractableType = null;

        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (grabbableType == null && t.Name == "Grabbable") grabbableType = t;
                    if (handGrabType == null && (t.Name == "HandGrabInteractable" || (t.Name.Contains("HandGrab") && t.Name.Contains("Interactable")))) handGrabType = t;
                    if (grabInteractableType == null && t.Name == "GrabInteractable") grabInteractableType = t;
                    if (grabbableType != null && handGrabType != null && grabInteractableType != null) break;
                }
                if (grabbableType != null && handGrabType != null && grabInteractableType != null) break;
            }
            catch { }
        }

        var panels = GameObject.FindObjectsOfType<Transform>();
        foreach (var tf in panels)
        {
            if (!tf.name.StartsWith("Panel_CH")) continue;
            var panel = tf.gameObject;
            Debug.Log($"PanelGrabVerifier: Inspecting {panel.name}");

            var rb = panel.GetComponent<Rigidbody>();
            Debug.Log($" - Rigidbody: {(rb!=null?"present":"missing")}");

            var colliders = panel.GetComponentsInChildren<Collider>();
            Debug.Log($" - Colliders total (incl children): {colliders.Length}");
            foreach (var c in colliders) Debug.Log($"   - {c.gameObject.name}: {c.GetType().Name} size/center (if BoxCollider): " + (c as BoxCollider != null ? ((BoxCollider)c).size.ToString() : "n/a"));

            object parentGrabbable = null;
            if (grabbableType != null)
            {
                parentGrabbable = panel.GetComponent(grabbableType);
                Debug.Log($" - Grabbable component: {(parentGrabbable!=null?grabbableType.FullName:"(none)")}");

                if (parentGrabbable != null)
                {
                    // try to inspect the Grabbable's injected Rigidbody (private field _rigidbody)
                    try
                    {
                        var rgField = grabbableType.GetField("_rigidbody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                      ?? grabbableType.GetField("rigidbody", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (rgField != null)
                        {
                            var val = rgField.GetValue(parentGrabbable) as Rigidbody;
                            Debug.Log($"   -> Grabbable._rigidbody: {(val!=null?"present":"(null)")}");
                        }
                    }
                    catch { }
                }
            }

            // find the installation child
            Transform installTf = panel.transform.Find("HandGrabInstallationRoutine");
            if (installTf == null)
            {
                // try to locate a child that contains the interactables
                foreach (Transform c in panel.transform)
                {
                    if (c.GetComponent(grabInteractableType) != null || c.GetComponent(handGrabType) != null)
                    {
                        installTf = c;
                        break;
                    }
                }
            }

            if (installTf == null)
            {
                Debug.LogWarning($" - PanelGrabVerifier: no HandGrabInstallationRoutine or candidate child found on {panel.name}");
                continue;
            }

            var install = installTf.gameObject;

            // Inspect HandGrabInteractable
            if (handGrabType != null)
            {
                var hcomp = install.GetComponent(handGrabType);
                Debug.Log($" - HandGrabInteractable component: {(hcomp!=null?handGrabType.FullName:"(none)")}");
                if (hcomp != null)
                {
                    // inspect and optionally adjust HandAlignment
                    try
                    {
                        var alignProp = handGrabType.GetProperty("HandAlignment");
                        if (alignProp != null)
                        {
                            var cur = alignProp.GetValue(hcomp, null);
                            Debug.Log($"   -> HandAlignment current value: {cur}");

                            // Attempt to set to a safer/none value if an enum member named 'None' exists
                            var enumType = alignProp.PropertyType;
                            if (enumType != null && enumType.IsEnum)
                            {
                                var noneField = enumType.GetField("None");
                                try
                                {
                                    if (noneField != null)
                                    {
                                        var noneVal = noneField.GetValue(null);
                                        alignProp.SetValue(hcomp, noneVal, null);
                                        var after = alignProp.GetValue(hcomp, null);
                                        Debug.Log($"   -> Set HandAlignment to {after} for {panel.name}");
                                    }
                                    else
                                    {
                                        // No explicit 'None' member; try numeric 0 if safe
                                        var zeroVal = System.Enum.ToObject(enumType, 0);
                                        alignProp.SetValue(hcomp, zeroVal, null);
                                        var after2 = alignProp.GetValue(hcomp, null);
                                        Debug.Log($"   -> Set HandAlignment to {after2} (numeric 0) for {panel.name}");
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    Debug.LogWarning($"   -> failed setting HandAlignment: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch { }
                    // attempt to call InjectOptionalPointableElement if exists and parentGrabbable present
                    var injectPointable = handGrabType.GetMethod("InjectOptionalPointableElement");
                    if (injectPointable != null && parentGrabbable != null)
                    {
                        try
                        {
                            injectPointable.Invoke(hcomp, new object[] { parentGrabbable });
                            Debug.Log($"   -> Injected parent Grabbable into HandGrabInteractable for {panel.name}");
                        }
                        catch (System.Exception ex) { Debug.LogWarning($"   -> failed injecting pointable into HandGrab: {ex.Message}"); }
                    }
                }
            }

            // Inspect GrabInteractable
            if (grabInteractableType != null)
            {
                var gcomp = install.GetComponent(grabInteractableType);
                Debug.Log($" - GrabInteractable component: {(gcomp!=null?grabInteractableType.FullName:"(none)")}");
                if (gcomp != null)
                {
                    // Try to inject pointable element into GrabInteractable as well (some versions expose this)
                    var injectPoint = grabInteractableType.GetMethod("InjectOptionalPointableElement")
                                    ?? grabInteractableType.GetMethod("InjectOptionalPointable");
                    if (injectPoint != null && parentGrabbable != null)
                    {
                        try
                        {
                            injectPoint.Invoke(gcomp, new object[] { parentGrabbable });
                            Debug.Log($"   -> Injected parent Grabbable into GrabInteractable for {panel.name}");
                        }
                        catch (System.Exception ex) { Debug.LogWarning($"   -> failed injecting pointable into GrabInteractable: {ex.Message}"); }
                    }

                    // Additional fallback: look for a field/property named 'pointableElement' or similar and set it via reflection
                    var pField = grabInteractableType.GetField("pointableElement", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                 ?? grabInteractableType.GetField("_pointableElement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                 ?? grabInteractableType.GetField("m_PointableElement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (pField != null && parentGrabbable != null)
                    {
                        try
                        {
                            var cur = pField.GetValue(gcomp);
                            if (cur == null)
                            {
                                pField.SetValue(gcomp, parentGrabbable);
                                Debug.Log($"   -> Set pointable field '{pField.Name}' on GrabInteractable for {panel.name}");
                            }
                        }
                        catch (System.Exception ex) { Debug.LogWarning($"   -> failed setting pointable field on GrabInteractable: {ex.Message}"); }
                    }
                }
            }

            // Final re-log of installation child's components
            Debug.Log($" - Post-injection check for {panel.name}:");
            foreach (var comp in install.GetComponents<Component>())
            {
                Debug.Log($"   - {comp.GetType().FullName}");
            }
        }

        Debug.Log("PanelGrabVerifier: Verification pass complete");
    }
}
