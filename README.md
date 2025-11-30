# XREE-LAB

This repository hosts a Unity XR application (Meta Quest 3S target) that displays real-time oscilloscope waveforms from a Keysight MSOX604A in VR. The project features draggable, world-space waveform panels with oscilloscope-style grids and scale indicators, streaming live data over TCP from a Python server connected to the physical oscilloscope.

## High-level goal
- Stream real oscilloscope data (CH1 & CH2) from a Keysight MSOX604A to Meta Quest headset in real-time
- Display waveforms in XR with oscilloscope-style visualization (grid, voltage/time scales)
- Support hand/pinch grab interactions for moving and positioning panels in 3D space
- Provide smooth, low-latency data streaming (~20 Hz update rate) over TCP

## What is implemented

### XR Interaction & Panel System
- Waveform visuals: `WaveformPanel` component drives a LineRenderer with configurable amplitude scaling, line thickness, and color
- Four draggable panels (CH1-CH4) spawned via `WaveformManager` with Meta hand/pinch grab support
- Runtime injection and compatibility code for Meta Interaction SDK (reflection-based fallbacks for InjectColliders, InjectRigidbody, etc.)
- Prefab-based workflow with inspector-configured HandGrab/MovementProvider rules preserved across instantiation

### Real-Time Data Streaming
- **TCP Client** (`TcpOscopeClient.cs`): Connects to Python server, parses JSON waveform packets, dispatches events on main thread
  - Auto-reconnect with configurable interval
  - Supports multiple channels, FFT requests, freeze commands
  - Verbose logging for diagnostics
- **Panel Connector** (`WaveformPanelConnector.cs`): Routes incoming waveform data to appropriate channel panels
  - Waits for TCP connection before sending stream commands (fixes race condition)
  - Auto-starts CH1 & CH2 streaming on connection
- **Python Server** (`tcp_streaming_server.py`): Streams oscilloscope data over TCP using pyvisa
  - Real scope mode: Reads from Keysight MSOX604A via VISA (USB/LAN)
  - Mock mode fallback: Generates synthetic sine/cosine waves for testing
  - Per-channel streaming threads, line-delimited JSON protocol
  - Configurable sample rate, normalization to [-1, 1] range

### Oscilloscope-Style Visualization
- **Grid Overlay** (`OscilloscopeGrid.cs`): Draws graticule with configurable divisions (10 horizontal, 8 vertical)
  - Individual LineRenderer per grid line for reliability
  - Auto-updating voltage/time scale labels (V/div, µs/ms/s)
  - Adjustable color, thickness, visibility
- **Scale Updater** (`ScopeScaleUpdater.cs`): Auto-calculates and updates grid scales from incoming data or manual override
- **Amplitude Scaling**: `WaveformPanel.amplitudeScale` (0.1-1.0) keeps waveforms within panel bounds
- **External Data Mode**: Disables synthetic generation when real data arrives via `SetSamples()`

## Key files and game objects

### Unity Scripts (Assets/Scripts/)
- `WaveformManager.cs` - spawns panels, logs diagnostics, handles Meta SDK injection
- `WaveformPanel.cs` - waveform rendering with LineRenderer, amplitude scaling, external data mode
- `TcpOscopeClient.cs` - TCP client for oscilloscope data streaming, event-driven architecture
- `WaveformPanelConnector.cs` - routes waveform events to correct channel panels
- `OscilloscopeGrid.cs` - draws oscilloscope graticule grid with individual line renderers
- `ScopeScaleUpdater.cs` - updates V/div and time/div labels based on data or manual settings
- `TestGreenCubeSpawner.cs` - test spawner for debugging grab interactions
- `PanelGrabVerifier.cs` - verifies grab component injection after spawn

### Python Server (OscopeScripts/)
- `tcp_streaming_server.py` - main TCP server with real scope integration via pyvisa
  - `KeysightScope` class: VISA connection, waveform acquisition, preamble parsing
  - `MockScope` class: Synthetic data generator for testing
  - Line-delimited JSON protocol with command parsing (stream, stop_stream, fft, freeze)
- `oscope.py` - Basic VISA connection test and channel enumeration
- `quick_channel_test.py` - Quick 2-channel test with voltage/sample rate readout
- `requirements.txt` - Python dependencies (pyvisa, numpy)

### Prefabs & Assets
- `Assets/Prefabs/Panel_CH.prefab` - fully configured panel with Interaction SDK components
- `Assets/Panel_Background.mat` - URP Unlit material for panel background
- `Assets/Panel_Frame.mat`, `Assets/Panel_Handle.mat` - frame/handle materials
- `Assets/Editor/SaveSelectedAsWaveformPrefab.cs` - Editor utility for creating prefabs

### Configuration
- **Scope IP**: Set in `tcp_streaming_server.py` (`SCOPE_IP = "169.254.208.205"`)
- **Server IP**: Set in Unity Inspector on `TcpOscopeClient` component (`serverIP = "192.168.1.156"`)
- **Channels**: CH1 & CH2 enabled by default in `WaveformPanelConnector`
- **Grid scales**: Adjustable per-panel in `OscilloscopeGrid` or auto-updated via `ScopeScaleUpdater`

