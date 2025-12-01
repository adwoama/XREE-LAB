using UnityEngine;

/// <summary>
/// Updates the oscilloscope time/div label after a zoom gesture completes.
/// Listens for MetaXRGestureActions.OnZoomComplete and applies the current WaveformPanel.horizontalScale
/// to adjust the OscilloscopeGrid's timePerDiv label (post-gesture, not continuously).
/// </summary>
public class ZoomScaleLabelUpdater : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private XreeLab.Gestures.MetaXRGestureActions gestureActions;
    [SerializeField] private WaveformPanel waveformPanel;
    [SerializeField] private OscilloscopeGrid grid;

    [Header("Behavior")]
    [Tooltip("If true, recompute base time/div from grid at Start. If false, keep last set value.")]
    public bool captureBaseOnStart = true;

    [Tooltip("Optional manual base time/div in microseconds. If > 0, overrides grid's current value as base.")]
    public float manualBaseTimePerDivUS = 0f;

    private float baseTimePerDivUS;

    void Start()
    {
        if (gestureActions != null)
        {
            gestureActions.OnZoomComplete += HandleZoomComplete;
        }

        if (captureBaseOnStart)
        {
            baseTimePerDivUS = manualBaseTimePerDivUS > 0f ? manualBaseTimePerDivUS : (grid != null ? grid.timePerDivUS : 0f);
        }
    }

    void OnDestroy()
    {
        if (gestureActions != null)
        {
            gestureActions.OnZoomComplete -= HandleZoomComplete;
        }
    }

    void HandleZoomComplete()
    {
        if (grid == null || waveformPanel == null) return;

        // Choose base time/div
        float baseUS = manualBaseTimePerDivUS > 0f ? manualBaseTimePerDivUS : (baseTimePerDivUS > 0f ? baseTimePerDivUS : grid.timePerDivUS);
        if (baseUS <= 0f) baseUS = grid.timePerDivUS; // fallback

        float effectiveUS = baseUS / Mathf.Max(0.0001f, waveformPanel.horizontalScale);
        grid.SetTimePerDiv(effectiveUS);
        Debug.Log($"[ZoomScaleLabelUpdater] Zoom complete: horizontalScale={waveformPanel.horizontalScale:F2}, time/div now={effectiveUS:F2}us");
    }
}
