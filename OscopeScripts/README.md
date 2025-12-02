# Wireless Oscilloscope Streaming for Meta Quest 3S

Real-time oscilloscope data streaming system for XR applications. Raspberry Pi acquires data from Keysight MSOX604A oscilloscope and streams it wirelessly to Meta Quest 3S running Unity.

## Architecture

```
┌─────────────────┐      SCPI/LAN      ┌──────────────┐     WebSocket    ┌────────────────┐
│  Oscilloscope   │◄──────────────────►│ Raspberry Pi │◄────────────────►│ Meta Quest 3S  │
│   MSOX604A      │                    │  (Server)    │                  │   (Unity)      │
└─────────────────┘                    └──────────────┘                  └────────────────┘
                                        - Data acquisition                 - Visualization
                                        - Preprocessing                    - Gesture control
                                        - FFT computation                  - Local buffering
```

## Features

 **Real-time streaming** - Low-latency waveform data streaming  
 **FFT Analysis** - Server-side Fast Fourier Transform with windowing  
 **Freeze/Hold** - Pause streaming, retain buffer locally (bandwidth drops to ~0)  
 **Preprocessing** - DC removal, scaling, windowing on Pi (clean data to headset)  
 **Gesture Control** - Trigger functions via hand gestures in VR  
 **Multiple Channels** - Stream/analyze up to 4 channels independently  

Supports Keysight MSOX604A on CH1/CH2 and Analog Discovery 3 (AD3) on CH3/CH4.

## Setup

### 1. Raspberry Pi Setup

```bash
# Clone or copy files to your Raspberry Pi
cd ~/oscope_project

# Install Python 3.11+ if needed
sudo apt update
sudo apt install python3.11 python3-pip python3-venv

# Create virtual environment
python3 -m venv venv
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt
```

On Windows, use the provided bootstrap script:

```bat
cd OscopeScripts
activate.bat
```

### 2. Configuration

**Update `oscope_streaming.py`:**

```python
# Line 385-390
scope_config = ScopeConfig(ip_address="169.254.208.205")  # Your scope's IP
stream_config = StreamConfig(
    headset_ip="192.168.1.100",  # Your Quest 3S IP on network
    port=8765
)
```

**Find your Quest 3S IP:**
1. In Quest: Settings → Wi-Fi → Connected network → Advanced
2. Note the IP address (e.g., `192.168.1.100`)

**Ensure devices on same network:**
- Connect Pi and Quest to same Wi-Fi network
- Or use Pi as Wi-Fi hotspot

### 3. Unity Setup (Quest 3S)

**Required packages:**
```
- XR Interaction Toolkit
- Meta XR All-in-One SDK
- NativeWebSocket (com.endel.nativewebsocket)
- Newtonsoft JSON (com.unity.nuget.newtonsoft-json)
```

**Installation:**
1. Copy `OscopeClient.cs` to your Unity project (`Assets/Scripts/`)
2. Attach to a GameObject in your scene
3. Set Raspberry Pi IP in Inspector
4. Hook up gesture events (see examples in script)

## Usage

### Start the Server (Raspberry Pi)

```bash
source venv/bin/activate
python oscope_streaming.py
```

Output:
```
Connected to: AGILENT TECHNOLOGIES,MSO-X 6004A,MY56270926,06.12.2016010702
Starting WebSocket server on port 8765
Waiting for headset connection...
```

### Connect from Unity (Quest 3S)

The `OscopeClient` script will auto-connect on Start() and begin streaming channel 1.

## API Commands

### From Unity to Pi (Gesture → Command)

**Start Streaming:**
```csharp
oscopeClient.StartStreaming(channel: 1);
```

**Request FFT:**
```csharp
oscopeClient.RequestFFT(channel: 1, windowType: "hann");
// Window types: "hann", "hamming", "blackman", "bartlett", null
```

**Freeze Channel (Trigger Hold):**
```csharp
oscopeClient.FreezeChannel(channel: 1, freeze: true);
// Bandwidth drops to near-zero, last buffer retained on headset
```

**Unfreeze:**
```csharp
oscopeClient.FreezeChannel(channel: 1, freeze: false);
```

**Stop Streaming:**
```csharp
oscopeClient.StopStreaming(channel: 1);
```

### From Pi to Unity (Data Events)

**Receive Waveform Data:**
```csharp
oscopeClient.OnWaveformReceived += (channel, data) => {
    // data is float[] of voltage samples
    // Update your visualization here
    UpdateWaveformDisplay(channel, data);
};
```

**Receive FFT Data:**
```csharp
oscopeClient.OnFFTReceived += (channel, fftData) => {
    // fftData contains frequencies[], magnitude_db[], magnitude_linear[]
    UpdateSpectrumDisplay(channel, fftData);
};
```

