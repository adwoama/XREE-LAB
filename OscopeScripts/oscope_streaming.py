# Wireless Oscilloscope Streaming System for Meta Quest 3S
# Raspberry Pi server that streams oscilloscope data to Unity headset

import pyvisa
import numpy as np
import json
import asyncio
import websockets
from scipy import signal
from scipy.fft import fft, fftfreq
from dataclasses import dataclass
from typing import Optional, Dict, List
import time

@dataclass
class ScopeConfig:
    """Configuration for oscilloscope connection"""
    ip_address: str = "169.254.208.205"
    visa_address: str = None
    timeout: int = 10000
    
    def __post_init__(self):
        if self.visa_address is None:
            self.visa_address = f"TCPIP0::{self.ip_address}::inst0::INSTR"


@dataclass
class StreamConfig:
    """Configuration for data streaming"""
    headset_ip: str = "192.168.1.183"  # UPDATE THIS with the Quest 3S IP
    port: int = 8765
    sample_rate: float = 20e9  # 20 GSa/s max for MSOX604A
    buffer_size: int = 1000  # Number of samples per transmission


class OscopeStreamer:
    """Main class for oscilloscope data acquisition and streaming"""
    
    def __init__(self, scope_config: ScopeConfig, stream_config: StreamConfig):
        self.scope_config = scope_config
        self.stream_config = stream_config
        self.scope = None
        self.rm = None
        self.streaming_channels: Dict[int, bool] = {}  # Track which channels are streaming
        self.frozen_channels: Dict[int, bool] = {}  # Track which channels are frozen
        self.last_buffers: Dict[int, np.ndarray] = {}  # Store last buffer for each channel
        self.connected_clients = set()
        
    def connect_to_scope(self) -> bool:
        """Establish connection to oscilloscope"""
        try:
            self.rm = pyvisa.ResourceManager()
            self.scope = self.rm.open_resource(self.scope_config.visa_address)
            self.scope.timeout = self.scope_config.timeout
            
            # Put scope in remote mode
            self.scope.write(":SYSTem:REMote")
            
            # Verify connection
            idn = self.scope.query("*IDN?")
            print(f"Connected to: {idn.strip()}")
            return True
            
        except Exception as e:
            print(f"Error connecting to oscilloscope: {e}")
            return False
    
    def disconnect_from_scope(self):
        """Safely disconnect from oscilloscope"""
        if self.scope:
            try:
                self.scope.write(":SYSTem:LOCal")
                self.scope.close()
                print("Oscilloscope returned to local control")
            except:
                print("Warning: Could not return scope to local mode")
    
    def get_channel_data(self, channel: int, apply_preprocessing: bool = True) -> Optional[np.ndarray]:
        """
        Acquire raw data from specified channel
        
        Args:
            channel: Channel number (1-4)
            apply_preprocessing: Whether to apply DC removal and scaling
            
        Returns:
            numpy array of voltage samples or None if error
        """
        try:
            # Set waveform source
            self.scope.write(f":WAV:SOUR CHAN{channel}")
            
            # Use BYTE format for faster transfer (can change to WORD for more precision)
            self.scope.write(":WAV:FORM BYTE")
            self.scope.write(":WAV:BYTeorder LSBFirst")
            
            # Get waveform preamble (contains scaling info)
            preamble = self.scope.query(":WAV:PRE?")
            preamble_vals = [float(x) for x in preamble.split(',')]
            
            # Extract scaling factors
            y_increment = preamble_vals[7]  # voltage per level
            y_origin = preamble_vals[8]     # voltage at center
            y_reference = preamble_vals[9]  # reference level
            
            # Acquire waveform data
            self.scope.write(":WAV:DATA?")
            raw_data = self.scope.read_raw()
            
            # Parse header and extract data
            # First 2 bytes are header (e.g., "#800001000")
            header_len = 2 + int(chr(raw_data[1]))
            data_bytes = raw_data[header_len:-1]  # Remove header and trailing newline
            
            # Convert bytes to numpy array
            data = np.frombuffer(data_bytes, dtype=np.uint8)
            
            # Convert to voltage
            voltage = (data - y_reference) * y_increment + y_origin
            
            if apply_preprocessing:
                voltage = self._preprocess_signal(voltage)
            
            # Store in buffer
            self.last_buffers[channel] = voltage
            
            return voltage
            
        except pyvisa.errors.VisaIOError as e:
            print(f"Error reading channel {channel}: {e}")
            return None
    
    def _preprocess_signal(self, signal_data: np.ndarray) -> np.ndarray:
        """
        Apply preprocessing: DC removal, scaling, optional windowing
        
        Args:
            signal_data: Raw voltage data
            
        Returns:
            Preprocessed signal
        """
        # DC removal (subtract mean)
        signal_data = signal_data - np.mean(signal_data)
        
        # Optional: Apply scaling/normalization
        max_val = np.max(np.abs(signal_data))
        if max_val > 0:
            signal_data = signal_data / max_val
        
        return signal_data
    
    def apply_fft(self, channel: int, window_type: str = 'hann') -> Dict:
        """
        Apply Fast Fourier Transform to channel data
        
        Args:
            channel: Channel number (1-4)
            window_type: Window function ('hann', 'hamming', 'blackman', 'bartlett', None)
            
        Returns:
            Dictionary containing frequency bins and magnitude spectrum
        """
        # Get channel data
        voltage = self.get_channel_data(channel, apply_preprocessing=True)
        
        if voltage is None:
            return None
        
        # Apply windowing if specified
        if window_type:
            if window_type == 'hann':
                window = np.hanning(len(voltage))
            elif window_type == 'hamming':
                window = np.hamming(len(voltage))
            elif window_type == 'blackman':
                window = np.blackman(len(voltage))
            elif window_type == 'bartlett':
                window = np.bartlett(len(voltage))
            else:
                window = np.ones(len(voltage))
            
            voltage = voltage * window
        
        # Perform FFT
        fft_result = fft(voltage)
        
        # Get actual sample rate from scope
        sample_rate = self._get_sample_rate()
        
        # Calculate frequency bins
        n_samples = len(voltage)
        freq_bins = fftfreq(n_samples, 1/sample_rate)
        
        # Only return positive frequencies
        positive_freq_idx = freq_bins >= 0
        freq_bins = freq_bins[positive_freq_idx]
        
        # Calculate magnitude spectrum (in dB)
        magnitude = np.abs(fft_result[positive_freq_idx])
        magnitude_db = 20 * np.log10(magnitude + 1e-12)  # Add small value to avoid log(0)
        
        return {
            'frequencies': freq_bins.tolist(),
            'magnitude_db': magnitude_db.tolist(),
            'magnitude_linear': magnitude.tolist(),
            'channel': channel,
            'window': window_type,
            'sample_rate': sample_rate
        }
    
    def _get_sample_rate(self) -> float:
        """Get actual sample rate from oscilloscope"""
        try:
            # Get time scale
            time_scale = float(self.scope.query(":TIMebase:SCALe?"))
            # Get number of points
            points = int(self.scope.query(":WAV:POINts?"))
            # Calculate sample rate
            total_time = time_scale * 10  # 10 divisions
            sample_rate = points / total_time
            return sample_rate
        except:
            # Return default if query fails
            return self.stream_config.sample_rate
    
    def freeze_channel(self, channel: int, freeze: bool = True):
        """
        Freeze/unfreeze channel streaming (trigger hold)
        
        Args:
            channel: Channel number (1-4)
            freeze: True to freeze, False to resume
        """
        self.frozen_channels[channel] = freeze
        status = "frozen" if freeze else "resumed"
        print(f"Channel {channel} {status}")
        
        # Return last buffer when freezing
        if freeze and channel in self.last_buffers:
            return self.last_buffers[channel]
        return None
    
    async def stream_channel(self, channel: int, websocket):
        """
        Continuously stream data from specified channel to headset
        
        Args:
            channel: Channel number (1-4)
            websocket: WebSocket connection to headset
        """
        self.streaming_channels[channel] = True
        print(f"Started streaming channel {channel}")
        
        try:
            while self.streaming_channels.get(channel, False):
                # Check if channel is frozen
                if self.frozen_channels.get(channel, False):
                    # Send frozen status, minimal bandwidth
                    await websocket.send(json.dumps({
                        'type': 'status',
                        'channel': channel,
                        'status': 'frozen'
                    }))
                    await asyncio.sleep(1)  # Check every second if still frozen
                    continue
                
                # Get fresh data
                voltage = self.get_channel_data(channel, apply_preprocessing=True)
                
                if voltage is not None:
                    # Prepare data packet
                    data_packet = {
                        'type': 'waveform',
                        'channel': channel,
                        'data': voltage.tolist(),
                        'timestamp': time.time(),
                        'sample_rate': self._get_sample_rate()
                    }
                    
                    # Send to headset
                    await websocket.send(json.dumps(data_packet))
                
                # Small delay to control streaming rate
                await asyncio.sleep(0.05)  # 20 Hz update rate
                
        except Exception as e:
            print(f"Error streaming channel {channel}: {e}")
        finally:
            self.streaming_channels[channel] = False
            print(f"Stopped streaming channel {channel}")
    
    async def handle_client_commands(self, websocket, path):
        """
        Handle incoming commands from headset (gesture triggers)
        
        Supported commands:
        - {"command": "stream", "channel": 1}
        - {"command": "stop_stream", "channel": 1}
        - {"command": "fft", "channel": 1, "window": "hann"}
        - {"command": "freeze", "channel": 1, "freeze": true}
        """
        self.connected_clients.add(websocket)
        print(f"Headset connected from {websocket.remote_address}")
        
        try:
            async for message in websocket:
                try:
                    command = json.loads(message)
                    cmd_type = command.get('command')
                    channel = command.get('channel', 1)
                    
                    if cmd_type == 'stream':
                        # Start streaming channel
                        asyncio.create_task(self.stream_channel(channel, websocket))
                        
                    elif cmd_type == 'stop_stream':
                        # Stop streaming channel
                        self.streaming_channels[channel] = False
                        
                    elif cmd_type == 'fft':
                        # Perform FFT and send result
                        window = command.get('window', 'hann')
                        fft_result = self.apply_fft(channel, window)
                        if fft_result:
                            fft_result['type'] = 'fft'
                            await websocket.send(json.dumps(fft_result))
                        
                    elif cmd_type == 'freeze':
                        # Freeze/unfreeze channel
                        freeze = command.get('freeze', True)
                        buffer_data = self.freeze_channel(channel, freeze)
                        
                        response = {
                            'type': 'freeze_response',
                            'channel': channel,
                            'frozen': freeze
                        }
                        
                        # Include last buffer if freezing
                        if freeze and buffer_data is not None:
                            response['buffer'] = buffer_data.tolist()
                        
                        await websocket.send(json.dumps(response))
                    
                    else:
                        await websocket.send(json.dumps({
                            'type': 'error',
                            'message': f'Unknown command: {cmd_type}'
                        }))
                        
                except json.JSONDecodeError:
                    await websocket.send(json.dumps({
                        'type': 'error',
                        'message': 'Invalid JSON'
                    }))
                    
        except websockets.exceptions.ConnectionClosed:
            print("Headset disconnected")
        finally:
            self.connected_clients.remove(websocket)
            # Stop all streaming for this connection
            for channel in list(self.streaming_channels.keys()):
                self.streaming_channels[channel] = False
    
    async def start_server(self):
        """Start WebSocket server to communicate with headset"""
        print(f"Starting WebSocket server on port {self.stream_config.port}")
        print(f"Waiting for headset connection from {self.stream_config.headset_ip}...")
        
        async with websockets.serve(
            self.handle_client_commands, 
            "0.0.0.0",  # Listen on all interfaces
            self.stream_config.port
        ):
            await asyncio.Future()  # Run forever


async def main():
    """Main entry point"""
    # Configuration
    scope_config = ScopeConfig(ip_address="169.254.208.205")
    stream_config = StreamConfig(
        headset_ip="192.168.1.100",  # UPDATE THIS with your Quest 3S IP
        port=8765
    )
    
    # Create streamer
    streamer = OscopeStreamer(scope_config, stream_config)
    
    # Connect to oscilloscope
    if not streamer.connect_to_scope():
        print("Failed to connect to oscilloscope")
        return
    
    try:
        # Start WebSocket server
        await streamer.start_server()
    except KeyboardInterrupt:
        print("\nShutting down...")
    finally:
        # Cleanup
        streamer.disconnect_from_scope()


if __name__ == "__main__":
    asyncio.run(main())
