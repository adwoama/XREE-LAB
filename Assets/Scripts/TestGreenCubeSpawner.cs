using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Spawns a single green cube in front of the camera with the Building-Block style
/// hierarchy: parent holds Rigidbody/BoxCollider/Grabbable, child holds
/// HandGrabInteractable + GrabInteractable. Uses reflection to avoid hard dependency
/// on the Interaction SDK types. Useful for quick grab tests.
/// </summary>
public class TestGreenCubeSpawner : MonoBehaviour
{
    public float distance = 1.2f;
    public Vector3 size = new Vector3(0.25f, 0.25f, 0.25f);

    void Start()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

    // Spawn slightly above the camera forward direction so it's not sunk into the floor
    Vector3 pos = cam.transform.position + cam.transform.forward * distance + Vector3.up * 0.35f;
        Quaternion rot = Quaternion.LookRotation(pos - cam.transform.position, Vector3.up);

        GameObject root = new GameObject("TestGreenCube");
        root.transform.position = pos;
        root.transform.rotation = rot;

        // visual child
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = size;
        var mr = visual.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Unlit/Color"));
        mr.material.color = Color.green;

        // leave child's BoxCollider so colliders exist in hierarchy

        // parent physics
        var parentCol = root.AddComponent<BoxCollider>();
        parentCol.size = size;
        parentCol.isTrigger = false;

