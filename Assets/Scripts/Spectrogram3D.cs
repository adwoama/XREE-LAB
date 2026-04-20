using System.Collections.Generic;
using UnityEngine;
using System;

public class Spectrogram3D : MonoBehaviour
{
    [Header("Spectrogram Settings")]
    [SerializeField] private GameObject voxelPrefab; // Prefab for individual voxels/bars
    [SerializeField] private Transform voxelParent; // Parent object for voxels
    [SerializeField] private Vector3 voxelSpacing = new Vector3(0.1f, 0.1f, 0.1f); // Spacing between voxels

    [Header("Appearance Settings")]
    [SerializeField] private Gradient colorGradient; // Gradient for voxel colors
    [SerializeField] private float maxVoxelHeight = 5f; // Maximum height of voxels

    private List<List<GameObject>> voxelGrid = new List<List<GameObject>>();

    [Header("Debug/Test Settings")]
    [SerializeField] private bool testMode = false; // Toggle for test mode
    [SerializeField] private int testTimeSteps = 10; // Number of time steps for test data
    [SerializeField] private int testFrequencyBins = 10; // Number of frequency bins for test data
    [SerializeField] private float testFrequency = 1f; // Frequency for sinusoidal test data
    [Tooltip("Multiplier applied to the inspector maxVoxelHeight to allow slightly taller debug visualization.")]
    public float heightMultiplier = 2.0f;
    [SerializeField] private bool usePrimitiveDebug = false; // Use a clean primitive cube for debugging

    private float[,] preallocatedData; // Preallocated array for data
    private int currentTimeSteps = 0;
    private int currentFrequencyBins = 0;

    private Queue<GameObject> voxelPool = new Queue<GameObject>(); // Object pooling for voxels

    private Color[] precomputedColors; // Precomputed gradient colors
    
    // Debug single-voxel reference and animation timer
    private GameObject debugVoxel;
    private Vector3 debugBaseScale = new Vector3(0.05f, 0.05f, 0.05f);
    private float testTimer = 0f;
    private Renderer debugRenderer;
    private MaterialPropertyBlock debugMPB;
    private float lastAnimatedValue = -1f;
    private Transform debugRenderableTransform;
    private Vector3 debugRenderableBaseLocalScale;
    // Mesh-instance fields for prefabs where transform-scaling doesn't visibly
    // alter the rendered geometry. We modify mesh vertices directly in that
    // case so height changes are visible.
    private MeshFilter debugMeshFilter;
    private Mesh debugInstancedMesh;
    private Vector3[] debugBaseVertices;
    private Vector3[] debugModifiedVertices;
    private float debugMeshBaseMinY;
    private float debugMeshBaseHeight;
    private BoxCollider debugBoxCollider;
    private Vector3 debugBoxBaseSize;
    private Vector3 debugBoxBaseCenter;
    private bool debugUpdateLogged = false;
    private int debugLogCount = 0;
    private int debugFrameCounter = 0;
    private int debugNormalsUpdateFrameInterval = 30;

    private void Start()
    {
        // Disable the default MeshRenderer on this GameObject at runtime so
        // the scene's placeholder cube is not visible and we only show the
        // script-generated voxel. Enable the MeshRenderer in the Editor and
        // let the script hide it at runtime — this avoids accidental
        // disabling in the prefab/scene that would make instantiated voxels
        // inherit a disabled renderer state.
        MeshRenderer defaultRenderer = GetComponent<MeshRenderer>();
        if (defaultRenderer != null)
        {
            defaultRenderer.enabled = false;
        }

        // Generate a single static voxel for debugging
        GenerateSingleVoxel();

        // Log current state to help debug why Update may not run or the
        // voxel may not animate. This log is printed once at Start.
        Debug.Log($"Spectrogram3D.Start: testMode={testMode}, debugVoxelAssigned={(debugVoxel!=null)}, debugRendererAssigned={(debugRenderer!=null)}, testFrequency={testFrequency}");
    }

