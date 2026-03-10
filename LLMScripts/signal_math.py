"""Math utilities for computing signal statistics from raw sample arrays.

Called by iterative_debug.py when the LLM requests computed features
(e.g. mean, RMS, peak-to-peak) from raw probe samples.
"""
import math
from typing import List, Dict


def compute_stats(voltages: List[float]) -> Dict[str, float]:
    """Return basic descriptive statistics for a voltage sample array."""
    n = len(voltages)
    if n == 0:
        return {}
    mean = sum(voltages) / n
    variance = sum((v - mean) ** 2 for v in voltages) / n
    std = math.sqrt(variance)
    rms = math.sqrt(sum(v ** 2 for v in voltages) / n)
    return {
        "mean": round(mean, 4),
        "min": round(min(voltages), 4),
        "max": round(max(voltages), 4),
        "peak_to_peak": round(max(voltages) - min(voltages), 4),
        "std": round(std, 4),
        "rms": round(rms, 4),
        "n": n,
    }


def dominant_frequency_estimate(voltages: List[float], sample_rate_hz: float) -> float:
    """Estimate the dominant AC frequency via mean-crossing rate.

    Works well for single-tone sinusoidal ripple typical of a buck converter.
    Returns 0.0 for signals with no detectable oscillation.
    """
    if len(voltages) < 4:
        return 0.0
    mean = sum(voltages) / len(voltages)
    crossings = sum(
        1 for i in range(1, len(voltages))
        if (voltages[i - 1] - mean) * (voltages[i] - mean) < 0
    )
    duration_s = len(voltages) / sample_rate_hz
    # Two crossings per cycle
    freq = (crossings / 2.0) / duration_s
    return round(freq, 1)


def moving_average(voltages: List[float], window: int = 5) -> List[float]:
    """Return a smoothed version of the signal using a centered moving average."""
    result = []
    half = window // 2
    for i in range(len(voltages)):
        start = max(0, i - half)
        end = min(len(voltages), i + half + 1)
        result.append(round(sum(voltages[start:end]) / (end - start), 4))
    return result


def peak_to_peak(voltages: List[float]) -> float:
    if not voltages:
        return 0.0
    return round(max(voltages) - min(voltages), 4)


def rms(voltages: List[float]) -> float:
    if not voltages:
        return 0.0
    return round(math.sqrt(sum(v ** 2 for v in voltages) / len(voltages)), 4)
