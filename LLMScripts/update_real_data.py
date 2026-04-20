import json
import os
import csv
import numpy as np

def parse_csv(file_path):
    """Parse a CSV file and extract time and voltage data."""
    time_data = []
    voltage_data = []
    try:
        with open(file_path, 'r') as file:
            reader = csv.reader(file)
            for row in reader:
                if row and not row[0].startswith('#') and row[0] != 'Time (s)':
                    time_data.append(float(row[0]))
                    voltage_data.append(float(row[1]))
        print(f"Successfully parsed {file_path}")
    except Exception as e:
        print(f"Error parsing {file_path}: {e}")
    return time_data, voltage_data

def calculate_dominant_frequency(time_data, voltage_data):
    """Calculate the dominant frequency from time and voltage data."""
    try:
        sampling_rate = 1 / (time_data[1] - time_data[0])  # Assuming uniform sampling
        fft_result = np.fft.fft(voltage_data)
        frequencies = np.fft.fftfreq(len(fft_result), d=1/sampling_rate)
        magnitude = np.abs(fft_result)
        dominant_frequency = frequencies[np.argmax(magnitude[:len(magnitude)//2])]
        return dominant_frequency
    except Exception as e:
        print(f"Error calculating dominant frequency: {e}")
        return None

def calculate_dc_offset(voltage_data):
    """Calculate the DC offset of the signal."""
    try:
        return np.mean(voltage_data)
    except Exception as e:
        print(f"Error calculating DC offset: {e}")
        return None

def classify_signal_type(voltage_data):
    """Classify the signal as DC, AC, or alternating high/low."""
    try:
        p2p = max(voltage_data) - min(voltage_data)
        std_dev = np.std(voltage_data)
        if p2p < 0.05 and std_dev < 0.01:
            return "DC"
        elif p2p > 1.0 and std_dev > 0.1:
            return "Alternating High/Low"
        else:
            return "AC"
    except Exception as e:
        print(f"Error classifying signal type: {e}")
        return "Unknown"

def update_json(json_path):
    """Update the JSON file with data from explicitly mapped CSV files."""
    # Explicit mapping of probes to CSV files
    probe_to_file = {
        "probeA": "B1_pos_0001.csv",
        "probeB": "S1_out_0003.csv",
        "probeC": "U1_VCC_0001.csv",
        "probeD": "U1_ctrl_0001.csv",
        "probeE": "U1_pwm_0001.csv",
        "probeF": "SP_pos_0001.csv",
        "probeG": "SP_neg_0001.csv"
    }

    data_dir = "c:\\Users\\robot\\Documents\\XREE-LAB\\data"

    try:
        with open(json_path, 'r') as json_file:
            data = json.load(json_file)
    except Exception as e:
        print(f"Error reading JSON file {json_path}: {e}")
        return

    for probe_key, file_name in probe_to_file.items():
        file_path = os.path.join(data_dir, file_name)
        if os.path.exists(file_path):
            print(f"Updating {probe_key} with data from {file_path}")
            time_data, voltage_data = parse_csv(file_path)
            dominant_frequency = calculate_dominant_frequency(time_data, voltage_data)
            dc_offset = calculate_dc_offset(voltage_data)
            signal_type = classify_signal_type(voltage_data)

            # Update JSON structure
            data['probes'][probe_key]['samples'] = {
                "time_s": time_data[:100],  # Limit to first 100 points for readability
                "voltage_V": voltage_data[:100]  # Limit to first 100 points for readability
            }
            data['probes'][probe_key]['stats']['dominant_frequency_hz'] = dominant_frequency
            data['probes'][probe_key]['stats']['dc_offset_V'] = dc_offset
            data['probes'][probe_key]['stats']['signal_type'] = signal_type
        else:
            print(f"File {file_path} not found for {probe_key}")

    try:
        with open(json_path, 'w') as json_file:
            json.dump(data, json_file, indent=2)
        print(f"Successfully updated {json_path}")
    except Exception as e:
        print(f"Error writing to JSON file {json_path}: {e}")

if __name__ == "__main__":
    # Path to the JSON file
    json_file_path = "c:\\Users\\robot\\Documents\\XREE-LAB\\LLMScripts\\real_data.json"

    update_json(json_file_path)
    print("JSON file update process completed.")