    /// <summary>
    /// Generates a single voxel for debugging purposes.
    /// </summary>
    private void GenerateSingleVoxel()
    {
        GameObject singleVoxel;
        if (usePrimitiveDebug)
        {
            // Create a clean primitive cube to avoid prefab-driven overrides
            singleVoxel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            singleVoxel.transform.SetParent(voxelParent, false);
        }
        else
        {
            singleVoxel = Instantiate(voxelPrefab, voxelParent);
        }

        // Adjust voxel size and position
        singleVoxel.transform.localPosition = Vector3.zero;

        // Ensure the correct component is being scaled
        Transform voxelTransform = singleVoxel.transform;
        voxelTransform.localScale = debugBaseScale; // Smaller scale for debugging

        // Keep a reference for lightweight animation (1x1 grid)
        debugVoxel = singleVoxel;

        // Locate the transform that actually contains the visible mesh
        // (some prefabs place the MeshFilter/Renderer on a child). Cache
        // that child's localScale so we can scale the visible mesh directly.
        if (singleVoxel != null)
        {
            Renderer childRenderer = singleVoxel.GetComponentInChildren<Renderer>();
            if (childRenderer != null)
            {
                debugRenderableTransform = childRenderer.transform;
                debugRenderableBaseLocalScale = debugRenderableTransform.localScale;
            }
            else
            {
                debugRenderableTransform = singleVoxel.transform;
                debugRenderableBaseLocalScale = singleVoxel.transform.localScale;
            }

            // If this visible mesh comes from a prefab MeshFilter, create
            // an instance of the mesh so we can edit vertices directly
            // (this avoids modifying the shared mesh asset).
            debugMeshFilter = singleVoxel.GetComponentInChildren<MeshFilter>();
            if (debugMeshFilter != null && debugMeshFilter.sharedMesh != null)
            {
                debugInstancedMesh = Instantiate(debugMeshFilter.sharedMesh);
                debugMeshFilter.mesh = debugInstancedMesh; // assign instance
                debugBaseVertices = debugInstancedMesh.vertices;
                debugModifiedVertices = new Vector3[debugBaseVertices.Length];
                debugInstancedMesh.MarkDynamic();

                // compute mesh local-space min/max Y
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                foreach (var v in debugBaseVertices)
                {
                    if (v.y < minY) minY = v.y;
                    if (v.y > maxY) maxY = v.y;
                }
                debugMeshBaseMinY = minY;
                debugMeshBaseHeight = Mathf.Max(0.0001f, maxY - minY);

                // find a BoxCollider on the mesh root (if present) to update
                debugBoxCollider = singleVoxel.GetComponentInChildren<BoxCollider>();
                if (debugBoxCollider != null)
                {
                    debugBoxBaseSize = debugBoxCollider.size;
                    debugBoxBaseCenter = debugBoxCollider.center;
                }
            }
        }

        // Apply color from the gradient
        // Prefer the visible child renderer if present.
        debugRenderer = singleVoxel.GetComponentInChildren<Renderer>();
        if (debugRenderer != null)
        {
            // Ensure the instantiated voxel's renderer is enabled regardless
            // of the prefab/scene state so we can see it while debugging.
            debugRenderer.enabled = true;

            // Create a MaterialPropertyBlock and apply an initial color.
            // Using a MPB avoids instancing or modifying shared materials,
            // reducing memory and GC pressure.
            debugMPB = new MaterialPropertyBlock();
            debugMPB.SetColor("_Color", colorGradient.Evaluate(0.5f));
            debugRenderer.SetPropertyBlock(debugMPB);
        }

        // Ensure the voxel does not participate in physics (prevents
        // unexpected movement when transforms are modified elsewhere).
        Rigidbody rb = singleVoxel.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = singleVoxel.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
    }

