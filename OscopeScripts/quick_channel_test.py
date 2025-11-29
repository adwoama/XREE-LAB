#!/usr/bin/env python3
"""
Quick channel test - connects to scope and reads channels 1 & 2
"""
import pyvisa
import numpy as np

def quick_test():
    scope_ip = "169.254.208.205"
    visa_address = f"TCPIP0::{scope_ip}::inst0::INSTR"
    
    rm = pyvisa.ResourceManager()
    
    print(f"Connecting to scope at {scope_ip}...")
    try:
        scope = rm.open_resource(visa_address)
        scope.timeout = 20000  # 20 second timeout
        print("Resource opened, sending *IDN? query...")
        
        # Identify
        idn = scope.query("*IDN?")
        print(f"Connected: {idn.strip()}\n")
        
        # Test channels 1 & 2
        for ch in [1, 2]:
            print(f"--- Channel {ch} ---")
            
            # Check if displayed
            disp = scope.query(f":CHANnel{ch}:DISPlay?").strip()
            print(f"Display: {'ON' if disp == '1' else 'OFF'}")
            
            if disp == '0':
                print(f"⚠️  Channel {ch} is OFF - turn it on to see data\n")
                continue
            
            # Get vertical scale
            vscale = scope.query(f":CHANnel{ch}:SCALe?").strip()
            print(f"V/div: {vscale}")
            
            # Get waveform data
            try:
                scope.write(f":WAVeform:SOURce CHANnel{ch}")
                scope.write(":WAVeform:FORMat BYTE")
                scope.write(":WAVeform:BYTeorder LSBFirst")
                scope.write(":WAVeform:POINts:MODE NORMal")  # Normal point mode
                scope.write(":WAVeform:POINts 1000")  # Request 1000 points max
                
                # Get preamble for scaling
                preamble = scope.query(":WAVeform:PREamble?").split(',')
                x_increment = float(preamble[4])
                x_origin = float(preamble[5])
                y_increment = float(preamble[7])
                y_origin = float(preamble[8])
                y_reference = float(preamble[9])
                
                # Read data
                scope.timeout = 3000  # Shorter timeout
                print("Reading waveform data...", end=" ", flush=True)
                raw_data = scope.query_binary_values(":WAVeform:DATA?", datatype='B', container=np.array)
                scope.timeout = 20000
                print("done")
                
                # Convert to voltage
                voltage = (raw_data - y_reference) * y_increment + y_origin
                
                print(f"Samples: {len(voltage)}")
                print(f"Min: {voltage.min():.4f}V")
                print(f"Max: {voltage.max():.4f}V")
                print(f"Mean: {voltage.mean():.4f}V")
                print(f"Pk-Pk: {voltage.max() - voltage.min():.4f}V")
                
                # Time info
                time = np.arange(len(voltage)) * x_increment + x_origin
                print(f"Time span: {time[-1]*1e6:.2f} µs")
                print(f"Sample rate: {1/x_increment/1e6:.2f} MSa/s\n")
                
            except Exception as e:
                print(f"✗ Data read error: {e}\n")
        
        # Return to local
        scope.write(":SYSTem:LOCal")
        scope.close()
        print("✓ Test complete - scope returned to local control")
        
    except Exception as e:
        print(f"✗ Connection failed: {e}")
        return False
    
    return True

if __name__ == "__main__":
    quick_test()
