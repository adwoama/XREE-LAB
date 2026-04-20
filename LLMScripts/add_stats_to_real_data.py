import json
from signal_math import compute_stats

def add_stats_to_json(json_path):
    """Add computed stats to each probe in the JSON file."""
    try:
        with open(json_path, 'r') as file:
            data = json.load(file)
    except Exception as e:
        print(f"Error reading JSON file: {e}")
        return

    for probe_key, probe_info in data['probes'].items():
        voltages = probe_info['samples'].get('voltage_V', [])
        if voltages:
            stats = compute_stats(voltages)
            data['probes'][probe_key]['stats'] = stats
            print(f"Added stats for {probe_key}: {stats}")
        else:
            print(f"No voltage data for {probe_key}, skipping stats computation.")

    try:
        with open(json_path, 'w') as file:
            json.dump(data, file, indent=2)
        print(f"Successfully updated {json_path} with stats.")
    except Exception as e:
        print(f"Error writing to JSON file: {e}")

if __name__ == "__main__":
    json_file_path = "c:\\Users\\robot\\Documents\\XREE-LAB\\LLMScripts\\real_data.json"
    add_stats_to_json(json_file_path)