    private void Update()
    {
        // Lightweight test animation for the single voxel when testMode is enabled.
        if (!testMode)
        {
            if (!debugUpdateLogged)
            {
                Debug.Log("Spectrogram3D.Update: testMode is false, skipping animation.");
                debugUpdateLogged = true;
            }
            return;
        }

        if (debugVoxel == null)
        {
            Debug.LogWarning("Spectrogram3D.Update: debugVoxel is null, cannot animate.");
            return;
        }

        // Use Time.time to drive the test signal so it runs reliably even if
        // frame timing varies. Use the full sine wave (not absolute) so the
        // signal goes negative as well — map it to [0,1] to get a symmetric
        // up/down oscillation.
        float t = Time.time * Mathf.Max(0.0001f, testFrequency);
        float raw = Mathf.Sin(t); // -1..1
        float normalized = (raw + 1f) * 0.5f; // 0..1

        // If we created an instanced mesh for the prefab, modify the mesh
        // vertices directly so the visual geometry changes. Otherwise fall
        // back to scaling the renderable transform.
        // Use the same `normalized` value for color so color correlates with height.
        float minScale = 0.5f; // relative shrink factor
        float maxScale = 1.5f; // relative grow factor
        float heightScaleFactor = Mathf.Lerp(minScale, maxScale, normalized);

        if (debugInstancedMesh != null && debugBaseVertices != null)
        {
            // Compute a desired visual height in local mesh units using
            // inspector `maxVoxelHeight` as the maximum. This maps the
            // normalized [0..1] signal to a concrete desired height and
            // prevents runaway expansion.
            float desiredHeight = Mathf.Clamp(normalized * maxVoxelHeight * heightMultiplier, 0f, maxVoxelHeight * heightMultiplier);
            // Ensure a tiny minimum so zero height doesn't collapse mesh
            desiredHeight = Mathf.Max(desiredHeight, debugMeshBaseHeight * 0.05f);

            for (int i = 0; i < debugBaseVertices.Length; i++)
            {
                // Preserve X/Z, remap Y proportionally between mesh min and desiredHeight.
                float ratio = (debugBaseVertices[i].y - debugMeshBaseMinY) / debugMeshBaseHeight;
                ratio = Mathf.Clamp01(ratio);
                float newYLocal = debugMeshBaseMinY + ratio * desiredHeight;
                if (float.IsNaN(newYLocal) || float.IsInfinity(newYLocal)) newYLocal = debugBaseVertices[i].y;
                debugModifiedVertices[i].x = debugBaseVertices[i].x;
                debugModifiedVertices[i].y = newYLocal;
                debugModifiedVertices[i].z = debugBaseVertices[i].z;
            }

            debugInstancedMesh.vertices = debugModifiedVertices;
            debugInstancedMesh.RecalculateBounds();

            // Recalculate normals less frequently to reduce CPU usage.
            debugFrameCounter++;
            if (debugFrameCounter % debugNormalsUpdateFrameInterval == 0)
            {
                debugInstancedMesh.RecalculateNormals();
            }

            // NOTE: skip updating colliders here in debug mode to avoid
            // introducing physics/collider-driven movement. If necessary,
            // we can update colliders later with a safe mapping.
        }
        else
        {
            // Scale the renderer transform (if present) rather than the root
            // object — this fixes prefabs where the visible mesh lives in a
            // child transform that was not being scaled.
            Vector3 s = debugRenderableBaseLocalScale;
            s.y = debugRenderableBaseLocalScale.y * heightScaleFactor;
            if (debugRenderableTransform != null)
            {
                debugRenderableTransform.localScale = s;
            }
            else if (debugVoxel != null)
            {
                debugVoxel.transform.localScale = s;
            }
        }

        // Update color via MaterialPropertyBlock (preferred) and also set
        // material color as a fallback so shaders that don't use the common
        // property names still show a visible change during debugging.
        Color c = colorGradient.Evaluate(normalized);
        if (debugRenderer != null)
        {
            if (debugMPB == null) debugMPB = new MaterialPropertyBlock();
            debugMPB.SetColor("_Color", c);
            debugMPB.SetColor("_BaseColor", c);
            debugRenderer.SetPropertyBlock(debugMPB);

            // Fallback: set instance material color (this will create a
            // material instance on first access; acceptable for debug).
            try
            {
                debugRenderer.material.color = c;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Spectrogram3D: failed to set material.color fallback: {e.Message}");
            }
        }

        // Debug: log scale and renderer bounds to determine whether the
        // transform scale change is taking effect or being overridden.
        if (debugLogCount < 20)
        {
            float localScaleY = (debugRenderableTransform != null) ? debugRenderableTransform.localScale.y : (debugVoxel != null ? debugVoxel.transform.localScale.y : 0f);
            float lossyScaleY = (debugRenderableTransform != null) ? debugRenderableTransform.lossyScale.y : (debugVoxel != null ? debugVoxel.transform.lossyScale.y : 0f);
            float rendererBoundsY = 0f;
            if (debugRenderer != null)
            {
                try { rendererBoundsY = debugRenderer.bounds.size.y; } catch { rendererBoundsY = 0f; }
            }
            float meshLocalBoundsY = 0f;
            if (debugInstancedMesh != null)
            {
                meshLocalBoundsY = debugInstancedMesh.bounds.size.y;
            }
            Debug.Log($"Spectrogram3D.DebugScale: normalized={normalized:F3}, localScaleY={localScaleY:F4}, lossyScaleY={lossyScaleY:F4}, rendererBoundsY={rendererBoundsY:F4}, meshLocalBoundsY={meshLocalBoundsY:F4}");
            debugLogCount++;
        }

        lastAnimatedValue = normalized;
    }

    /// <summary>
    /// Initializes the voxel grid with the specified dimensions.
    /// </summary>
    /// <param name="timeSteps">Number of time steps.</param>
    /// <param name="frequencyBins">Number of frequency bins.</param>
    private void InitializeVoxelGrid(int timeSteps, int frequencyBins)
    {
        currentTimeSteps = timeSteps;
        currentFrequencyBins = frequencyBins;
        preallocatedData = new float[timeSteps, frequencyBins];

        AdjustVoxelGrid(timeSteps, frequencyBins);
    }

