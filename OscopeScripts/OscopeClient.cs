using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;
using Newtonsoft.Json;

/// <summary>
/// Unity client for receiving oscilloscope data from Raspberry Pi
/// Attach this to a GameObject in the Meta Quest 3S scene
/// </summary>
public class OscopeClient : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private string raspberryPiIP = "192.168.1.50"; // UPDATE with the Pi's IP
    [SerializeField] private int port = 8765;
    
    [Header("Display Settings")]
    [SerializeField] private int channel = 1;
    [SerializeField] private bool autoStartStreaming = true;
    
    private WebSocket websocket;
    private Queue<WaveformData> waveformQueue = new Queue<WaveformData>();
    private Queue<FFTData> fftQueue = new Queue<FFTData>();
    
    // Events for gesture control
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
        await ConnectToServer();
        
        if (autoStartStreaming)
        {
            await Task.Delay(1000); // Wait for connection
            StartStreaming(channel);
        }
    }

    async Task ConnectToServer()
    {
        string url = $"ws://{raspberryPiIP}:{port}";
        Debug.Log($"Connecting to oscilloscope server at {url}");
        
        websocket = new WebSocket(url);
        
        websocket.OnOpen += () =>
        {
            Debug.Log("Connected to oscilloscope server!");
        };
        
        websocket.OnError += (e) =>
        {
            Debug.LogError($"WebSocket Error: {e}");
        };
        
        websocket.OnClose += (e) =>
        {
            Debug.Log($"Connection closed: {e}");
        };
        
        websocket.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            ProcessMessage(message);
        };
        
        await websocket.Connect();
    }
    
    void ProcessMessage(string json)
    {
        try
        {
            // Determine message type
            var baseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            string msgType = baseObj["type"].ToString();
            
            switch (msgType)
            {
                case "waveform":
                    var waveform = JsonConvert.DeserializeObject<WaveformData>(json);
                    waveformQueue.Enqueue(waveform);
                    break;
                    
                case "fft":
                    var fft = JsonConvert.DeserializeObject<FFTData>(json);
                    fftQueue.Enqueue(fft);
                    break;
                    
                case "freeze_response":
                    var freezeResp = JsonConvert.DeserializeObject<FreezeResponse>(json);
                    OnChannelFrozen?.Invoke(freezeResp.channel, freezeResp.frozen);
                    
                    // If buffer included, treat as final waveform
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
            Debug.LogError($"Error processing message: {e.Message}");
        }
    }
    
    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
        #endif
        
        // Process waveform data on main thread
        while (waveformQueue.Count > 0)
        {
            var waveform = waveformQueue.Dequeue();
            OnWaveformReceived?.Invoke(waveform.channel, waveform.data);
        }
        
        // Process FFT data on main thread
        while (fftQueue.Count > 0)
        {
            var fft = fftQueue.Dequeue();
            OnFFTReceived?.Invoke(fft.channel, fft);
        }
    }
    
    // === GESTURE-TRIGGERED METHODS ===
    
    /// <summary>
    /// Start streaming data from specified channel
    /// Triggered by: Pinch gesture, voice command, or button
    /// </summary>
    public async void StartStreaming(int channelNum)
    {
        var cmd = new Command
        {
            command = "stream",
            channel = channelNum
        };
        
        await SendCommand(cmd);
        Debug.Log($"Started streaming channel {channelNum}");
    }
    
    /// <summary>
    /// Stop streaming data from specified channel
    /// </summary>
    public async void StopStreaming(int channelNum)
    {
        var cmd = new Command
        {
            command = "stop_stream",
            channel = channelNum
        };
        
        await SendCommand(cmd);
        Debug.Log($"Stopped streaming channel {channelNum}");
    }
    
    /// <summary>
    /// Request FFT analysis on specified channel
    /// Triggered by: Swipe up gesture, rotate gesture
    /// </summary>
    public async void RequestFFT(int channelNum, string windowType = "hann")
    {
        var cmd = new Command
        {
            command = "fft",
            channel = channelNum,
            window = windowType
        };
        
        await SendCommand(cmd);
        Debug.Log($"Requested FFT for channel {channelNum} with {windowType} window");
    }
    
    /// <summary>
    /// Freeze/unfreeze channel (trigger hold)
    /// Triggered by: Grab gesture, double tap
    /// Reduces bandwidth to near-zero, headset displays last buffer
    /// </summary>
    public async void FreezeChannel(int channelNum, bool freeze = true)
    {
        var cmd = new Command
        {
            command = "freeze",
            channel = channelNum,
            freeze = freeze
        };
        
        await SendCommand(cmd);
        string status = freeze ? "frozen" : "unfrozen";
        Debug.Log($"Channel {channelNum} {status}");
    }
    
    private async Task SendCommand(Command cmd)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogError("WebSocket not connected!");
            return;
        }
        
        string json = JsonConvert.SerializeObject(cmd);
        await websocket.SendText(json);
    }
    
    async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
    
    // === EXAMPLE GESTURE INTEGRATION ===
    
    /// <summary>
    /// Example: Hook this up to Meta Interaction SDK gestures
    /// </summary>
    public void OnPinchGesture(int channelNum)
    {
        StartStreaming(channelNum);
    }
    
    public void OnSwipeUpGesture(int channelNum)
    {
        RequestFFT(channelNum);
    }
    
    public void OnGrabGesture(int channelNum)
    {
        FreezeChannel(channelNum, true);
    }
    
    public void OnReleaseGesture(int channelNum)
    {
        FreezeChannel(channelNum, false);
    }
}
