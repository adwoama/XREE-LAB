# TCP Oscilloscope Streaming Server (Windows/Pi)
# Streams newline-delimited JSON frames to Unity client

import json
import socket
import threading
import time
import numpy as np

# If you have pyvisa + scope, you can integrate similar to oscope_streaming.py
USE_MOCK = True  # set False when integrating real scope reads

HOST = "0.0.0.0"
PORT = 8765
VERBOSE = True  # toggle detailed server diagnostics

# Simple mock signal generator per channel
class MockScope:
    def __init__(self, sample_rate=1e6, n=1000):
        self.sample_rate = sample_rate
        self.n = n
        self.t = np.arange(n) / sample_rate
        self.phase1 = 0.0
        self.phase2 = 0.0

    def read_channel(self, ch):
        if ch == 1:
            self.phase1 += 0.1
            data = 0.8 * np.sin(2*np.pi*1000*self.t + self.phase1)
        else:
            self.phase2 += 0.05
            data = 0.6 * np.cos(2*np.pi*1500*self.t + self.phase2)
        # normalize similar to preprocess
        data = data - np.mean(data)
        m = np.max(np.abs(data))
        if m > 0:
            data = data / m
        return data.astype(np.float32)

scope = MockScope()

def handle_client(conn, addr):
    print(f"Client connected: {addr}")
    conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    alive = True
    streaming_channels = set()  # channels currently streaming to this client

    # Send immediate hello/status so client sees a first line even before streaming
    try:
        hello = {"type": "status", "message": "hello", "channels_active": []}
        conn.sendall((json.dumps(hello) + "\n").encode("utf-8"))
    except Exception:
        pass

    def send_json_safe(obj):
        nonlocal alive
        try:
            data = (json.dumps(obj) + "\n").encode("utf-8")
            conn.sendall(data)
        except Exception as e:
            # Connection likely closed by client
            alive = False

    def streaming_loop():
        # Push frames at ~20 Hz for any active channel
        packet_counts = {}
        last_log = time.time()
        while alive:
            if streaming_channels:
                for ch in list(streaming_channels):
                    try:
                        data = scope.read_channel(ch)
                        pkt = {
                            "type": "waveform",
                            "channel": ch,
                            "data": data.tolist(),
                            "timestamp": time.time(),
                            "sample_rate": float(scope.sample_rate)
                        }
                        send_json_safe(pkt)
                        packet_counts[ch] = packet_counts.get(ch, 0) + 1
                    except Exception as e:
                        if VERBOSE:
                            print(f"Channel {ch} send error: {e}")
                if VERBOSE and (time.time() - last_log) > 1.0:
                    for ch in streaming_channels:
                        # basic stats on latest data
                        try:
                            sample = scope.read_channel(ch)
                            mn = float(np.min(sample))
                            mx = float(np.max(sample))
                            print(f"[SERVER] ch={ch} packets={packet_counts.get(ch,0)} last_min={mn:.3f} last_max={mx:.3f}")
                        except Exception:
                            pass
                    last_log = time.time()
            time.sleep(0.05)

    thread = threading.Thread(target=streaming_loop, daemon=True)
    thread.start()

    # Read line-delimited JSON commands
    try:
        with conn, conn.makefile('r', encoding='utf-8', newline='\n') as f:
            while alive:
                line = f.readline()
                if not line:
                    break
                try:
                    cmd = json.loads(line)
                except Exception:
                    continue
                if VERBOSE:
                    print(f"[SERVER] Received command: {cmd}")
                ctype = cmd.get("command")
                ch = int(cmd.get("channel", 1))
                if ctype == "stream":
                    streaming_channels.add(ch)
                    if VERBOSE:
                        print(f"[SERVER] STREAM START ch={ch} from {addr}")
                    send_json_safe({"type":"status","message":"stream started","channel":ch})
                elif ctype == "stop_stream":
                    streaming_channels.discard(ch)
                    send_json_safe({"type":"status","message":"stream stopped","channel":ch})
                elif ctype == "fft":
                    data = scope.read_channel(ch)
                    freqs = np.fft.rfftfreq(len(data), 1.0/scope.sample_rate)
                    mag = np.abs(np.fft.rfft(data))
                    mag_db = 20*np.log10(mag + 1e-12)
                    send_json_safe({
                        "type":"fft",
                        "channel": ch,
                        "frequencies": freqs.astype(np.float32).tolist(),
                        "magnitude_db": mag_db.astype(np.float32).tolist(),
                        "magnitude_linear": mag.astype(np.float32).tolist(),
                        "window": cmd.get("window", "hann"),
                        "sample_rate": float(scope.sample_rate)
                    })
                elif ctype == "freeze":
                    freeze = bool(cmd.get("freeze", True))
                    resp = {"type":"freeze_response","channel":ch,"frozen":freeze}
                    if freeze:
                        resp["buffer"] = scope.read_channel(ch).tolist()
                    send_json_safe(resp)
                else:
                    if VERBOSE:
                        print(f"[SERVER] Unknown command: {cmd}")
                    send_json_safe({"type":"error","message":f"Unknown command {ctype}"})
    except Exception as e:
        print(f"Client error: {e}")

    print(f"Client disconnected: {addr}")


# Note: process_command/start_streaming merged into handle_client above for robustness


def main():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((HOST, PORT))
        s.listen(5)
        print(f"TCP server listening on {HOST}:{PORT}")
        while True:
            conn, addr = s.accept()
            threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()
