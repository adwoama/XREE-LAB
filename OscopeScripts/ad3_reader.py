"""
Analog Discovery 3 Reader
Connects to Digilent AD3 via WaveForms SDK and streams analog input data.
Provides a similar interface to pyvisa scope for easy integration.
"""

import time
import numpy as np
import ctypes
import sys
from os import sep

# Pre-load the WaveForms SDK DLL so pydwf can find it
HAVE_PYDWF = False
try:
    if sys.platform.startswith("win"):
        # On Windows, load dwf.dll from standard install location
        try:
            dwf_dll = ctypes.cdll.dwf  # Try system PATH first
        except OSError:
            # Fallback to default WaveForms SDK install path
            sdk_path = f"C:{sep}Program Files (x86){sep}Digilent{sep}WaveFormsSDK{sep}samples{sep}py"
            dll_path = f"C:{sep}Program Files (x86){sep}Digilent{sep}WaveFormsSDK{sep}lib{sep}x64{sep}dwf.dll"
            try:
                dwf_dll = ctypes.cdll.LoadLibrary(dll_path)
            except OSError:
                # Try alternate 32-bit path
                dll_path = f"C:{sep}Program Files (x86){sep}Digilent{sep}WaveFormsSDK{sep}lib{sep}x86{sep}dwf.dll"
                dwf_dll = ctypes.cdll.LoadLibrary(dll_path)
    
    # Now import pydwf (it will use the loaded DLL)
    from pydwf import DwfLibrary
    HAVE_PYDWF = True