        var rb = root.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.mass = 1f;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        // discover SDK types
        System.Type grabbableType = null;
        System.Type handGrabType = null;
        System.Type grabInteractableType = null;
        System.Collections.Generic.List<System.Type> handCandidates = new System.Collections.Generic.List<System.Type>();

        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (grabbableType == null && t.Name == "Grabbable") grabbableType = t;
                    if (grabInteractableType == null && t.Name == "GrabInteractable") grabInteractableType = t;
                    if (t.Name.Contains("HandGrab") && t.Name.Contains("Interactable")) handCandidates.Add(t);
                }
                if (grabbableType != null && handCandidates.Count>0 && grabInteractableType != null) break;
            }
            catch { }
        }

        if (handCandidates.Count > 0)
        {
            handGrabType = handCandidates.Find(x => x.Name == "HandGrabInteractable");
            if (handGrabType == null) handGrabType = handCandidates.Find(x => !x.Name.Contains("Touch")) ?? handCandidates[0];
        }

        // add Grabbable to parent
        if (grabbableType != null)
        {
            try
            {
                var g = root.GetComponent(grabbableType) ?? root.AddComponent(grabbableType);
                Debug.Log($"TestGreenCubeSpawner: Added Grabbable {grabbableType.FullName}");
                var injectRb = grabbableType.GetMethod("InjectOptionalRigidbody");
                if (injectRb != null)
                {
                    try { injectRb.Invoke(g, new object[] { rb }); } catch { }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"TestGreenCubeSpawner: failed to add Grabbable: {ex.Message}"); }
        }

        // installation child
        var install = new GameObject("HandGrabInstallationRoutine");
        install.transform.SetParent(root.transform, false);

        // add HandGrabInteractable
        if (handGrabType != null)
        {
            try
            {
                var h = install.GetComponent(handGrabType) ?? install.AddComponent(handGrabType);
                Debug.Log($"TestGreenCubeSpawner: Added HandGrabInteractable {handGrabType.FullName}");
                var injectRb = handGrabType.GetMethod("InjectRigidbody") ?? handGrabType.GetMethod("InjectRigidbody", new System.Type[] { typeof(Rigidbody) });
                if (injectRb != null) try { injectRb.Invoke(h, new object[] { rb }); } catch { }

                // inject colliders / bounds
                try
                {
                    var collidersList = new System.Collections.Generic.List<Collider>(root.GetComponentsInChildren<Collider>());
                    var injectAll = handGrabType.GetMethod("InjectAllHandGrabInteractable") ?? handGrabType.GetMethod("InjectAllTouchHandGrabInteractable");
                    Collider bounds = visual.GetComponent<Collider>() ?? parentCol;
                    if (injectAll != null && bounds != null)
                    {
                        try { injectAll.Invoke(h, new object[] { 0, rb, null, null }); } catch { }
                    }
                    var injectBounds = handGrabType.GetMethod("InjectBoundsCollider");
                    if (injectBounds != null && bounds != null) try { injectBounds.Invoke(h, new object[] { bounds }); } catch { }
                    var injectColl = handGrabType.GetMethod("InjectColliders");
                    if (injectColl != null) try { injectColl.Invoke(h, new object[] { collidersList }); } catch { }
                }
                catch { }

                // Extra defensive injections and fallbacks to set private backing fields early
                try
                {
                    var injOpt = handGrabType.GetMethod("InjectOptionalRigidbody") ?? handGrabType.GetMethod("InjectRigidbody");
                    if (injOpt != null) try { injOpt.Invoke(h, new object[] { rb }); } catch { }

                    var rbFld = handGrabType.GetField("_rigidbody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                               ?? handGrabType.GetField("rigidbody", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (rbFld != null) try { rbFld.SetValue(h, rb); } catch { }

                    var collArray = root.GetComponentsInChildren<Collider>();
                    // Try injector that accepts a List<Collider>
                    var injColl = handGrabType.GetMethod("InjectColliders");
                    if (injColl != null)
                    {
                        try
                        {
                            var collList = new System.Collections.Generic.List<Collider>(collArray);
                            injColl.Invoke(h, new object[] { collList });
                        }
                        catch { }
                    }

                    // If the backing field/property exists and expects an array, set it as an array.
                    var collFld = handGrabType.GetField("_colliders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                  ?? handGrabType.GetField("colliders", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (collFld != null)
                    {
                        try
                        {
                            if (collFld.FieldType.IsArray && collArray != null)
                            {
                                collFld.SetValue(h, collArray);
                            }
                            else if (typeof(System.Collections.IList).IsAssignableFrom(collFld.FieldType))
                            {
                                // try List<Collider>
                                var collList = new System.Collections.Generic.List<Collider>(collArray);
                                collFld.SetValue(h, collList);
                            }
                        }
                        catch { }
                    }

                    var bounds = visual.GetComponent<Collider>() ?? parentCol;
                    var injB = handGrabType.GetMethod("InjectBoundsCollider");
                    if (injB != null && bounds != null) try { injB.Invoke(h, new object[] { bounds }); } catch { }
                    var bFld = handGrabType.GetField("_boundsCollider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                               ?? handGrabType.GetField("boundsCollider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (bFld != null && bounds != null) try { bFld.SetValue(h, bounds); } catch { }
                }
                catch { }

                // set HandAlignment permissive
                try
                {
                    var alignProp = handGrabType.GetProperty("HandAlignment");
                    if (alignProp != null)
                    {
                        var enumType = alignProp.PropertyType;
                        object val = null;
                        if (enumType != null && enumType.IsEnum)
                        {
                            var noneField = enumType.GetField("None");
                            if (noneField != null) val = noneField.GetValue(null);
                            else val = System.Enum.ToObject(enumType, 0);
                            try { alignProp.SetValue(h, val, null); } catch { }
                        }
                    }
                }
                catch { }

                // Ensure a MovementProvider exists and is injected (MoveTowardsTargetProvider)
                try
                {
                    System.Type moveType = null;
                    foreach (var asm2 in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            foreach (var t2 in asm2.GetTypes())
                            {
                                if (t2.Name == "MoveTowardsTargetProvider") { moveType = t2; break; }
                            }
                            if (moveType != null) break;
                        }
                        catch { }
                    }
                    if (moveType != null)
                    {
                        var movement = install.GetComponent(moveType) ?? install.AddComponent(moveType);
                        var injMove = handGrabType.GetMethod("InjectOptionalMovementProvider");
                        if (injMove != null)
                        {
                            try { injMove.Invoke(h, new object[] { movement }); } catch { }
                        }
                        else
                        {
                            var mFld = handGrabType.GetField("_movementProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                       ?? handGrabType.GetField("movementProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (mFld != null) try { mFld.SetValue(h, movement); } catch { }
                        }
                    }
                }
                catch { }

                // Copy Pinch/Palm grabbing rules from an existing HandGrabInteractable in the scene (if present)
                try
                {
                    // Use Object.FindObjectOfType(Type) via reflection to locate an example component
                    var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectOfType", new System.Type[] { typeof(System.Type) });
                    object example = null;
                    if (findMethod != null)
                    {
                        try { example = findMethod.Invoke(null, new object[] { handGrabType }); } catch { example = null; }
                    }

                    System.Type grabbingRuleType = null;
                    if (example != null)
                    {
                        var pinchProp = handGrabType.GetProperty("PinchGrabRules");
                        var palmProp = handGrabType.GetProperty("PalmGrabRules");
                        var injPinch = handGrabType.GetMethod("InjectPinchGrabRules");
                        var injPalm = handGrabType.GetMethod("InjectPalmGrabRules");
                        if (pinchProp != null && injPinch != null)
                        {
                            try { var pinchVal = pinchProp.GetValue(example, null); injPinch.Invoke(h, new object[] { pinchVal }); } catch { }
                        }
                        if (palmProp != null && injPalm != null)
                        {
                            try { var palmVal = palmProp.GetValue(example, null); injPalm.Invoke(h, new object[] { palmVal }); } catch { }
                        }
                    }
                    else
                    {
                        // Fallback: try to pull GrabbingRule.DefaultPinchRule / DefaultPalmRule static fields
                        foreach (var asm2 in System.AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                foreach (var t2 in asm2.GetTypes())
                                {
                                    if (t2.Name == "GrabbingRule") { grabbingRuleType = t2; break; }
                                }
                                if (grabbingRuleType != null) break;
                            }
                            catch { }
                        }
                        if (grabbingRuleType != null)
                        {
                            try
                            {
                                var defaultPinchProp = grabbingRuleType.GetProperty("DefaultPinchRule", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                var defaultPalmProp = grabbingRuleType.GetProperty("DefaultPalmRule", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                var injPinch = handGrabType.GetMethod("InjectPinchGrabRules");
                                var injPalm = handGrabType.GetMethod("InjectPalmGrabRules");
                                if (defaultPinchProp != null && injPinch != null)
                                {
                                    try
                                    {
                                        var val = defaultPinchProp.GetValue(null, null);
                                        // Defensive: ensure thumb is Ignored (match building-block convention)
                                        try
                                        {
                                            // Find the FingerRequirement enum type
                                            System.Type fingerReqType = null;
                                            foreach (var a3 in System.AppDomain.CurrentDomain.GetAssemblies())
                                            {
                                                try
                                                {
                                                    fingerReqType = a3.GetType("Oculus.Interaction.GrabAPI.FingerRequirement")
                                                                ?? a3.GetType("Oculus.Interaction.Grab.FingerRequirement")
                                                                ?? a3.GetType("FingerRequirement");
                                                    if (fingerReqType != null) break;
                                                }
                                                catch { }
                                            }
                                            if (fingerReqType != null && val != null)
                                            {
                                                var thumbFld = grabbingRuleType.GetField("_thumbRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                if (thumbFld != null)
                                                {
                                                    var ignoredEnum = System.Enum.ToObject(fingerReqType, 0); // Ignored == 0
                                                    thumbFld.SetValue(val, ignoredEnum);
                                                }
                                            }
                                        }
                                        catch { }
                                        try { injPinch.Invoke(h, new object[] { val }); } catch { }
                                    }
                                    catch { }
                                }
                                if (defaultPalmProp != null && injPalm != null)
                                {
                                    try { var val = defaultPalmProp.GetValue(null, null); injPalm.Invoke(h, new object[] { val }); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch (System.Exception ex) { Debug.LogWarning($"TestGreenCubeSpawner: failed to add HandGrabInteractable: {ex.Message}"); }
        }

        // add GrabInteractable
        if (grabInteractableType != null)
        {
            try
            {
                var g = install.GetComponent(grabInteractableType) ?? install.AddComponent(grabInteractableType);
                Debug.Log($"TestGreenCubeSpawner: Added GrabInteractable {grabInteractableType.FullName}");
                var injectAll = grabInteractableType.GetMethod("InjectAllGrabInteractable");
                if (injectAll != null) try { injectAll.Invoke(g, new object[] { rb }); } catch { }
                else
                {
                    var inject = grabInteractableType.GetMethod("InjectRigidbody");
                    if (inject != null) try { inject.Invoke(g, new object[] { rb }); } catch { }
                }
                // try to set grab source to closest point for robustness
                var useClosest = grabInteractableType.GetProperty("UseClosestPointAsGrabSource") ?? grabInteractableType.GetProperty("useClosestPointAsGrabSource");
                if (useClosest != null)
                {
                    try { useClosest.SetValue(g, true, null); } catch { }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"TestGreenCubeSpawner: failed to add GrabInteractable: {ex.Message}"); }

                // Defensive: ensure private _rigidbody/backing fields are set and colliders injected
                try
                {
                    var gcomp = install.GetComponent(grabInteractableType);
                    if (gcomp != null)
                    {
                        var rbFldG = grabInteractableType.GetField("_rigidbody", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                     ?? grabInteractableType.GetField("rigidbody", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (rbFldG != null) try { rbFldG.SetValue(gcomp, rb); } catch { }

                        var collArray = root.GetComponentsInChildren<Collider>();
                        var injCollG = grabInteractableType.GetMethod("InjectColliders");
                        if (injCollG != null)
                        {
                            try
                            {
                                var collList = new System.Collections.Generic.List<Collider>(collArray);
                                injCollG.Invoke(gcomp, new object[] { collList });
                            }
                            catch { }
                        }
                        var collFldG = grabInteractableType.GetField("_colliders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                       ?? grabInteractableType.GetField("colliders", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (collFldG != null)
                        {
                            try
                            {
                                if (collFldG.FieldType.IsArray && collArray != null)
                                {
                                    collFldG.SetValue(gcomp, collArray);
                                }
                                else if (typeof(System.Collections.IList).IsAssignableFrom(collFldG.FieldType))
                                {
                                    var collList = new System.Collections.Generic.List<Collider>(collArray);
                                    collFldG.SetValue(gcomp, collList);
                                }
                            }
                            catch { }
                        }

                        // attempt to set optional pointable element to parent grabbable
                        var injectPoint = grabInteractableType.GetMethod("InjectOptionalPointableElement");
                        if (injectPoint != null)
                        {
                            var parentG = root.GetComponent(grabbableType);
                            if (parentG != null) try { injectPoint.Invoke(gcomp, new object[] { parentG }); } catch { }
                        }
                    }
                }
                catch { }
        }

        Debug.Log("TestGreenCubeSpawner: Spawned TestGreenCube. Try grabbing it now.");

        // Detailed diagnostics: log key runtime fields and private/backing fields
        try
        {
            var parentGrabbable = (grabbableType != null) ? root.GetComponent(grabbableType) : null;
            var handComp = (handGrabType != null) ? install.GetComponent(handGrabType) : null;
            var grabComp = (grabInteractableType != null) ? install.GetComponent(grabInteractableType) : null;

            Debug.Log($"TestGreenCubeSpawner: root pos={root.transform.position}, rb.isKinematic={rb.isKinematic}, rb.mass={rb.mass}");
            var allCols = root.GetComponentsInChildren<Collider>();
            Debug.Log($"TestGreenCubeSpawner: {allCols.Length} colliders in Rigidbody hierarchy");

            if (parentGrabbable != null) Debug.Log($"TestGreenCubeSpawner: parent Grabbable present: {parentGrabbable.GetType().FullName}");
            else Debug.Log("TestGreenCubeSpawner: parent Grabbable NOT present");

            if (handComp != null) Debug.Log($"TestGreenCubeSpawner: HandGrabInteractable present: {handComp.GetType().FullName}");
            else Debug.Log("TestGreenCubeSpawner: HandGrabInteractable NOT present");

            if (grabComp != null) Debug.Log($"TestGreenCubeSpawner: GrabInteractable present: {grabComp.GetType().FullName}");
            else Debug.Log("TestGreenCubeSpawner: GrabInteractable NOT present");

            // Helper to log private fields
            System.Action<object, string[]> dumpFields = (obj, names) =>
            {
                if (obj == null) return;
                var t = obj.GetType();
                foreach (var n in names)
                {
                    try
                    {
                        var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        if (f != null)
                        {
                            var v = f.GetValue(obj);
                            Debug.Log($"TestGreenCubeSpawner: {t.Name}.{n} = {v}");
                        }
                        else
                        {
                            var p = t.GetProperty(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                            if (p != null)
                            {
                                var pv = p.GetValue(obj, null);
                                Debug.Log($"TestGreenCubeSpawner: {t.Name}.{n} (prop) = {pv}");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"TestGreenCubeSpawner: failed to read field {n} on {obj.GetType().Name}: {ex.Message}");
                    }
                }
            };

            // Dump common backing fields
            dumpFields(parentGrabbable, new string[] { "_rigidbody", "_pointable", "_colliders" });
            dumpFields(handComp, new string[] { "_rigidbody", "_boundsCollider", "_colliders", "_handAligment", "_movementProvider" });
            dumpFields(grabComp, new string[] { "_rigidbody", "_colliders" });

            // Extra: log the resolved Pinch/Palm grabbing rule values (thumb/index/middle/ring/pinky)
            try
            {
                if (handComp != null)
                {
                    var t = handComp.GetType();
                    var pinchProp = t.GetProperty("PinchGrabRules");
                    var palmProp = t.GetProperty("PalmGrabRules");
                    System.Type grabbingRuleType = null;
                    if (pinchProp != null)
                    {
                        try
                        {
                            var pinchVal = pinchProp.GetValue(handComp, null);
                            if (pinchVal != null)
                            {
                                var grType = pinchVal.GetType();
                                grabbingRuleType = grType;
                                var fingerFields = new string[] { "_thumbRequirement", "_indexRequirement", "_middleRequirement", "_ringRequirement", "_pinkyRequirement" };
                                var parts = new System.Collections.Generic.List<string>();
                                foreach (var f in fingerFields)
                                {
                                    try
                                    {
                                        var fld = grType.GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fld != null)
                                        {
                                            var v = fld.GetValue(pinchVal);
                                            parts.Add($"{f.Replace("_", "")}={v}");
                                        }
                                    }
                                    catch { }
                                }
                                Debug.Log($"TestGreenCubeSpawner: PinchGrabRules -> {string.Join(", ", parts.ToArray())}");
                            }
                        }
                        catch { }
                    }
                    if (palmProp != null)
                    {
                        try
                        {
                            var palmVal = palmProp.GetValue(handComp, null);
                            if (palmVal != null)
                            {
                                var grType = palmVal.GetType();
                                var fingerFields = new string[] { "_thumbRequirement", "_indexRequirement", "_middleRequirement", "_ringRequirement", "_pinkyRequirement" };
                                var parts = new System.Collections.Generic.List<string>();
                                foreach (var f in fingerFields)
                                {
                                    try
                                    {
                                        var fld = grType.GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (fld != null)
                                        {
                                            var v = fld.GetValue(palmVal);
                                            parts.Add($"{f.Replace("_", "")}={v}");
                                        }
                                    }
                                    catch { }
                                }
                                Debug.Log($"TestGreenCubeSpawner: PalmGrabRules -> {string.Join(", ", parts.ToArray())}");
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Start a coroutine to re-inject grab rules one frame later to avoid Start()/ordering races
            try
            {
                StartCoroutine(ReinjectHandGrabRulesNextFrame(install, handGrabType));
            }
            catch { }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"TestGreenCubeSpawner: diagnostic logging failed: {ex.Message}");
        }
    }

    private System.Collections.IEnumerator ReinjectHandGrabRulesNextFrame(GameObject install, System.Type handGrabType)
    {
        yield return null; // wait one frame so HandGrabInteractable.Start() has run

        if (handGrabType == null || install == null) yield break;
        var handComp = install.GetComponent(handGrabType);
        if (handComp == null) yield break;

        // Find grabbing rule type and finger enum
        System.Type grabbingRuleType = null;
        System.Type fingerReqType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (grabbingRuleType == null && t.Name == "GrabbingRule") grabbingRuleType = t;
                    if (fingerReqType == null && t.Name == "FingerRequirement") fingerReqType = t;
                    if (grabbingRuleType != null && fingerReqType != null) break;
                }
                if (grabbingRuleType != null && fingerReqType != null) break;
            }
            catch { }
        }

        // Build explicit rules: pinch = thumb=Ignored, index=Optional, middle=Optional, ring=Ignored, pinky=Ignored
        object pinchRule = null;
        object palmRule = null;
        if (grabbingRuleType != null && fingerReqType != null)
        {
            pinchRule = BuildGrabbingRuleInstance(grabbingRuleType, fingerReqType,
                thumb: 0, index: 1, middle: 1, ring: 0, pinky: 0, unselectMode: 0);
            // palm: thumb=Optional(1), index=Required(2), middle=Required(2), ring=Required(2), pinky=Optional(1)
            palmRule = BuildGrabbingRuleInstance(grabbingRuleType, fingerReqType,
                thumb: 1, index: 2, middle: 2, ring: 2, pinky: 1, unselectMode: 0);
        }

        try
        {
            var injPinch = handGrabType.GetMethod("InjectPinchGrabRules");
            var injPalm = handGrabType.GetMethod("InjectPalmGrabRules");
            if (injPinch != null && pinchRule != null)
            {
                try { injPinch.Invoke(handComp, new object[] { pinchRule }); Debug.Log("TestGreenCubeSpawner: Re-injected explicit PinchGrabRules"); } catch (System.Exception e) { Debug.LogWarning("TestGreenCubeSpawner: failed reinject pinch: " + e.Message); }
            }
            if (injPalm != null && palmRule != null)
            {
                try { injPalm.Invoke(handComp, new object[] { palmRule }); Debug.Log("TestGreenCubeSpawner: Re-injected explicit PalmGrabRules"); } catch (System.Exception e) { Debug.LogWarning("TestGreenCubeSpawner: failed reinject palm: " + e.Message); }
            }

            // If injectors not present, set backing fields directly
            if (injPinch == null && pinchRule != null)
            {
                var fld = handGrabType.GetField("_pinchGrabRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null) try { fld.SetValue(handComp, pinchRule); Debug.Log("TestGreenCubeSpawner: Set _pinchGrabRules backing field directly"); } catch { }
            }
            if (injPalm == null && palmRule != null)
            {
                var fld = handGrabType.GetField("_palmGrabRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null) try { fld.SetValue(handComp, palmRule); Debug.Log("TestGreenCubeSpawner: Set _palmGrabRules backing field directly"); } catch { }
            }
        }
        catch { }
    }

    private object BuildGrabbingRuleInstance(System.Type grabbingRuleType, System.Type fingerReqType,
        int thumb, int index, int middle, int ring, int pinky, int unselectMode)
    {
        try
        {
            // Create boxed struct instance (default) and set private fields
            object instance = System.Activator.CreateInstance(grabbingRuleType);
            var thumbFld = grabbingRuleType.GetField("_thumbRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var indexFld = grabbingRuleType.GetField("_indexRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var middleFld = grabbingRuleType.GetField("_middleRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ringFld = grabbingRuleType.GetField("_ringRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pinkyFld = grabbingRuleType.GetField("_pinkyRequirement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var unFld = grabbingRuleType.GetField("_unselectMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (thumbFld != null) thumbFld.SetValue(instance, System.Enum.ToObject(fingerReqType, thumb));
            if (indexFld != null) indexFld.SetValue(instance, System.Enum.ToObject(fingerReqType, index));
            if (middleFld != null) middleFld.SetValue(instance, System.Enum.ToObject(fingerReqType, middle));
            if (ringFld != null) ringFld.SetValue(instance, System.Enum.ToObject(fingerReqType, ring));
            if (pinkyFld != null) pinkyFld.SetValue(instance, System.Enum.ToObject(fingerReqType, pinky));
            if (unFld != null) unFld.SetValue(instance, System.Enum.ToObject(unFld.FieldType, unselectMode));

            return instance;
        }
        catch { return null; }
    }
}
