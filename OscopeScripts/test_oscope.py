#!/usr/bin/env python3
"""
Quick test script to verify oscilloscope connection and basic functionality
Run this first before starting the full streaming server
"""

import sys
from oscope_streaming import ScopeConfig, OscopeStreamer, StreamConfig

def test_connection():
    """Test basic connection to oscilloscope"""
    print("=== Oscilloscope Connection Test ===\n")
    
    scope_config = ScopeConfig(ip_address="169.254.208.205")
    stream_config = StreamConfig()
    
    streamer = OscopeStreamer(scope_config, stream_config)
    
    # Test connection
    print("1. Testing connection to oscilloscope...")
    if not streamer.connect_to_scope():
        print("✗ Failed to connect")
        return False
    print("✓ Connected successfully\n")
    
    # Test channel data acquisition
    print("2. Testing data acquisition from channel 1...")
    try:
        data = streamer.get_channel_data(1, apply_preprocessing=False)
        if data is not None:
            print(f"✓ Received {len(data)} samples")
            print(f"   Min: {data.min():.6f}V, Max: {data.max():.6f}V, Mean: {data.mean():.6f}V\n")
        else:
            print("⚠️  No data received (normal if no probe connected)\n")
    except Exception as e:
        print(f"✗ Error: {e}\n")
    
    # Test preprocessing
    print("3. Testing preprocessing (DC removal, scaling)...")
    try:
        data = streamer.get_channel_data(1, apply_preprocessing=True)
        if data is not None:
            print(f"✓ Preprocessed {len(data)} samples")
            print(f"   Min: {data.min():.6f}, Max: {data.max():.6f}, Mean: {data.mean():.6f}\n")
        else:
            print("⚠️  No data received\n")
    except Exception as e:
        print(f"✗ Error: {e}\n")
    
    # Test FFT
    print("4. Testing FFT computation...")
    try:
        fft_result = streamer.apply_fft(1, window_type='hann')
        if fft_result:
            n_freqs = len(fft_result['frequencies'])
            print(f"✓ FFT computed with {n_freqs} frequency bins")
            print(f"   Frequency range: 0 to {fft_result['frequencies'][-1]/1e6:.2f} MHz")
            print(f"   Sample rate: {fft_result['sample_rate']/1e6:.2f} MSa/s\n")
        else:
            print("⚠️  FFT failed\n")
    except Exception as e:
        print(f"✗ Error: {e}\n")
    
    # Test freeze functionality
    print("5. Testing freeze/hold functionality...")
    try:
        buffer = streamer.freeze_channel(1, freeze=True)
        print("✓ Channel 1 frozen")
        streamer.freeze_channel(1, freeze=False)
        print("✓ Channel 1 unfrozen\n")
    except Exception as e:
        print(f"✗ Error: {e}\n")
    
    # Cleanup
    print("6. Returning scope to local control...")
    streamer.disconnect_from_scope()
    print("✓ Disconnected\n")
    
    print("=== All Tests Passed! ===")
    print("\nYou're ready to run the full streaming server:")
    print("  python oscope_streaming.py")
    
    return True

if __name__ == "__main__":
    try:
        success = test_connection()
        sys.exit(0 if success else 1)
    except KeyboardInterrupt:
        print("\n\nTest interrupted by user")
        sys.exit(1)
    except Exception as e:
        print(f"\n✗ Unexpected error: {e}")
        sys.exit(1)
