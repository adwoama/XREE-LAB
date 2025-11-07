using System.Collections;
using UnityEngine;

/// <summary>
/// Simple WaveformManager: instantiates a configured prefab for each channel (preferred),
/// or creates a lightweight fallback panel when no prefab is available.
/// This version focuses on reliable runtime instantiation and clear logging so you can
/// diagnose missing panels in builds.
/// </summary>
public class WaveformManager : MonoBehaviour
{
    [Tooltip("Number of panels to spawn")]
    public int channels = 4;
    [Tooltip("Distance in meters in front of the camera")]
    public float distance = 1.2f;
    public float verticalOffset = 1.1f;
    public float spacing = 0.65f;

    [Tooltip("Optional: assign a prefab that contains the configured panel (recommended)")]
    public GameObject panelPrefab;

    public Vector2 panelSize = new Vector2(0.9f, 0.6f);

    void Start()
    {
        // Diagnostic: report whether the inspector-assigned prefab looks like a scene object
        if (panelPrefab != null)
        {
            bool sceneValid = panelPrefab.scene.IsValid();
            Debug.Log($"WaveformManager: panelPrefab assigned='{panelPrefab.name}', sceneIsValid={sceneValid}");
            if (sceneValid)
                Debug.LogWarning("WaveformManager: the assigned panelPrefab appears to be a Scene object. Make sure you assign the prefab asset from the Project view (Assets) so the reference persists into builds.");
        }

        StartCoroutine(InitAndSpawnPanels());
    }

    private IEnumerator InitAndSpawnPanels()
    {
        // Wait briefly for Camera/main to exist (XR camera may be created after scene start)
        float start = Time.realtimeSinceStartup;
        Camera cam = Camera.main;
        while (cam == null && Time.realtimeSinceStartup - start < 5f)
        {
            var cams = GameObject.FindObjectsOfType<Camera>();
            if (cams != null && cams.Length > 0) cam = cams[0];
            if (cam == null) yield return null; else break;
        }

        if (cam == null)
        {
            Debug.LogWarning("WaveformManager: No camera found after waiting — panels will not be instantiated.");
            yield break;
        }

        // Try load from Resources if inspector field not set (optional convenience)
        if (panelPrefab == null)
        {
            var res = Resources.Load<GameObject>("Prefabs/Panel_CH") ?? Resources.Load<GameObject>("Panel_CH");
            if (res != null)
            {
                panelPrefab = res;
                Debug.Log("WaveformManager: Loaded Panel_CH prefab from Resources.");
            }
            else
            {
                Debug.Log("WaveformManager: No panelPrefab assigned; will use procedural fallback.");
            }
        }

        Vector3 center = cam.transform.position + cam.transform.forward * distance + Vector3.up * verticalOffset;
        float totalWidth = (channels - 1) * spacing;
        Vector3 leftMost = center - cam.transform.right * (totalWidth / 2f);

        Debug.Log($"WaveformManager: Spawning {channels} panels. Prefab: {(panelPrefab!=null?panelPrefab.name:"(none)")}");

        for (int i = 0; i < channels; i++)
        {
            Vector3 pos = leftMost + cam.transform.right * (i * spacing) + cam.transform.forward * (i * -0.005f);
            Quaternion rot = Quaternion.LookRotation(pos - cam.transform.position, Vector3.up);

            GameObject panel = null;
            if (panelPrefab != null)
            {
                panel = Instantiate(panelPrefab);
                panel.name = $"Panel_CH{i+1}";
                panel.transform.position = pos;
                panel.transform.rotation = rot;
                panel.SetActive(true);
                Debug.Log($"WaveformManager: Instantiated prefab {panel.name} at {pos}");
                LogPrefabDiagnostics(panel, i+1);
            }
            else
            {
                panel = CreateSimplePanel($"Panel_CH{i+1}", pos, rot, panelSize.x, panelSize.y);
                Debug.Log($"WaveformManager: Created procedural panel {panel.name} at {pos}");
                LogPrefabDiagnostics(panel, i+1);
            }

            var wf = panel.GetComponent<WaveformPanel>();
            if (wf != null)
            {
                wf.SetLabel("CH" + (i+1));
                wf.frequency = 0.5f + i * 0.7f;
                wf.amplitude = 0.6f;
                wf.noise = 0.08f * (i+1);
                wf.SetColor(Color.HSVToRGB(i/(float)channels, 0.9f, 0.9f));
            }

            // yield a frame between spawns to avoid hiccups on slow devices
            yield return null;
        }
    }

