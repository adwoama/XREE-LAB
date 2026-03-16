using System.Collections.Generic;
using UnityEngine;

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

    /// <summary>
    /// Sets the spectrogram data and updates the 3D grid.
    /// </summary>
    /// <param name="data">2D array of amplitudes (time x frequency).</param>
    public void SetData(float[,] data)
    {
        int timeSteps = data.GetLength(0);
        int frequencyBins = data.GetLength(1);

        AdjustVoxelGrid(timeSteps, frequencyBins);

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
                    voxelRenderer.material.color = colorGradient.Evaluate(normalizedHeight);
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

        // Remove extra rows if needed
        while (voxelGrid.Count > timeSteps)
        {
            List<GameObject> rowToRemove = voxelGrid[voxelGrid.Count - 1];
            foreach (GameObject voxel in rowToRemove)
            {
                Destroy(voxel);
            }
            voxelGrid.RemoveAt(voxelGrid.Count - 1);
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
                Destroy(voxelToRemove);
            }
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
                Destroy(voxel);
            }
        }
        voxelGrid.Clear();
    }
}