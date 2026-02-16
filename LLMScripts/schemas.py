"""Schemas and block definitions for the LLM interface."""
MODULE_BLOCKS = [
    "Oscilloscope",
    "SignalGenerator",
    "DataLogger",
    "VirtualProbe",
]

FUNCTION_BLOCKS = [
    "FFT",
    "Filter",
    "Math",
    "SpectrogramGenerator",
    "WaterfallGenerator",
]

VISUALIZATION_BLOCKS = [
    "TimeDomainPanel",
    "FFTMagnitudePanel",
    "Spectrogram3DVolume",
    "Waterfall3DPlot",
    "VectorFieldVisualization",
]

INTERACTION_BLOCKS = [
    "GestureTrigger",
    "SpatialAnchor",
    "GazeSelector",
    "VoiceCommand",
]

ALL_BLOCK_TYPES = MODULE_BLOCKS + FUNCTION_BLOCKS + VISUALIZATION_BLOCKS + INTERACTION_BLOCKS

# Minimal JSON schema for input validation (used by validator.py)
INPUT_SCHEMA = {
    "type": "object",
    "required": ["task_type", "circuit_topology", "probe_labels", "symptoms", "signal_features"],
    "properties": {
        "task_type": {"type": "string", "enum": ["debug", "analysis"]},
        "circuit_topology": {"type": "string"},
        "probe_labels": {"type": "object"},
        "symptoms": {"type": "string"},
        "signal_features": {"type": "object"},
    },
}
