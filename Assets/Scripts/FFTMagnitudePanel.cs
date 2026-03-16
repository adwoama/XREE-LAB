using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FFTMagnitudePanel : MonoBehaviour
{
    [Header("Bar Settings")]
    [SerializeField] private GameObject barPrefab; // Prefab for individual bars
    [SerializeField] private Transform barParent; // Parent object for bars
    [SerializeField] private float barSpacing = 0.1f; // Spacing between bars

    [Header("Bar Appearance")]
    [SerializeField] private Gradient barColorGradient; // Gradient for bar colors
    [SerializeField] private float maxBarHeight = 5f; // Maximum height of bars

    private List<GameObject> bars = new List<GameObject>();

    /// <summary>
    /// Sets the FFT magnitude data and updates the bar graph.
    /// </summary>
    /// <param name="magnitudes">Array of FFT magnitudes.</param>
    public void SetData(float[] magnitudes)
    {
        // Ensure the correct number of bars
        AdjustBarCount(magnitudes.Length);

        // Update each bar's height and color
        for (int i = 0; i < magnitudes.Length; i++)
        {
            float normalizedHeight = Mathf.Clamp01(magnitudes[i]);
            Vector3 barScale = bars[i].transform.localScale;
            barScale.y = normalizedHeight * maxBarHeight;
            bars[i].transform.localScale = barScale;

            Renderer barRenderer = bars[i].GetComponent<Renderer>();
            if (barRenderer != null)
            {
                barRenderer.material.color = barColorGradient.Evaluate(normalizedHeight);
            }
        }
    }

    /// <summary>
    /// Ensures the correct number of bars are instantiated.
    /// </summary>
    /// <param name="count">The required number of bars.</param>
    private void AdjustBarCount(int count)
    {
        // Add bars if needed
        while (bars.Count < count)
        {
            GameObject newBar = Instantiate(barPrefab, barParent);
            newBar.transform.localPosition = new Vector3(bars.Count * (1 + barSpacing), 0, 0);
            bars.Add(newBar);
        }

        // Remove extra bars if needed
        while (bars.Count > count)
        {
            GameObject barToRemove = bars[bars.Count - 1];
            bars.RemoveAt(bars.Count - 1);
            Destroy(barToRemove);
        }
    }

    /// <summary>
    /// Clears all bars from the panel.
    /// </summary>
    public void Clear()
    {
        foreach (GameObject bar in bars)
        {
            Destroy(bar);
        }
        bars.Clear();
    }
}