**Channel Frozen:**
```csharp
oscopeClient.OnChannelFrozen += (channel, isFrozen) => {
    // Update UI to show frozen state
    UpdateFrozenIndicator(channel, isFrozen);
};
```

## Gesture Mapping Examples

Current in-app gestures (Meta XR hand tracking):

- **Left wrist flip:** Toggle the menu Canvas
- **Right index single-tap:** Place cursor/select UI target
- **Right index double-tap:** Freeze/unfreeze selected channel
- **Middle finger hold:** Toggle FFT mode for selected channel
- **Two-hand index pinch:** Horizontal zoom; updates time/div label when released

## Message Protocol

### Waveform Data (Pi → Quest)
```json
{
  "type": "waveform",
  "channel": 1,
  "data": [0.023, 0.045, -0.012, ...],
  "timestamp": 1732234567.89,
  "sample_rate": 5000000000.0
}
```

### FFT Data (Pi → Quest)
```json
{
  "type": "fft",
  "channel": 1,
  "frequencies": [0, 1000, 2000, ...],
  "magnitude_db": [-20.5, -18.3, -25.1, ...],
  "magnitude_linear": [0.095, 0.122, 0.056, ...],
  "window": "hann",
  "sample_rate": 5000000000.0
}
```

### Command (Quest → Pi)
```json
{
  "command": "stream",
  "channel": 1
}
```

```json
{
  "command": "fft",
  "channel": 1,
  "window": "hann"
}
```

```json
{
  "command": "freeze",
  "channel": 1,
  "freeze": true
}
```

## Performance Optimization

### Bandwidth Considerations

**Streaming mode (active):**
- ~20 updates/second
- Each packet: ~4-8 KB (1000 samples)
- Bandwidth: ~80-160 KB/s per channel

**Frozen mode:**
- 1 status update/second
- Packet: ~100 bytes
- Bandwidth: ~100 bytes/s (99.9% reduction)

### Preprocessing Benefits

All done on Pi before transmission:
- **DC removal**: Removes offset, improves dynamic range
- **Scaling**: Normalizes amplitude for consistent display
- **Windowing**: Applied before FFT to reduce spectral leakage

Result: Headset receives clean, ready-to-display data

## Troubleshooting

### Pi can't connect to oscilloscope

```bash
# Test network connectivity
ping 169.254.208.205

# Check scope IP in LAN settings:
# Utility → I/O → LAN Settings → Config
# Ensure "Automatic" is OFF for static IP
```

### Quest can't connect to Pi

```bash
# On Pi, check if server is running
netstat -tuln | grep 8765

# Test from Quest browser
# Open: http://<PI_IP>:8765
# Should see WebSocket connection attempt
```

### Scope stuck in remote mode

On oscilloscope front panel:
- Press **Local** button, or
- Power cycle the scope

The script should automatically return it to local control.

### High latency

- Reduce update rate in `stream_channel()` (line 256):
  ```python
  await asyncio.sleep(0.1)  # 10 Hz instead of 20 Hz
  ```
- Use UDP instead of WebSocket (lower latency, no reliability)
- Reduce buffer size in config

## Network Setup Options

### Option 1: Same Wi-Fi Network (Recommended)
- Connect both Pi and Quest to same router
- Easiest, most stable

### Option 2: Pi as Hotspot
- Configure Pi to broadcast Wi-Fi
- Quest connects directly to Pi
- Lower latency, no external network needed

### Option 3: Direct Ethernet + Wi-Fi Bridge
- Pi connected to scope via Ethernet
- Pi has Wi-Fi for Quest connection
- Best for lab setups

## Files

- `oscope_streaming.py` - Main Python server (run on Pi)
- `OscopeClient.cs` - Unity client script (attach to GameObject)
- `requirements.txt` - Python dependencies
- `oscope.py` - Original test script (connectivity verification)
- `activate.bat` - Windows bootstrap to create/activate venv and install deps

## Dependencies

**Python (Pi):**
- pyvisa >= 1.15.0
- numpy >= 1.24.0
- scipy >= 1.11.0
- websockets >= 12.0
- pydwf >= 1.1.19 (Analog Discovery 3)

**Unity (Quest):**
- NativeWebSocket
- Newtonsoft.Json
- Meta XR SDK
- XR Interaction Toolkit

## Future Enhancements

- [ ] Multiple simultaneous client connections
- [ ] Configurable sample rate from Unity
- [ ] Trigger configuration via gestures
- [ ] Measurement cursors controlled by hand tracking
- [ ] Recording/playback functionality
- [ ] UDP streaming option for ultra-low latency
- [ ] Automatic scope discovery (mDNS)

## License

MIT - Use freely for your CMU XR Systems project!

## Credits

Created for CMU F25 XR Systems course - Meta Quest 3S oscilloscope visualization project.
