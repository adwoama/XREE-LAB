using UnityEngine;

// Attach this to a GameObject (e.g. WaveformManager) and assign:
// - TcpOscopeClient reference
// - WaveformPanel for Channel 1
// - WaveformPanel for Channel 2
// It listens to incoming waveform events and routes them.
public class WaveformPanelConnector : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private TcpOscopeClient tcpClient;

    [Header("Panels")] 
    [SerializeField] private WaveformPanel channel1Panel;
    [SerializeField] private WaveformPanel channel2Panel;

    [Header("Auto Start Both Channels")] 
    [SerializeField] private bool streamChannel1 = true;
    [SerializeField] private bool streamChannel2 = true;

    private bool channelsStarted = false;

    private void Awake()
    {
        if (tcpClient == null)
        {
            Debug.LogError("WaveformPanelConnector: TcpOscopeClient not assigned.");
            return;
        }
        tcpClient.OnWaveformReceived += OnWaveform;
        tcpClient.OnFFTReceived += OnFFT;
        tcpClient.OnChannelFrozen += OnChannelFrozen;
    }

    private void Start()
    {
        if (tcpClient == null) return;

        if (streamChannel1 || streamChannel2)
        {
            // If already connected, start immediately; else defer until OnConnected
            if (tcpClient.IsConnected)
            {
                StartRequestedChannels();
            }
            else
            {
                tcpClient.OnConnected += HandleClientConnected;
            }
        }
    }

    private void HandleClientConnected()
    {
        Debug.Log($"[WaveformPanelConnector] HandleClientConnected - channelsStarted={channelsStarted}, streamCH1={streamChannel1}, streamCH2={streamChannel2}");
        // Avoid duplicate starts on reconnects
        if (!channelsStarted)
        {
            StartRequestedChannels();
        }
    }

    private async System.Threading.Tasks.Task StartRequestedChannels()
    {
        if (channelsStarted)
        {
            Debug.LogWarning("[WaveformPanelConnector] StartRequestedChannels called but already started.");
            return;
        }
        Debug.Log($"[WaveformPanelConnector] StartRequestedChannels: will start CH1={streamChannel1}, CH2={streamChannel2}");
        
        if (streamChannel1)
        {
            tcpClient.StartStreaming(1);
            await System.Threading.Tasks.Task.Delay(100); // small delay between commands
        }
        
        if (streamChannel2)
        {
            tcpClient.StartStreaming(2);
            await System.Threading.Tasks.Task.Delay(100);
        }
        
        channelsStarted = true;
        tcpClient.OnConnected -= HandleClientConnected; // unsubscribe once done
        Debug.Log("[WaveformPanelConnector] StartRequestedChannels: completed.");
    }

    private void OnDestroy()
    {
        if (tcpClient != null)
            tcpClient.OnWaveformReceived -= OnWaveform;
        if (tcpClient != null)
            tcpClient.OnConnected -= HandleClientConnected;
        if (tcpClient != null)
            tcpClient.OnFFTReceived -= OnFFT;
        if (tcpClient != null)
            tcpClient.OnChannelFrozen -= OnChannelFrozen;
    }

    private void OnWaveform(int channel, float[] samples)
    {
        if (IsChannelFrozen(channel)) return; // suppress live updates while frozen
        switch (channel)
        {
            case 1:
                if (channel1Panel != null)
                {
                    channel1Panel.SetSamples(samples);
                    if (samples != null && samples.Length > 0)
                        Debug.Log($"[WaveformPanelConnector] Applied CH1 samples len={samples.Length} first={samples[0]:F3}");
                }
                break;
            case 2:
                if (channel2Panel != null)
                {
                    channel2Panel.SetSamples(samples);
                    if (samples != null && samples.Length > 0)
                        Debug.Log($"[WaveformPanelConnector] Applied CH2 samples len={samples.Length} first={samples[0]:F3}");
                }
                break;
        }
    }

    // FFT handler
    private void OnFFT(int channel, TcpOscopeClient.FFTData fft)
    {
        float[] mags = fft.magnitude_db != null && fft.magnitude_db.Length > 0 ? fft.magnitude_db : fft.magnitude_linear;
        switch (channel)
        {
            case 1:
                if (channel1Panel != null && channel1Panel.showFFT)
                    channel1Panel.SetFFT(mags);
                break;
            case 2:
                if (channel2Panel != null && channel2Panel.showFFT)
                    channel2Panel.SetFFT(mags);
                break;
        }
    }

    private System.Collections.Generic.HashSet<int> frozenChannels = new System.Collections.Generic.HashSet<int>();
    private void OnChannelFrozen(int channel, bool frozen)
    {
        if (frozen) frozenChannels.Add(channel); else frozenChannels.Remove(channel);
        Debug.Log($"[WaveformPanelConnector] Channel {channel} frozen={frozen}");

        // Update panel indicator
        switch (channel)
        {
            case 1:
                if (channel1Panel != null) channel1Panel.SetFrozen(frozen);
                break;
            case 2:
                if (channel2Panel != null) channel2Panel.SetFrozen(frozen);
                break;
        }
    }
    private bool IsChannelFrozen(int ch) => frozenChannels.Contains(ch);
}
