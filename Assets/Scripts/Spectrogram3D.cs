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

    private float[,] preallocatedData; // Preallocated array for data
    private int currentTimeSteps = 0;
    private int currentFrequencyBins = 0;

    private void Start()
    {
        // Preallocate voxel grid based on initial settings
        InitializeVoxelGrid(10, 10); // Default size, can be adjusted
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
                    voxelRenderer.sharedMaterial.color = colorGradient.Evaluate(normalizedHeight);
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
                GameObject newVoxel = Instantiate(voxelPrefab, voxelParent);
                newVoxel.transform.localPosition = new Vector3(t * voxelSpacing.x, 0, t * voxelSpacing.z);
                voxelGrid[t].Add(newVoxel);
            }

            while (voxelGrid[t].Count > frequencyBins)
            {
                GameObject voxelToRemove = voxelGrid[t][voxelGrid[t].Count - 1];
                voxelGrid[t].RemoveAt(voxelGrid[t].Count - 1);
                voxelToRemove.SetActive(false); // Disable instead of destroying
            }
        }

        // Remove extra rows if needed
        while (voxelGrid.Count > timeSteps)
        {
            List<GameObject> rowToRemove = voxelGrid[voxelGrid.Count - 1];
            foreach (GameObject voxel in rowToRemove)
            {
                voxel.SetActive(false); // Disable instead of destroying
            }
            voxelGrid.RemoveAt(voxelGrid.Count - 1);
        }
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