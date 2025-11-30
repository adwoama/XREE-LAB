using UnityEngine;

/// <summary>
/// Automatically updates OscilloscopeGrid scale labels based on scope channel settings.
/// Attach to a GameObject that has both WaveformPanel and OscilloscopeGrid.
/// </summary>
public class ScopeScaleUpdater : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TcpOscopeClient tcpClient;
    [SerializeField] private OscilloscopeGrid grid;
    [SerializeField] private int channelNumber = 1;

    [Header("Manual Scale Override")]
    [Tooltip("If > 0, uses this instead of auto-calculated volts/div")]
    public float manualVoltsPerDiv = 0f;
    [Tooltip("If > 0, uses this instead of auto-calculated time/div")]
    public float manualTimePerDivUS = 0f;

    private float lastSampleRate = 0f;
    private int lastSampleCount = 0;

    void Start()
    {
        if (tcpClient != null && grid != null)
        {
            tcpClient.OnWaveformReceived += OnWaveformData;
        }
    }

    void OnDestroy()
    {
        if (tcpClient != null)
        {
            tcpClient.OnWaveformReceived -= OnWaveformData;
        }
    }

    private void OnWaveformData(int channel, float[] samples)
    {
        if (channel != channelNumber) return;
        if (grid == null) return;

        // Get sample rate from client's last received packet
        // (TcpOscopeClient would need to expose this; for now we estimate)
        int sampleCount = samples.Length;
        
        // Calculate time per division based on sample rate
        // Typical scope: 10 horizontal divisions, so total time = samples / sample_rate
        // Example: 1000 samples at 1 MSa/s = 1ms total, /10 div = 100µs/div
        if (manualTimePerDivUS > 0)
        {
            grid.SetTimePerDiv(manualTimePerDivUS);
        }
        else if (lastSampleRate > 0 && sampleCount > 0)
        {
            float totalTimeSeconds = sampleCount / lastSampleRate;
            float timePerDivUS = (totalTimeSeconds / 10f) * 1e6f; // 10 divisions
            grid.SetTimePerDiv(timePerDivUS);
        }

        // Voltage scale: estimate from data peak-to-peak
        if (manualVoltsPerDiv > 0)
        {
            grid.SetVoltsPerDiv(manualVoltsPerDiv);
        }
        else
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float v in samples)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            float pkpk = max - min;
            // Normalize: data is in [-1,1] range; assume this represents actual Vpp on scope
            // For now, use a reasonable default like 2V/div for 8 divisions = 16V full scale
            // User can override with manualVoltsPerDiv
            float estimatedVoltsPerDiv = pkpk / 8f; // 8 vertical divisions
            if (estimatedVoltsPerDiv < 0.1f) estimatedVoltsPerDiv = 0.1f;
            grid.SetVoltsPerDiv(estimatedVoltsPerDiv);
        }
    }

    // Public API to update sample rate (call from connector or client event)
    public void SetSampleRate(float rateHz)
    {
        lastSampleRate = rateHz;
    }
}
