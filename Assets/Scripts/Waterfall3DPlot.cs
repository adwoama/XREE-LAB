using System.Collections.Generic;
using UnityEngine;

public class Waterfall3DPlot : MonoBehaviour
{
    [Header("Waterfall Settings")]
    [SerializeField] private GameObject slicePrefab; // Prefab for individual FFT slices
    [SerializeField] private Transform sliceParent; // Parent object for slices
    [SerializeField] private float sliceSpacing = 0.1f; // Spacing between slices in Z direction

    [Header("Appearance Settings")]
    [SerializeField] private Gradient colorGradient; // Gradient for slice colors
    [SerializeField] private float maxSliceHeight = 5f; // Maximum height of slice bars

    private Queue<GameObject> slices = new Queue<GameObject>();

    /// <summary>
    /// Adds a new FFT frame to the waterfall plot.
    /// </summary>
    /// <param name="fftFrame">Array of FFT magnitudes.</param>
    public void AddFrame(float[] fftFrame)
    {
        // Create a new slice
        GameObject newSlice = Instantiate(slicePrefab, sliceParent);
        newSlice.transform.localPosition = Vector3.zero;

        // Update bars in the slice
        for (int i = 0; i < fftFrame.Length; i++)
        {
            float normalizedHeight = Mathf.Clamp01(fftFrame[i]);
            Transform bar = newSlice.transform.GetChild(i);
            Vector3 barScale = bar.localScale;
            barScale.y = normalizedHeight * maxSliceHeight;
            bar.localScale = barScale;

            Renderer barRenderer = bar.GetComponent<Renderer>();
            if (barRenderer != null)
            {
                barRenderer.material.color = colorGradient.Evaluate(normalizedHeight);
            }
        }

        // Add the new slice to the queue
        slices.Enqueue(newSlice);

        // Offset all slices backward
        foreach (GameObject slice in slices)
        {
            slice.transform.localPosition += new Vector3(0, 0, -sliceSpacing);
        }

        // Remove the oldest slice if the queue exceeds the limit
        if (slices.Count > 100) // Arbitrary limit for now
        {
            GameObject oldSlice = slices.Dequeue();
            Destroy(oldSlice);
        }
    }

    /// <summary>
    /// Clears all slices from the waterfall plot.
    /// </summary>
    public void Clear()
    {
        foreach (GameObject slice in slices)
        {
            Destroy(slice);
        }
        slices.Clear();
    }
}