    /// <summary>
    /// Sets the spectrogram data and updates the 3D grid.
    /// </summary>
    /// <param name="data">2D array of amplitudes (time x frequency).</param>
    public void SetData(float[,] data)
    {
        int timeSteps = data.GetLength(0);
        int frequencyBins = data.GetLength(1);

        if (timeSteps != currentTimeSteps || frequencyBins != currentFrequencyBins)
        {
            AdjustVoxelGrid(timeSteps, frequencyBins);
            currentTimeSteps = timeSteps;
            currentFrequencyBins = frequencyBins;
        }

        for (int t = 0; t < timeSteps; t++)
        {
            for (int f = 0; f < frequencyBins; f++)
            {
                float normalizedHeight = Mathf.Clamp01(data[t, f]);
                Vector3 voxelScale = voxelGrid[t][f].transform.localScale;
                voxelScale.y = normalizedHeight * maxVoxelHeight;
                voxelGrid[t][f].transform.localScale = voxelScale;

                Renderer voxelRenderer = voxelGrid[t][f].GetComponent<Renderer>();
                if (voxelRenderer != null)
                {
                    voxelRenderer.sharedMaterial.color = GetPrecomputedColor(normalizedHeight);
                }
            }
        }
    }

    /// <summary>
    /// Ensures the correct number of voxels in the grid.
    /// </summary>
    /// <param name="timeSteps">Number of time steps.</param>
    /// <param name="frequencyBins">Number of frequency bins.</param>
    private void AdjustVoxelGrid(int timeSteps, int frequencyBins)
    {
        // Add rows if needed
        while (voxelGrid.Count < timeSteps)
        {
            voxelGrid.Add(new List<GameObject>());
        }

        // Adjust columns in each row
        for (int t = 0; t < timeSteps; t++)
        {
            while (voxelGrid[t].Count < frequencyBins)
            {
                GameObject newVoxel = GetPooledVoxel();
                newVoxel.transform.localPosition = new Vector3(t * voxelSpacing.x, 0, t * voxelSpacing.z);
                voxelGrid[t].Add(newVoxel);
            }

            while (voxelGrid[t].Count > frequencyBins)
            {
                GameObject voxelToRemove = voxelGrid[t][voxelGrid[t].Count - 1];
                voxelGrid[t].RemoveAt(voxelGrid[t].Count - 1);
                ReturnVoxelToPool(voxelToRemove);
            }
        }

        // Remove extra rows if needed
        while (voxelGrid.Count > timeSteps)
        {
            List<GameObject> rowToRemove = voxelGrid[voxelGrid.Count - 1];
            foreach (GameObject voxel in rowToRemove)
            {
                ReturnVoxelToPool(voxel);
            }
            voxelGrid.RemoveAt(voxelGrid.Count - 1);
        }
    }

    /// <summary>
    /// Retrieves a voxel from the pool or instantiates a new one if the pool is empty.
    /// </summary>
    private GameObject GetPooledVoxel()
    {
        if (voxelPool.Count > 0)
        {
            GameObject voxel = voxelPool.Dequeue();
            voxel.SetActive(true);
            return voxel;
        }
        return Instantiate(voxelPrefab, voxelParent);
    }

    /// <summary>
    /// Returns a voxel to the pool for reuse.
    /// </summary>
    private void ReturnVoxelToPool(GameObject voxel)
    {
        voxel.SetActive(false);
        voxelPool.Enqueue(voxel);
    }

    /// <summary>
    /// Precomputes gradient colors for a fixed number of steps to optimize color evaluation.
    /// </summary>
    private void PrecomputeGradientColors(int steps)
    {
        precomputedColors = new Color[steps];
        for (int i = 0; i < steps; i++)
        {
            precomputedColors[i] = colorGradient.Evaluate((float)i / (steps - 1));
        }
    }

    /// <summary>
    /// Retrieves a precomputed color based on a normalized value.
    /// </summary>
    private Color GetPrecomputedColor(float normalizedValue)
    {
        int index = Mathf.Clamp(Mathf.RoundToInt(normalizedValue * (precomputedColors.Length - 1)), 0, precomputedColors.Length - 1);
        return precomputedColors[index];
    }

    /// <summary>
    /// Clears all voxels from the grid.
    /// </summary>
    public void Clear()
    {
        foreach (List<GameObject> row in voxelGrid)
        {
            foreach (GameObject voxel in row)
            {
                voxel.SetActive(false); // Disable instead of destroying
            }
        }
        voxelGrid.Clear();
    }

    /// <summary>
    /// Generates example rendering data (sinusoidal or random) for visualization.
    /// </summary>
    private void GenerateTestData()
    {
        for (int t = 0; t < currentTimeSteps; t++)
        {
            for (int f = 0; f < currentFrequencyBins; f++)
            {
                // Example: Sinusoidal data
                preallocatedData[t, f] = Mathf.Abs(Mathf.Sin((t + f) * testFrequency * Mathf.PI / currentTimeSteps));

                // Uncomment the following line for random data instead:
                // preallocatedData[t, f] = UnityEngine.Random.value;
            }
        }

        SetData(preallocatedData);
    }
}