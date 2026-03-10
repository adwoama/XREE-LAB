import numpy as np
import json
from scipy.signal import find_peaks

def trim_data(file_path, output_path, time_limit, time_unit='s', sampling_rate_hz=1000):
    """
    Trims the data to the specified time length.

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the trimmed data.
        time_limit (float): The maximum time length to keep.
        time_unit (str): Unit of time ('s' for seconds, 'ms' for milliseconds, etc.).
        sampling_rate_hz (int): Sampling rate of the data in Hz.

    Returns:
        None
    """
    # Load the data
    with open(file_path, 'r') as f:
        data = json.load(f)

    # Convert time limit to number of samples
    if time_unit == 'ms':
        time_limit /= 1000  # Convert milliseconds to seconds
    num_samples = int(time_limit * sampling_rate_hz)

    # Trim the data
    trimmed_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            trimmed_data[probe] = {
                'samples': probe_data['samples'][:num_samples],
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', sampling_rate_hz)
            }

    # Save the trimmed data
    with open(output_path, 'w') as f:
        json.dump(trimmed_data, f, indent=4)

def downsample_data(file_path, output_path, factor):
    """
    Downsamples the data by the specified factor.

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the downsampled data.
        factor (int): Downsampling factor (e.g., 2 means keep every 2nd sample).

    Returns:
        None
    """
    if factor < 1:
        raise ValueError("Downsampling factor must be >= 1")

    # Load the data
    with open(file_path, 'r') as f:
        data = json.load(f)

    # Downsample the data
    downsampled_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            downsampled_data[probe] = {
                'samples': probe_data['samples'][::factor],
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', 1000) / factor
            }

    # Save the downsampled data
    with open(output_path, 'w') as f:
        json.dump(downsampled_data, f, indent=4)

def trim_highest_variance(file_path, output_path, window_size, sampling_rate_hz=1000):
    """
    Trims the data to the section with the highest variance.

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the trimmed data.
        window_size (int): Number of samples in the window.
        sampling_rate_hz (int): Sampling rate of the data in Hz.

    Returns:
        None
    """
    with open(file_path, 'r') as f:
        data = json.load(f)

    trimmed_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            samples = np.array(probe_data['samples'])
            variances = [np.var(samples[i:i+window_size]) for i in range(len(samples) - window_size + 1)]
            max_var_index = np.argmax(variances)
            trimmed_data[probe] = {
                'samples': samples[max_var_index:max_var_index + window_size].tolist(),
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', sampling_rate_hz)
            }

    with open(output_path, 'w') as f:
        json.dump(trimmed_data, f, indent=4)

def trim_most_stable(file_path, output_path, window_size, sampling_rate_hz=1000):
    """
    Trims the data to the section with the lowest variance (most stable).

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the trimmed data.
        window_size (int): Number of samples in the window.
        sampling_rate_hz (int): Sampling rate of the data in Hz.

    Returns:
        None
    """
    with open(file_path, 'r') as f:
        data = json.load(f)

    trimmed_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            samples = np.array(probe_data['samples'])
            variances = [np.var(samples[i:i+window_size]) for i in range(len(samples) - window_size + 1)]
            min_var_index = np.argmin(variances)
            trimmed_data[probe] = {
                'samples': samples[min_var_index:min_var_index + window_size].tolist(),
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', sampling_rate_hz)
            }

    with open(output_path, 'w') as f:
        json.dump(trimmed_data, f, indent=4)

def trim_around_threshold(file_path, output_path, threshold, window_size, sampling_rate_hz=1000):
    """
    Trims the data to the section around a threshold value.

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the trimmed data.
        threshold (float): The threshold value to center around.
        window_size (int): Number of samples in the window.
        sampling_rate_hz (int): Sampling rate of the data in Hz.

    Returns:
        None
    """
    with open(file_path, 'r') as f:
        data = json.load(f)

    trimmed_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            samples = np.array(probe_data['samples'])
            closest_index = np.argmin(np.abs(samples - threshold))
            start_index = max(0, closest_index - window_size // 2)
            end_index = min(len(samples), start_index + window_size)
            trimmed_data[probe] = {
                'samples': samples[start_index:end_index].tolist(),
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', sampling_rate_hz)
            }

    with open(output_path, 'w') as f:
        json.dump(trimmed_data, f, indent=4)

def trim_around_dominant_frequency(file_path, output_path, window_size, sampling_rate_hz=1000):
    """
    Trims the data to the section around the dominant frequency.

    Args:
        file_path (str): Path to the input data file (JSON format).
        output_path (str): Path to save the trimmed data.
        window_size (int): Number of samples in the window.
        sampling_rate_hz (int): Sampling rate of the data in Hz.

    Returns:
        None
    """
    with open(file_path, 'r') as f:
        data = json.load(f)

    trimmed_data = {}
    for probe, probe_data in data.items():
        if 'samples' in probe_data:
            samples = np.array(probe_data['samples'])
            fft_result = np.fft.fft(samples)
            freqs = np.fft.fftfreq(len(samples), d=1/sampling_rate_hz)
            dominant_freq_index = np.argmax(np.abs(fft_result[:len(fft_result)//2]))
            dominant_freq_time = int(sampling_rate_hz / freqs[dominant_freq_index])
            start_index = max(0, dominant_freq_time - window_size // 2)
            end_index = min(len(samples), start_index + window_size)
            trimmed_data[probe] = {
                'samples': samples[start_index:end_index].tolist(),
                'sampling_rate_hz': probe_data.get('sampling_rate_hz', sampling_rate_hz)
            }

    with open(output_path, 'w') as f:
        json.dump(trimmed_data, f, indent=4)

if __name__ == "__main__":
    # Example usage
    trim_data(
        file_path="input.json",
        output_path="trimmed_output.json",
        time_limit=2.0,  # 2 seconds
        time_unit='s',
        sampling_rate_hz=1000
    )

    downsample_data(
        file_path="trimmed_output.json",
        output_path="downsampled_output.json",
        factor=2
    )

    trim_highest_variance("input.json", "trimmed_highest_variance.json", window_size=100)
    trim_most_stable("input.json", "trimmed_most_stable.json", window_size=100)
    trim_around_threshold("input.json", "trimmed_around_threshold.json", threshold=0.5, window_size=100)
    trim_around_dominant_frequency("input.json", "trimmed_around_dominant_frequency.json", window_size=100)