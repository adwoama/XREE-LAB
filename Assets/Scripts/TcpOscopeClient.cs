using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// TCP client for receiving oscilloscope data (JSON lines) from server
/// Attach to a GameObject in the scene (e.g., OscopeManager)
/// </summary>
public class TcpOscopeClient : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private string serverIP = "192.168.1.156"; //"127.0.0.1"; // Windows dev: localhost; change to Pi IP later
    [SerializeField] private int port = 8765;

    [Header("Display Settings")]
    [SerializeField] private int channel = 1;
    [SerializeField] private bool autoStartStreaming = true;
    [SerializeField] private bool autoReconnect = true;
    [SerializeField] private float reconnectIntervalSeconds = 3f;
    [SerializeField] private bool verboseLogging = true; // extra diagnostics

    private TcpClient client;
    private StreamReader reader;
    private StreamWriter writer;
    private CancellationTokenSource cts;

    private Queue<WaveformData> waveformQueue = new Queue<WaveformData>();
    private Queue<FFTData> fftQueue = new Queue<FFTData>();

    // Connection state/event
    public event Action OnConnected; // Fired after successful initial connect or reconnect
    public bool IsConnected => client != null && client.Connected;

    // Diagnostics
    private Dictionary<int, int> channelPacketCounts = new Dictionary<int, int>();
    private float lastPacketLogTime = 0f;

    // Events
    public event Action<int, float[]> OnWaveformReceived;
    public event Action<int, FFTData> OnFFTReceived;
    public event Action<int, bool> OnChannelFrozen;

    // Data structures matching Python server
    [Serializable]
    public class WaveformData
    {
        public string type;
        public int channel;
        public float[] data;
        public float timestamp;
        public float sample_rate;
    }

    [Serializable]
    public class FFTData
    {
        public string type;
        public int channel;
        public float[] frequencies;
        public float[] magnitude_db;
        public float[] magnitude_linear;
        public string window;
        public float sample_rate;
    }

    [Serializable]
    public class FreezeResponse
    {
        public string type;
        public int channel;
        public bool frozen;
        public float[] buffer;
    }

    [Serializable]
    public class Command
    {
        public string command;
        public int channel;
        public bool freeze;
        public string window;
    }

    async void Start()
    {
        await Connect();
        if (autoStartStreaming)
        {
            await Task.Delay(500);
            StartStreaming(channel);
        }

        if (autoReconnect)
        {
            StartCoroutine(ReconnectLoop());
        }
    }

    public async Task Connect()
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(serverIP, port);
            var stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            cts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveLoop(cts.Token));
            Debug.Log($"TCP connected to {serverIP}:{port}");
            OnConnected?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"TCP Connect attempt failed: {e.Message}");
        }
    }

    private System.Collections.IEnumerator ReconnectLoop()
    {
        while (autoReconnect)
        {
            if (client == null || !client.Connected)
            {
                Debug.Log($"[TcpOscopeClient] Attempting reconnect to {serverIP}:{port}...");
                _ = Connect();
            }
            yield return new WaitForSeconds(reconnectIntervalSeconds);
        }
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && client != null && client.Connected)
            {
                string line = await reader.ReadLineAsync();
                if (line == null) { await Task.Delay(10); continue; }
                ProcessMessage(line);
            }
            Debug.LogWarning("[TcpOscopeClient] Receive loop ended (disconnected)." );
        }
        catch (Exception e)
        {
            Debug.LogError($"TCP Receive error: {e.Message} | SocketConnected={client?.Connected}" );
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var baseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (baseObj == null || !baseObj.ContainsKey("type")) return;
            string msgType = baseObj["type"].ToString();

            switch (msgType)
            {
                case "waveform":
                    var waveform = JsonConvert.DeserializeObject<WaveformData>(json);
                    lock (waveformQueue) waveformQueue.Enqueue(waveform);
                    break;
                case "fft":
                    var fft = JsonConvert.DeserializeObject<FFTData>(json);
                    lock (fftQueue) fftQueue.Enqueue(fft);
                    break;
                case "freeze_response":
                    var freezeResp = JsonConvert.DeserializeObject<FreezeResponse>(json);
                    OnChannelFrozen?.Invoke(freezeResp.channel, freezeResp.frozen);
                    if (freezeResp.buffer != null && freezeResp.buffer.Length > 0)
                    {
                        OnWaveformReceived?.Invoke(freezeResp.channel, freezeResp.buffer);
                    }
                    break;
                case "status":
                    Debug.Log($"Status: {json}");
                    break;
                case "error":
                    Debug.LogError($"Server error: {json}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ProcessMessage error: {e.Message}\n{json}");
        }
    }

    void Update()
    {
        // Drain queues on main thread
        lock (waveformQueue)
        {
            while (waveformQueue.Count > 0)
            {
                var wf = waveformQueue.Dequeue();
                OnWaveformReceived?.Invoke(wf.channel, wf.data);
                if (!channelPacketCounts.ContainsKey(wf.channel)) channelPacketCounts[wf.channel] = 0;
                channelPacketCounts[wf.channel]++;
                if (verboseLogging)
                {
                    // Periodic summary every ~1s
                    if (Time.time - lastPacketLogTime > 1f)
                    {
                        foreach (var kv in channelPacketCounts)
                        {
                            if (kv.Value > 0)
                            {
                                float min = float.MaxValue; float max = float.MinValue;
                                var arr = wf.data; // use last dequeued samples for rough stats
                                for (int i = 0; i < arr.Length; i++) { if (arr[i] < min) min = arr[i]; if (arr[i] > max) max = arr[i]; }
                                Debug.Log($"[TcpOscopeClient] ch={wf.channel} packets={kv.Value} last_len={arr.Length} min={min:F3} max={max:F3}");
                            }
                        }
                        lastPacketLogTime = Time.time;
                    }
                }
            }
        }
        lock (fftQueue)
        {
            while (fftQueue.Count > 0)
            {
                var f = fftQueue.Dequeue();
                OnFFTReceived?.Invoke(f.channel, f);
            }
        }
    }

    // === Commands ===
    public async void StartStreaming(int channelNum)
    {
        await SendCommand(new Command { command = "stream", channel = channelNum });
        Debug.Log($"[TcpOscopeClient] StartStreaming command SENT for ch={channelNum}");
    }

    public async void StopStreaming(int channelNum)
    {
        await SendCommand(new Command { command = "stop_stream", channel = channelNum });
        Debug.Log($"StopStreaming {channelNum}");
    }

    public async void RequestFFT(int channelNum, string windowType = "hann")
    {
        await SendCommand(new Command { command = "fft", channel = channelNum, window = windowType });
        Debug.Log($"RequestFFT {channelNum} {windowType}");
    }

    public async void FreezeChannel(int channelNum, bool freeze = true)
    {
        await SendCommand(new Command { command = "freeze", channel = channelNum, freeze = freeze });
        Debug.Log($"Freeze {channelNum} = {freeze}");
    }

    private async Task SendCommand(Command cmd)
    {
        try
        {
            if (writer == null)
            {
                Debug.LogWarning($"[TcpOscopeClient] SendCommand FAILED: writer is null (ch={cmd.channel}, cmd={cmd.command})");
                return;
            }
            string json = JsonConvert.SerializeObject(cmd);
            await writer.WriteLineAsync(json);
            Debug.Log($"[TcpOscopeClient] SendCommand SUCCESS: {json}");
        }
        catch (Exception e)
        {
            Debug.LogError($"SendCommand error: {e.Message}");
        }
    }

    private async void OnApplicationQuit()
    {
        await Disconnect();
    }

    public async Task Disconnect()
    {
        try
        {
            cts?.Cancel();
            await Task.Delay(50);
            reader?.Dispose();
            writer?.Dispose();
            client?.Close();
            Debug.Log("[TcpOscopeClient] Disconnect completed.");
        }
        catch { }
    }
}
