"""
Standalone AD3 sanity check - directly test acquisition without the server.
Prints raw buffer samples, status codes, and SDK messages.
"""
import ctypes
import time
import sys
from os import sep

import platform
arch = platform.architecture()[0]
if sys.platform.startswith("win"):
    if arch == "64bit":
        dll_path = f"C:{sep}Program Files (x86){sep}Digilent{sep}WaveFormsSDK{sep}lib{sep}x64{sep}dwf.dll"
    else:
        dll_path = f"C:{sep}Program Files (x86){sep}Digilent{sep}WaveFormsSDK{sep}lib{sep}x86{sep}dwf.dll"
    print(f"[SANITY] Python architecture: {arch}")
    print(f"[SANITY] Attempting to load DLL from: {dll_path}")
    try:
        dwf = ctypes.cdll.LoadLibrary(dll_path)
        print("[SANITY] DLL loaded from explicit path.")
    except OSError as e:
        print(f"[SANITY][WARN] Could not load DLL from explicit path: {e}")
        try:
            dwf = ctypes.cdll.dwf
            print("[SANITY] DLL loaded from system PATH.")
        except Exception as e2:
            print(f"[SANITY][FATAL] Cannot load DLL: {e2}")
            sys.exit(1)
else:
    print("[SANITY][FATAL] Non-Windows platform detected. DLL loading not supported.")
    sys.exit(1)

# Define prototypes
dwf.FDwfDeviceOpen.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
dwf.FDwfDeviceOpen.restype = ctypes.c_int
dwf.FDwfDeviceClose.argtypes = [ctypes.c_int]
dwf.FDwfDeviceClose.restype = ctypes.c_int
dwf.FDwfGetLastErrorMsg.argtypes = [ctypes.c_char_p]
dwf.FDwfGetLastErrorMsg.restype = ctypes.c_int

# Device enumeration
dwf.FDwfEnum.argtypes = [ctypes.c_int]
dwf.FDwfEnum.restype = ctypes.c_int
dwf.FDwfEnumDeviceType.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)]
dwf.FDwfEnumDeviceType.restype = ctypes.c_int
dwf.FDwfEnumDeviceIsOpened.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
dwf.FDwfEnumDeviceIsOpened.restype = ctypes.c_int

# Analog output (for demo signals)
dwf.FDwfAnalogOutNodeEnableSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogOutNodeEnableSet.restype = ctypes.c_int
dwf.FDwfAnalogOutNodeFunctionSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogOutNodeFunctionSet.restype = ctypes.c_int
dwf.FDwfAnalogOutNodeFrequencySet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogOutNodeFrequencySet.restype = ctypes.c_int
dwf.FDwfAnalogOutNodeAmplitudeSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogOutNodeAmplitudeSet.restype = ctypes.c_int
dwf.FDwfAnalogOutNodeOffsetSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogOutNodeOffsetSet.restype = ctypes.c_int
dwf.FDwfAnalogOutConfigure.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogOutConfigure.restype = ctypes.c_int

dwf.FDwfAnalogInChannelEnableSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogInChannelEnableSet.restype = ctypes.c_int
dwf.FDwfAnalogInChannelRangeSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogInChannelRangeSet.restype = ctypes.c_int
dwf.FDwfAnalogInChannelOffsetSet.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogInChannelOffsetSet.restype = ctypes.c_int
dwf.FDwfAnalogInFrequencySet.argtypes = [ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogInFrequencySet.restype = ctypes.c_int
dwf.FDwfAnalogInBufferSizeSet.argtypes = [ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogInBufferSizeSet.restype = ctypes.c_int
dwf.FDwfAnalogInAcquisitionModeSet.argtypes = [ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogInAcquisitionModeSet.restype = ctypes.c_int
dwf.FDwfAnalogInTriggerSourceSet.argtypes = [ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogInTriggerSourceSet.restype = ctypes.c_int
dwf.FDwfAnalogInTriggerAutoTimeoutSet.argtypes = [ctypes.c_int, ctypes.c_double]
dwf.FDwfAnalogInTriggerAutoTimeoutSet.restype = ctypes.c_int
dwf.FDwfAnalogInConfigure.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
dwf.FDwfAnalogInConfigure.restype = ctypes.c_int
dwf.FDwfAnalogInStatus.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
dwf.FDwfAnalogInStatus.restype = ctypes.c_int
dwf.FDwfAnalogInStatusSamplesValid.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
dwf.FDwfAnalogInStatusSamplesValid.restype = ctypes.c_int
dwf.FDwfAnalogInStatusData.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_double), ctypes.c_int]
dwf.FDwfAnalogInStatusData.restype = ctypes.c_int

def check_error(msg):
    buf = ctypes.create_string_buffer(512)
    dwf.FDwfGetLastErrorMsg(buf)
    err = buf.value.decode(errors='ignore')
    if err:
        print(f"[SANITY][ERR] {msg}: {err}")
    return err

# Open device
hdwf = ctypes.c_int()
print("[SANITY] Enumerating devices...")
num_devices = dwf.FDwfEnum(ctypes.c_int(0))  # enumfilter=0 (all)
print(f"[SANITY] Found {num_devices} device(s)")

# Try to find demo device or use first available
device_idx = -1
for i in range(num_devices):
    devid = ctypes.c_int()
    devver = ctypes.c_int()
    dwf.FDwfEnumDeviceType(i, ctypes.byref(devid), ctypes.byref(devver))
    is_opened = ctypes.c_int()
    dwf.FDwfEnumDeviceIsOpened(i, ctypes.byref(is_opened))
    print(f"[SANITY]   Device {i}: ID={devid.value} Ver={devver.value} Opened={is_opened.value}")
    if device_idx == -1 and is_opened.value == 0:
        device_idx = i

if device_idx == -1:
    device_idx = -1  # Use default

print(f"[SANITY] Opening device index {device_idx}...")
if dwf.FDwfDeviceOpen(ctypes.c_int(device_idx), ctypes.byref(hdwf)) == 0 or hdwf.value == 0:
    check_error("FDwfDeviceOpen")
    print("[SANITY][FATAL] Cannot open device")
    sys.exit(1)
print(f"[SANITY] Device opened: handle={hdwf.value}")

# Configure analog outputs to generate demo signals (like WaveForms does)
print("[SANITY] Configuring analog output demo signals...")
# Channel 0: Square wave ~1kHz, 2Vpp
dwf.FDwfAnalogOutNodeEnableSet(hdwf, ctypes.c_int(0), ctypes.c_int(0), ctypes.c_int(1))  # ch0, carrier, enable
dwf.FDwfAnalogOutNodeFunctionSet(hdwf, ctypes.c_int(0), ctypes.c_int(0), ctypes.c_int(1))  # square=1
dwf.FDwfAnalogOutNodeFrequencySet(hdwf, ctypes.c_int(0), ctypes.c_int(0), ctypes.c_double(1000.0))
dwf.FDwfAnalogOutNodeAmplitudeSet(hdwf, ctypes.c_int(0), ctypes.c_int(0), ctypes.c_double(1.0))
dwf.FDwfAnalogOutNodeOffsetSet(hdwf, ctypes.c_int(0), ctypes.c_int(0), ctypes.c_double(0.0))
dwf.FDwfAnalogOutConfigure(hdwf, ctypes.c_int(0), ctypes.c_int(1))  # start

# Channel 1: Sine wave ~1kHz, 2Vpp
dwf.FDwfAnalogOutNodeEnableSet(hdwf, ctypes.c_int(1), ctypes.c_int(0), ctypes.c_int(1))
dwf.FDwfAnalogOutNodeFunctionSet(hdwf, ctypes.c_int(1), ctypes.c_int(0), ctypes.c_int(0))  # sine=0
dwf.FDwfAnalogOutNodeFrequencySet(hdwf, ctypes.c_int(1), ctypes.c_int(0), ctypes.c_double(1000.0))
dwf.FDwfAnalogOutNodeAmplitudeSet(hdwf, ctypes.c_int(1), ctypes.c_int(0), ctypes.c_double(1.0))
dwf.FDwfAnalogOutNodeOffsetSet(hdwf, ctypes.c_int(1), ctypes.c_int(0), ctypes.c_double(0.0))
dwf.FDwfAnalogOutConfigure(hdwf, ctypes.c_int(1), ctypes.c_int(1))

print("[SANITY] Demo signals started")

# Configure
BUFFER_SIZE = 1000
SAMPLE_RATE = 1e6
VOLTAGE_RANGE = 5.0

print("[SANITY] Configuring channels...")
for ch in [0, 1]:
    if dwf.FDwfAnalogInChannelEnableSet(hdwf, ctypes.c_int(ch), ctypes.c_int(1)) == 0:
        check_error(f"ChannelEnableSet ch{ch}")
    if dwf.FDwfAnalogInChannelRangeSet(hdwf, ctypes.c_int(ch), ctypes.c_double(VOLTAGE_RANGE)) == 0:
        check_error(f"ChannelRangeSet ch{ch}")
    if dwf.FDwfAnalogInChannelOffsetSet(hdwf, ctypes.c_int(ch), ctypes.c_double(0.0)) == 0:
        check_error(f"ChannelOffsetSet ch{ch}")

print(f"[SANITY] Setting frequency={SAMPLE_RATE/1e6:.2f} MSa/s")
if dwf.FDwfAnalogInFrequencySet(hdwf, ctypes.c_double(SAMPLE_RATE)) == 0:
    check_error("FrequencySet")

print(f"[SANITY] Setting buffer size={BUFFER_SIZE}")
if dwf.FDwfAnalogInBufferSizeSet(hdwf, ctypes.c_int(BUFFER_SIZE)) == 0:
    check_error("BufferSizeSet")

print("[SANITY] Setting acquisition mode to ScanShift (1)")
if dwf.FDwfAnalogInAcquisitionModeSet(hdwf, ctypes.c_int(1)) == 0:
    check_error("AcquisitionModeSet")

print("[SANITY] Setting trigger source to None (0)")
if dwf.FDwfAnalogInTriggerSourceSet(hdwf, ctypes.c_int(0)) == 0:
    check_error("TriggerSourceSet")

print("[SANITY] Setting trigger auto-timeout to 0 (immediate)")
if dwf.FDwfAnalogInTriggerAutoTimeoutSet(hdwf, ctypes.c_double(0.0)) == 0:
    check_error("TriggerAutoTimeoutSet")

print("[SANITY] Starting acquisition (reconfigure=1, start=1)...")
if dwf.FDwfAnalogInConfigure(hdwf, ctypes.c_int(1), ctypes.c_int(1)) == 0:
    check_error("Configure")
    print("[SANITY][FATAL] Configure failed")
    dwf.FDwfDeviceClose(hdwf)
    sys.exit(1)

print("[SANITY] Acquisition started. Reading 10 buffers...\n")

for i in range(10):
    time.sleep(0.1)
    
    # Trigger status read
    st = ctypes.c_int()
    if dwf.FDwfAnalogInStatus(hdwf, ctypes.c_int(1), ctypes.byref(st)) == 0:
        check_error(f"Status iter={i}")
        continue
    
    # Check samples valid
    valid = ctypes.c_int()
    if dwf.FDwfAnalogInStatusSamplesValid(hdwf, ctypes.byref(valid)) == 0:
        check_error(f"SamplesValid iter={i}")
        valid.value = 0
    
    print(f"[SANITY] Buffer {i+1}: status={st.value} samplesValid={valid.value}")
    
    # Fetch data
    ch0_buf = (ctypes.c_double * BUFFER_SIZE)()
    ch1_buf = (ctypes.c_double * BUFFER_SIZE)()
    
    if dwf.FDwfAnalogInStatusData(hdwf, ctypes.c_int(0), ch0_buf, ctypes.c_int(BUFFER_SIZE)) == 0:
        check_error(f"StatusData ch0 iter={i}")
    if dwf.FDwfAnalogInStatusData(hdwf, ctypes.c_int(1), ch1_buf, ctypes.c_int(BUFFER_SIZE)) == 0:
        check_error(f"StatusData ch1 iter={i}")
    
    ch0_list = list(ch0_buf)
    ch1_list = list(ch1_buf)
    
    ch0_min = min(ch0_list)
    ch0_max = max(ch0_list)
    ch0_mean = sum(ch0_list) / len(ch0_list)
    ch0_head = ch0_list[:5]
    
    ch1_min = min(ch1_list)
    ch1_max = max(ch1_list)
    ch1_mean = sum(ch1_list) / len(ch1_list)
    ch1_head = ch1_list[:5]
    
    print(f"  CH0: min={ch0_min:.4f} max={ch0_max:.4f} mean={ch0_mean:.4f} head={[f'{x:.4f}' for x in ch0_head]}")
    print(f"  CH1: min={ch1_min:.4f} max={ch1_max:.4f} mean={ch1_mean:.4f} head={[f'{x:.4f}' for x in ch1_head]}")
    print()

print("[SANITY] Closing device...")
dwf.FDwfDeviceClose(hdwf)
print("[SANITY] Done.")
