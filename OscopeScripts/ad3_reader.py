"""
Analog Discovery 3 Reader
Connects to Digilent AD3 via WaveForms SDK and streams analog input data.
Provides a similar interface to pyvisa scope for easy integration.
"""

import time
import numpy as np

try:
    from pydwf import DwfLibrary, DwfEnumConfigInfo, DwfAnalogInTriggerSource
    HAVE_PYDWF = True
except ImportError:
    HAVE_PYDWF = False
    print("[AD3] pydwf not installed. Install: pip install pydwf")

class AD3Reader:
    """
    Simple wrapper for Analog Discovery 3 analog input streaming.
    Supports continuous acquisition on 2 channels (scope inputs 1+ and 1-).
    """
    def __init__(self, sample_rate=1e6, buffer_size=1000, voltage_range=5.0):
        """
        Args:
            sample_rate: samples/sec per channel (up to 125 MSa/s for AD3)
            buffer_size: number of samples per fetch
            voltage_range: +/- voltage range (5V, 2.5V, etc.)
        """
        if not HAVE_PYDWF:
            raise RuntimeError("pydwf library not available. Install WaveForms SDK and pydwf.")
        
        self.sample_rate = sample_rate
        self.buffer_size = buffer_size
        self.voltage_range = voltage_range
        
        self.dwf = DwfLibrary()
        self.device = None
        self.is_running = False
        
    def connect(self):
        """Open first available AD3 device and configure analog input."""
        print("[AD3] Opening device...")
        self.device = self.dwf.device.open(-1)  # -1 = first available
        if self.device is None:
            raise RuntimeError("No Analog Discovery device found. Check USB connection.")
        
        print(f"[AD3] Connected: {self.device.name}")
        
        # Reset
        self.device.analogIn.reset()
        
        # Configure channels
        for ch in [0, 1]:  # CH1, CH2
            self.device.analogIn.channelEnableSet(ch, True)
            self.device.analogIn.channelRangeSet(ch, self.voltage_range)
            self.device.analogIn.channelOffsetSet(ch, 0.0)
        
        # Acquisition mode: record (continuous streaming)
        self.device.analogIn.acquisitionModeSet(self.dwf.DwfAcquisitionMode.Record)
        
        # Sample rate and buffer
        self.device.analogIn.frequencySet(self.sample_rate)
        self.device.analogIn.recordLengthSet(self.buffer_size / self.sample_rate)  # seconds
        
        # Trigger: auto (immediate)
        self.device.analogIn.triggerSourceSet(DwfAnalogInTriggerSource.None_)
        
        print(f"[AD3] Configured: {self.sample_rate/1e6:.2f} MSa/s, buffer={self.buffer_size}, range=±{self.voltage_range}V")
        
    def start_streaming(self):
        """Start continuous acquisition."""
        if self.device is None:
            raise RuntimeError("Device not connected. Call connect() first.")
        self.device.analogIn.configure(False, True)  # reconfigure=False, start=True
        self.is_running = True
        print("[AD3] Streaming started")
        
    def stop_streaming(self):
        """Stop acquisition."""
        if self.device:
            self.device.analogIn.configure(False, False)  # stop
            self.is_running = False
            print("[AD3] Streaming stopped")
    
    def read_channels(self, timeout=2.0):
        """
        Fetch latest samples from both channels.
        Returns: (ch1_data, ch2_data) as numpy arrays normalized to [-1, 1]
        """
        if not self.is_running:
            return None, None
        
        # Poll status
        start = time.time()
        while time.time() - start < timeout:
            status = self.device.analogIn.status(True)  # read data
            available = self.device.analogIn.statusRecord()
            if available >= self.buffer_size:
                break
            time.sleep(0.01)
        else:
            print(f"[AD3] Timeout waiting for {self.buffer_size} samples")
            return None, None
        
        # Fetch samples
        ch1 = self.device.analogIn.statusData(0, self.buffer_size)
        ch2 = self.device.analogIn.statusData(1, self.buffer_size)
        
        # Normalize to [-1, 1] to match Keysight format
        ch1_norm = np.array(ch1) / self.voltage_range
        ch2_norm = np.array(ch2) / self.voltage_range
        
        return ch1_norm, ch2_norm
    
    def close(self):
        """Close device."""
        self.stop_streaming()
        if self.device:
            self.device.close()
            self.device = None
            print("[AD3] Device closed")


# Mock fallback for testing without hardware
class MockAD3Reader:
    """Generates synthetic waveforms for testing."""
    def __init__(self, sample_rate=1e6, buffer_size=1000, voltage_range=5.0):
        self.sample_rate = sample_rate
        self.buffer_size = buffer_size
        self.voltage_range = voltage_range
        self.is_running = False
        self.t = 0
        
    def connect(self):
        print("[MockAD3] Mock device connected")
        
    def start_streaming(self):
        self.is_running = True
        print("[MockAD3] Mock streaming started")
        
    def stop_streaming(self):
        self.is_running = False
        
    def read_channels(self, timeout=2.0):
        if not self.is_running:
            return None, None
        # Generate sine waves at different frequencies
        t = np.linspace(self.t, self.t + self.buffer_size / self.sample_rate, self.buffer_size)
        ch1 = 0.7 * np.sin(2 * np.pi * 5000 * t)  # 5 kHz
        ch2 = 0.5 * np.sin(2 * np.pi * 10000 * t)  # 10 kHz
        self.t += self.buffer_size / self.sample_rate
        time.sleep(0.05)  # ~20 Hz update
        return ch1, ch2
    
    def close(self):
        print("[MockAD3] Mock device closed")


def create_reader(mock=False, **kwargs):
    """Factory: returns real or mock AD3 reader."""
    if mock or not HAVE_PYDWF:
        return MockAD3Reader(**kwargs)
    return AD3Reader(**kwargs)
