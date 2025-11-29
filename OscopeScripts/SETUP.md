# Quick Start Guide

## Before You Begin

1. **Connect oscilloscope to network**
   - Set static IP: `169.254.208.205`
   - Turn OFF "Automatic" in LAN settings
   
2. **Find Quest 3S IP address**
   - Settings → Wi-Fi → Connected network → Advanced
   - Note the IP (e.g., `192.168.1.100`)

3. **Update configuration**
   - Edit `oscope_streaming.py` line 389 with Quest IP

## Running on Raspberry Pi

```bash
# First time setup
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# Test connection
python test_oscope.py

# Start server
python oscope_streaming.py
```

## Unity Setup (Quest 3S)

1. Install packages:
   - NativeWebSocket (Package Manager → Add from git URL: `https://github.com/endel/NativeWebSocket.git#upm`)
   - Newtonsoft JSON (already in Unity)
   - Meta XR All-in-One SDK

2. Add script:
   - Copy `OscopeClient.cs` to `Assets/Scripts/`
   - Attach to GameObject
   - Set Pi IP in Inspector

3. Hook up gestures (example):
```csharp
// In the gesture handler script
public OscopeClient oscopeClient;

void OnPinchDetected() {
    oscopeClient.StartStreaming(1);
}

void OnGrabDetected() {
    oscopeClient.FreezeChannel(1, true);
}
```

## Gesture Reference

| Gesture | Function | Code |
|---------|----------|------|
| 👌 Pinch | Start stream | `StartStreaming(channel)` |
| ✋ Grab | Freeze | `FreezeChannel(channel, true)` |
| 🖐️ Release | Unfreeze | `FreezeChannel(channel, false)` |
| 👆 Swipe Up | FFT | `RequestFFT(channel)` |
| ✊ Close Fist | Stop stream | `StopStreaming(channel)` |
| Horizontal Zoom
| Cursor Measurement

## Troubleshooting

**Scope won't connect:**
```bash
ping 169.254.208.205
# If fails, check scope IP in Utility → I/O → LAN
```

**Quest won't connect:**
- Check both devices on same Wi-Fi
- Verify Pi IP in Unity Inspector
- Check firewall (allow port 8765)

**Scope stuck in remote mode:**
- Press "Local" button on front panel
- Or power cycle

## What to Expect

**Without probes:**
- ✅ Connection works
- ✅ Commands work
- ⚠️  Data will timeout (normal)

**With probes:**
- ✅ Full streaming
- ✅ Real waveforms
- ✅ FFT analysis

## Performance

- Stream rate: 20 Hz (adjustable)
- Latency: ~50-100ms
- Bandwidth (streaming): ~160 KB/s per channel
- Bandwidth (frozen): ~100 bytes/s

## Files You Need

**On Pi:**
- `oscope_streaming.py` (main server)
- `requirements.txt` (dependencies)
- `test_oscope.py` (connection test)

**In Unity:**
- `OscopeClient.cs` (client script)

**Documentation:**
- `README.md` (full details)
- `QUICKSTART.md` (this file)

## Next Steps

1. Test connection: `python test_oscope.py`
2. Start server: `python oscope_streaming.py`
3. Build Unity app to Quest
4. Connect and test gestures!

---

**Need help?** Check README.md for detailed protocol documentation and troubleshooting.