    private void LogPrefabDiagnostics(GameObject panel, int channelIdx)
    {
        if (panel == null) return;
        Debug.Log($"WaveformManager: [Diag] Panel {panel.name} activeInHierarchy={panel.activeInHierarchy}");
        // list cameras
        var cams = GameObject.FindObjectsOfType<Camera>();
        Debug.Log($"WaveformManager: [Diag] Cameras found={cams.Length}");
        for (int c = 0; c < cams.Length; c++)
        {
            var cam = cams[c];
            Debug.Log($"WaveformManager: [Diag] Camera[{c}] name={cam.name} tag={cam.tag} enabled={cam.enabled} cullingMask={cam.cullingMask} transform={cam.transform.position}");
        }

        // renderer and material diagnostics
        var rends = panel.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"WaveformManager: [Diag] Panel {panel.name} renderers={rends.Length}");
        foreach (var r in rends)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            Debug.Log($"WaveformManager: [Diag] Renderer={r.gameObject.name} enabled={r.enabled} bounds={r.bounds}");
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) Debug.Log($"WaveformManager: [Diag]  material[{m}]=null");
                else Debug.Log($"WaveformManager: [Diag]  material[{m}] name={mat.name} shader={mat.shader?.name}");
            }
        }
    }

    private GameObject CreateSimplePanel(string name, Vector3 pos, Quaternion rot, float w, float h)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.rotation = rot;

        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Background";
        bg.transform.SetParent(go.transform, false);
        bg.transform.localScale = new Vector3(w, h, 1f);
        bg.transform.localPosition = Vector3.zero;

        // try to use URP Unlit if available
        Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        var mat = new Material(s != null ? s : Shader.Find("Standard"));
        mat.color = new Color(0f,0f,0f,0.35f);
        var mr = bg.GetComponent<MeshRenderer>();
        mr.material = mat;

        // cleanup mesh collider and add small box collider
        var meshCol = bg.GetComponent<MeshCollider>();
        if (meshCol != null) DestroyImmediate(meshCol);
        var bgBox = bg.AddComponent<BoxCollider>();
        bgBox.size = new Vector3(1f,1f,0.01f);

        // add simple waveform component (assumes WaveformPanel script exists)
        var wf = go.AddComponent<WaveformPanel>();
        wf.width = w;
        wf.height = h * 0.85f;

        // LineRenderer
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = wf.resolution > 0 ? wf.resolution : 128;
        lr.widthMultiplier = 0.01f;
        lr.material = mat;
        lr.startColor = lr.endColor = Color.green;

        // basic physics for grabbing tests (may not include full SDK wiring)
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(w, h, 0.02f);
        col.isTrigger = false;
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.isKinematic = false; rb.mass = 1f; rb.constraints = RigidbodyConstraints.FreezeRotation;

        return go;
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
                try { injPinch.Invoke(handComp, new object[] { pinchRule }); Debug.Log("WaveformManager: Re-injected explicit PinchGrabRules"); } catch (System.Exception e) { Debug.LogWarning("WaveformManager: failed reinject pinch: " + e.Message); }
            }
            if (injPalm != null && palmRule != null)
            {
                try { injPalm.Invoke(handComp, new object[] { palmRule }); Debug.Log("WaveformManager: Re-injected explicit PalmGrabRules"); } catch (System.Exception e) { Debug.LogWarning("WaveformManager: failed reinject palm: " + e.Message); }
            }

            // If injectors not present, set backing fields directly
            if (injPinch == null && pinchRule != null)
            {
                var fld = handGrabType.GetField("_pinchGrabRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null) try { fld.SetValue(handComp, pinchRule); Debug.Log("WaveformManager: Set _pinchGrabRules backing field directly"); } catch { }
            }
            if (injPalm == null && palmRule != null)
            {
                var fld = handGrabType.GetField("_palmGrabRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null) try { fld.SetValue(handComp, palmRule); Debug.Log("WaveformManager: Set _palmGrabRules backing field directly"); } catch { }
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

    void CreateWindowFrame(GameObject parent, float width, float height, int index)
    {
        // material for frame and handle
        var frameMat = new Material(Shader.Find("Unlit/Color"));
        frameMat.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        var handleMat = new Material(Shader.Find("Unlit/Color"));
        handleMat.color = Color.Lerp(Color.yellow, Color.white, 0.3f);

        float edgeThickness = 0.02f;

        // create four edge quads (left, right, top, bottom)
        void AddEdge(string name, Vector3 localPos, Vector3 localScale)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Quad);
            e.name = name;
            e.transform.SetParent(parent.transform, false);
            e.transform.localPosition = localPos + new Vector3(0f, 0f, -0.001f);
            e.transform.localRotation = Quaternion.identity;
            e.transform.localScale = localScale;
            var mr = e.GetComponent<MeshRenderer>();
            mr.material = frameMat;
            // small collider to allow grab targeting on edges as well
            var c = e.GetComponent<MeshCollider>();
            if (c != null) DestroyImmediate(c);
            var col = e.AddComponent<BoxCollider>();
            col.size = new Vector3(localScale.x, localScale.y, 0.01f);
        }

        AddEdge("Edge_Left", new Vector3(-width/2f + edgeThickness/2f, 0f, 0f), new Vector3(edgeThickness, height, 1f));
        AddEdge("Edge_Right", new Vector3(width/2f - edgeThickness/2f, 0f, 0f), new Vector3(edgeThickness, height, 1f));
        AddEdge("Edge_Top", new Vector3(0f, height/2f - edgeThickness/2f, 0f), new Vector3(width, edgeThickness, 1f));
        AddEdge("Edge_Bottom", new Vector3(0f, -height/2f + edgeThickness/2f, 0f), new Vector3(width, edgeThickness, 1f));

        // grab handle at top center
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        handle.name = "Grab_Handle";
        handle.transform.SetParent(parent.transform, false);
        handle.transform.localScale = new Vector3(width * 0.22f, edgeThickness * 1.2f, 0.02f);
        handle.transform.localPosition = new Vector3(0f, height/2f + edgeThickness*0.6f, -0.005f);
        var hm = handle.GetComponent<MeshRenderer>();
        hm.material = handleMat;
        // leave handle collider for grabbing
    }
}