except Exception as e:
    HAVE_PYDWF = False
    print(f"[AD3] Failed to load WaveForms SDK or pydwf: {e}")
    print("[AD3] Ensure WaveForms application is installed from Digilent")

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
        self.is_mock = False
        
        self.dwf = DwfLibrary()
        self.device = None
        self._dwf = dwf_dll  # raw ctypes DLL for compatibility path
        self._handle = ctypes.c_int()
        # Define function prototypes for raw SDK calls to ensure correct pointer types
        try:
            # Common analog-in config/status APIs
            self._dwf.FDwfAnalogInStatusRecord.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)]
            self._dwf.FDwfAnalogInStatusRecord.restype = ctypes.c_int
            self._dwf.FDwfAnalogInStatusData.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_double), ctypes.c_int]
            self._dwf.FDwfAnalogInStatusData.restype = ctypes.c_int
            self._dwf.FDwfAnalogInStatusSamplesValid.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
            self._dwf.FDwfAnalogInStatusSamplesValid.restype = ctypes.c_int
            self._dwf.FDwfAnalogInBufferSizeSet.argtypes = [ctypes.c_int, ctypes.c_int]
            self._dwf.FDwfAnalogInBufferSizeSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInStatus.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
            self._dwf.FDwfAnalogInStatus.restype = ctypes.c_int
            self._dwf.FDwfAnalogInConfigure.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
            self._dwf.FDwfAnalogInConfigure.restype = ctypes.c_int
            self._dwf.FDwfAnalogInAcquisitionModeSet.argtypes = [ctypes.c_int, ctypes.c_int]
            self._dwf.FDwfAnalogInAcquisitionModeSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInChannelEnableSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
            self._dwf.FDwfAnalogInChannelEnableSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInChannelRangeSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_double]
            self._dwf.FDwfAnalogInChannelRangeSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInFrequencySet.argtypes = [ctypes.c_int, ctypes.c_double]
            self._dwf.FDwfAnalogInFrequencySet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInTriggerSourceSet.argtypes = [ctypes.c_int, ctypes.c_int]
            self._dwf.FDwfAnalogInTriggerSourceSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInTriggerAutoTimeoutSet.argtypes = [ctypes.c_int, ctypes.c_double]
            self._dwf.FDwfAnalogInTriggerAutoTimeoutSet.restype = ctypes.c_int
            self._dwf.FDwfAnalogInChannelOffsetSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_double]
            self._dwf.FDwfAnalogInChannelOffsetSet.restype = ctypes.c_int
            self._dwf.FDwfGetLastErrorMsg.argtypes = [ctypes.c_char_p]
            self._dwf.FDwfGetLastErrorMsg.restype = ctypes.c_int
            self._dwf.FDwfDeviceOpen.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
            self._dwf.FDwfDeviceOpen.restype = ctypes.c_int
            self._dwf.FDwfDeviceClose.argtypes = [ctypes.c_int]
            self._dwf.FDwfDeviceClose.restype = ctypes.c_int
        except Exception:
            pass
        self.is_running = False
        
    def connect(self):
        """Open first available AD3 device and configure analog input."""
        print("[AD3] Opening device...")
        # Try pydwf high-level open first; if not available, use raw DLL
        try:
            self.device = self.dwf.device.open(-1)
        except Exception:
            # Fallback to raw SDK
            if not self._dwf:
                raise RuntimeError("WaveForms SDK DLL not loaded")
            if self._dwf.FDwfDeviceOpen(ctypes.c_int(-1), ctypes.byref(self._handle)) != 0:
                pass
            else:
                raise RuntimeError("No Analog Discovery device found. Check USB connection.")

        print("[AD3] Connected")

        # Configure via raw DLL for wide compatibility
        # Reset
        try:
            self.device.analogIn.reset()
            use_high_level = True
        except Exception:
            use_high_level = False

        if use_high_level:
            for ch in [0, 1]:
                self.device.analogIn.channelEnableSet(ch, True)
                self.device.analogIn.channelRangeSet(ch, self.voltage_range)
                self.device.analogIn.channelOffsetSet(ch, 0.0)
            # Set trigger to auto mode (like WaveForms UI)
            try:
                hdwf = self.device.analogIn.hdwf
                self._dwf.FDwfAnalogInTriggerSourceSet(hdwf, ctypes.c_int(0))  # trigsrcNone = 0
                self._dwf.FDwfAnalogInTriggerAutoTimeoutSet(hdwf, ctypes.c_double(0.0))  # immediate
            except Exception:
                pass
            # Use ScanShift to continuously shift-in newest data
            try:
                self.device.analogIn.bufferSizeSet(self.buffer_size)
            except Exception:
                pass
            self.device.analogIn.acquisitionModeSet(self.dwf.DwfAcquisitionMode.ScanShift)
            self.device.analogIn.frequencySet(self.sample_rate)
        else:
            # Channel enable
            self._dwf.FDwfAnalogInChannelEnableSet(self._handle, ctypes.c_int(0), ctypes.c_int(1))
            self._dwf.FDwfAnalogInChannelEnableSet(self._handle, ctypes.c_int(1), ctypes.c_int(1))
            # Range and offset
            self._dwf.FDwfAnalogInChannelRangeSet(self._handle, ctypes.c_int(0), ctypes.c_double(self.voltage_range))
            self._dwf.FDwfAnalogInChannelRangeSet(self._handle, ctypes.c_int(1), ctypes.c_double(self.voltage_range))
            self._dwf.FDwfAnalogInChannelOffsetSet(self._handle, ctypes.c_int(0), ctypes.c_double(0.0))
            self._dwf.FDwfAnalogInChannelOffsetSet(self._handle, ctypes.c_int(1), ctypes.c_double(0.0))
            # Disable trigger to acquire immediately
            self._dwf.FDwfAnalogInTriggerSourceSet(self._handle, ctypes.c_int(0))  # trigsrcNone
            # Frequency and record length
            self._dwf.FDwfAnalogInFrequencySet(self._handle, ctypes.c_double(self.sample_rate))
            # Set buffer size for scan-shift style streaming
            self._dwf.FDwfAnalogInBufferSizeSet(self._handle, ctypes.c_int(self.buffer_size))
            # Acquisition mode: 1 = ScanShift (enum order: 0=Single,1=ScanShift,2=ScanScreen,3=Record)
            self._dwf.FDwfAnalogInAcquisitionModeSet(self._handle, ctypes.c_int(1))
            # Trigger auto-timeout
            self._dwf.FDwfAnalogInTriggerSourceSet(self._handle, ctypes.c_int(0))
            self._dwf.FDwfAnalogInTriggerAutoTimeoutSet(self._handle, ctypes.c_double(0.0))

        print(f"[AD3] Configured: {self.sample_rate/1e6:.2f} MSa/s, buffer={self.buffer_size}, range=±{self.voltage_range}V")
        
    def start_streaming(self):
        """Start continuous acquisition."""
        if self.device is None and self._handle.value == 0:
            raise RuntimeError("Device not connected. Call connect() first.")
        try:
            self.device.analogIn.configure(False, True)
        except Exception:
            # Reconfigure and start
            self._dwf.FDwfAnalogInConfigure(self._handle, ctypes.c_int(1), ctypes.c_int(1))
        self.is_running = True
        print("[AD3] Streaming started")
        
    def stop_streaming(self):
        """Stop acquisition."""
        if self.device:
            try:
                self.device.analogIn.configure(False, False)
            except Exception:
                pass
            self.is_running = False
            print("[AD3] Streaming stopped")
    
    def read_channels(self, timeout=2.0):
        """
        Fetch latest samples from both channels.
        Returns: (ch1_data, ch2_data) as numpy arrays normalized to [-1, 1]
        """
        if not self.is_running:
            return None, None
        
        # Poll status and fetch samples via available API
        start = time.time()
        if self.device and hasattr(self.device, 'analogIn'):
            while time.time() - start < timeout:
                try:
                    _ = self.device.analogIn.status(True)
                    available, lost, corrupt = self.device.analogIn.statusRecord()
                    if available >= self.buffer_size:
                        break
                except Exception:
                    break
                time.sleep(0.01)
            else:
                print(f"[AD3] Timeout waiting for {self.buffer_size} samples")
                return None, None
            ch1 = self.device.analogIn.statusData(0, self.buffer_size)
            ch2 = self.device.analogIn.statusData(1, self.buffer_size)
        else:
            st = ctypes.c_int()
            # Trigger a new status read; for scan-shift we can read the latest buffer directly
            if self._dwf.FDwfAnalogInStatus(self._handle, ctypes.c_int(1), ctypes.byref(st)) == 0:
                self._log_last_error("FDwfAnalogInStatus")
            valid = ctypes.c_int()
            if self._dwf.FDwfAnalogInStatusSamplesValid(self._handle, ctypes.byref(valid)) == 0:
                self._log_last_error("FDwfAnalogInStatusSamplesValid")
            print(f"[AD3][RAW] status={st.value} samplesValid={valid.value}")
            ch1_buf = (ctypes.c_double * self.buffer_size)()
            ch2_buf = (ctypes.c_double * self.buffer_size)()
            if self._dwf.FDwfAnalogInStatusData(self._handle, ctypes.c_int(0), ch1_buf, ctypes.c_int(self.buffer_size)) == 0:
                self._log_last_error("FDwfAnalogInStatusData ch0")
            if self._dwf.FDwfAnalogInStatusData(self._handle, ctypes.c_int(1), ch2_buf, ctypes.c_int(self.buffer_size)) == 0:
                self._log_last_error("FDwfAnalogInStatusData ch1")
            ch1 = list(ch1_buf)
            ch2 = list(ch2_buf)
            print(f"[AD3][RAW] ch1 head={ch1[:3]} ch2 head={ch2[:3]}")
        
        # Normalize to [-1, 1] to match Keysight format: scale by voltage range
        # DO NOT remove DC offset - we want to preserve actual voltage levels
        ch1_arr = np.array(ch1, dtype=np.float32)
        ch2_arr = np.array(ch2, dtype=np.float32)
        # Scale by voltage range: divide by half-range so ±voltage_range maps to ±1
        ch1_norm = ch1_arr / self.voltage_range
        ch2_norm = ch2_arr / self.voltage_range
        
        return ch1_norm, ch2_norm

    def _log_last_error(self, where: str):
        try:
            buf = ctypes.create_string_buffer(512)
            if hasattr(self._dwf, 'FDwfGetLastErrorMsg'):
                self._dwf.FDwfGetLastErrorMsg(buf)
                print(f"[AD3][ERR] {where}: {buf.value.decode(errors='ignore')}")
        except Exception:
            pass
    
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
        self.is_mock = True
        
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