## Prefab contents and materials
- `Panel_CH.prefab` (in `Assets/Prefabs/`) contains:
	- Root GameObject `Panel_CH` with a Rigidbody and BoxCollider sized to the panel.
	- `WaveformPanel` component with default dummy waveform parameters (resolution, frequency, amplitude, color).
	- `LineRenderer` used for waveform visualization.
	- Child GameObjects: `Background` (Quad, MeshRenderer, material `Panel_Background`), frame edges (`Edge_Top`, `Edge_Left`, etc.), and a `Grab_Handle` child used as the grab handle.
	- Interaction SDK components wired on the prefab: `Grabbable` (parent), `GrabInteractable`, `HandGrabInteractable` (installation child) and `MoveTowardsTargetProvider`.
- Materials used:
	- `Assets/Panel_Background.mat` - URP-compatible material (Unlit/URP) to avoid magenta artifacts on URP builds.
	- `Assets/Panel_Frame.mat`, `Assets/Panel_Handle.mat` - small materials for frame/handle.

The prefab was intentionally assembled in Editor so the HandGrabInteractable serialized properties (Pinch/Palm grabbing rules, MovementProvider references) are preserved and don't need fragile runtime reflection to set.

## What has been tried (summary of approaches)
- Runtime-only wiring by creating panels procedurally and using reflection to set private backing fields and call injector methods. This worked in the Editor in many cases but was fragile due to Start() ordering races and un-serialized runtime changes not persisting into build-time assets.
- Prefab-first workflow: create a fully-configured prefab in Editor (with Inspector-set HandGrab rules, MovementProvider, and materials) and then instantiate that prefab at runtime. This is more robust and recommended.
- Added a one-frame re-injection coroutine (`ReinjectHandGrabRulesNextFrame`) to try to address Start() ordering races when reflection is unavoidable.

## Setup & Usage

### Python Server Setup
1. Navigate to `OscopeScripts/` directory
2. Activate virtual environment:
   ```cmd
   cd c:\Users\robot\Documents\XREE-LAB\OscopeScripts
   venv\Scripts\activate.bat
   ```
3. Install dependencies (if needed):
   ```cmd
   pip install -r requirements.txt
   ```
4. Update scope IP in `tcp_streaming_server.py` if changed (default: `169.254.208.205`)
5. Ensure oscilloscope is powered on and CH1/CH2 are displayed
6. Start server:
   ```cmd
   python tcp_streaming_server.py
   ```
   Look for: `[SCOPE] Connected: KEYSIGHT TECHNOLOGIES...` and `[SERVER] Using REAL scope data`

### Unity Setup
1. Open project in Unity 2022.3+ with URP
2. Select `OscopeManager` GameObject in Hierarchy
3. In `TcpOscopeClient` component, set `Server IP` to your PC's LAN IP (e.g., `192.168.1.156`)
4. In `WaveformPanelConnector`, ensure `Stream Channel 1` and `Stream Channel 2` are checked
5. On each panel GameObject:
   - Add `OscilloscopeGrid` component (if not present)
   - Add `ScopeScaleUpdater` component and assign references:
     - `Tcp Client`: OscopeManager
     - `Grid`: OscilloscopeGrid on same panel
     - `Channel Number`: 1 or 2
   - Adjust `Amplitude Scale` (0.5-0.8 recommended) in `WaveformPanel` to fit waveforms in bounds
6. Build Settings → Android → Switch Platform
7. Build And Run to Meta Quest 3S

### Networking Notes
- **Firewall**: Allow Python and Unity through Windows Firewall (both inbound/outbound)
- **IP Addresses**: 
  - Scope: Link-local (169.254.x.x) or LAN IP
  - PC Server: LAN IP (192.168.x.x), NOT 127.0.0.1 for device builds
  - Quest: Check in Meta Quest Developer Hub or via `adb shell ip addr`
- **Testing**: Use `telnet <server_ip> 8765` to verify TCP connectivity before building

### Debugging
- **Unity Logs (Device)**: 
  ```cmd
  adb logcat -s Unity
  adb logcat | findstr TcpOscopeClient
  ```
- **Meta Quest Developer Hub**: Device → Logs tab → filter "TcpOscopeClient" or "WaveformPanelConnector"
- **Server Logs**: Watch Python console for connection events, stream commands, packet counts
- **Verbose Mode**: Enable `verboseLogging` in `TcpOscopeClient` for detailed packet statistics

## What works
- Real-time streaming of oscilloscope data from Keysight MSOX604A to Meta Quest over TCP
- Smooth waveform updates at ~20 Hz with automatic reconnection on disconnect
- Oscilloscope-style grid overlay with auto-updating voltage and time scale labels
- Hand/pinch grab interactions for moving panels in 3D space
- Prefab-based panel instantiation preserves Interaction SDK configuration
- Dual-channel display (CH1 & CH2) with independent waveform routing
- Automatic fallback to mock data if scope connection fails
- Amplitude scaling keeps waveforms within panel bounds

## Known issues / What didn't work reliably
- If you assign a scene object instance (a Hierarchy object) to `WaveformManager.panelPrefab` during Play instead of the prefab asset from the Project view, that scene reference does not persist to builds - panels will not appear in the built player. Always assign the prefab asset (Project → Assets/Prefabs/Panel_CH).
- Some earlier edits introduced duplicate/fragmented code in `WaveformManager.cs` which caused compile errors; those were cleaned up. 
- Runtime reflection is brittle across SDK versions and can miss private fields with different names or signature changes; prefer the prefab workflow.
- Shader/material pitfalls: if a material uses a shader that isn't available on the Android/Quest build (or not included in the build), objects may appear invisible or magenta on device. Use URP-compatible Unlit shaders for stable results and verify shaders are included.

