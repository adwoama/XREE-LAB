# Interface with a Keysight InfiniiVision MSOX604A Mixed Signal Oscilloscope 20 GSa/s
import pyvisa
import subprocess
import platform

def ping_test(ip_address):
    """Test network connectivity to the oscilloscope"""
    param = '-n' if platform.system().lower() == 'windows' else '-c'
    command = ['ping', param, '1', ip_address]
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=5)
        return result.returncode == 0
    except:
        return False

def main():
    rm = pyvisa.ResourceManager()
    
    # Use your scope's VISA address - update this if the IP changes
    scope_ip = "169.254.208.205"
    visa_address = f"TCPIP0::{scope_ip}::inst0::INSTR"
    
    # Test network connectivity first
    print(f"Testing network connectivity to {scope_ip}...")
    if ping_test(scope_ip):
        print("✓ Ping successful - scope is reachable on network\n")
    else:
        print("✗ Ping failed - scope is not reachable")
        print("  Check that the scope is powered on and connected to the network")
        print(f"  Verify the IP address is still {scope_ip} in the scope's LAN settings")
        return
    
    try:
        scope = rm.open_resource(visa_address)
        # Increase timeout to 10 seconds
        scope.timeout = 10000
        # Put scope in remote mode (disables front panel)
        scope.write(":SYSTem:REMote")
    except Exception as e:
        print(f"Error opening connection: {e}")
        print("\nTroubleshooting tips:")
        print("1. Check if the oscilloscope is powered on")
        print("2. Verify network connectivity - try pinging 169.254.217.89")
        print("3. Check the oscilloscope's IP address in its network settings")
        print("4. Ensure no firewall is blocking the connection")
        return
    
    # Identify the instrument
    try:
        idn = scope.query("*IDN?")
        print("Connected to:", idn)
        print("\n✓ Communication successful! No probes needed for basic connectivity.")
    except pyvisa.errors.VisaIOError as e:
        print(f"Communication error: {e}")
        print("\nThe connection was established but the scope isn't responding.")
        print("Check the scope's network settings and ensure SCPI communication is enabled.")
        scope.close()
        return
    
    # Check which channels are active
    active_channels = []
    for ch in range(1, 5):  # MSOX604A has 4 analog channels
        response = scope.query(f":CHANnel{ch}:DISPlay?")
        if response.strip() == "1":
            active_channels.append(ch)
    
    print("Active channels:", active_channels)
    
    # Read waveform data from each active channel
    if active_channels:
        print("\nAttempting to read waveform data...")
        for ch in active_channels:
            try:
                scope.write(f":WAV:SOUR CHAN{ch}")
                scope.write(":WAV:FORM ASCii")  # ASCII format for simplicity
                # Temporarily reduce timeout for waveform query
                original_timeout = scope.timeout
                scope.timeout = 3000  # 3 seconds
                data = scope.query(":WAV:DATA?")
                scope.timeout = original_timeout
                print(f"Channel {ch} data (first 200 chars):")
                print(data[:200])  # Print a snippet of the waveform data
            except pyvisa.errors.VisaIOError:
                scope.timeout = original_timeout  # Restore timeout
                print(f"✗ Channel {ch}: No signal detected (normal without probes connected)")
    else:
        print("Note: No channels are currently active/displayed on the scope")
    
    # Return scope to local mode so front panel buttons work
    try:
        scope.write(":SYSTem:LOCal")
        print("\n✓ Connection test complete! Scope returned to local control.")
    except:
        print("\n✓ Connection test complete! (Note: Scope may still be in remote mode - press Local button on panel)")
    finally:
        scope.close()

if __name__ == "__main__":
    main()