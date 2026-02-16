"""Format prompts for the local LLM (Ollama) using the required structured input/output."""
import json
from .schemas import ALL_BLOCK_TYPES, MODULE_BLOCKS, FUNCTION_BLOCKS, VISUALIZATION_BLOCKS, INTERACTION_BLOCKS


def _block_list_text():
    parts = [
        "Module Blocks:\n  - " + ", ".join(MODULE_BLOCKS),
        "Function Blocks:\n  - " + ", ".join(FUNCTION_BLOCKS),
        "Visualization Blocks:\n  - " + ", ".join(VISUALIZATION_BLOCKS),
        "Interaction Blocks:\n  - " + ", ".join(INTERACTION_BLOCKS),
    ]
    return "\n".join(parts)


def format_prompt(input_json: dict) -> str:
    """Return a string prompt to send to the LLM.

    The prompt instructs the model to output a single JSON object with the
    exact required keys: `steps`, `blocks_to_spawn`, `reasoning`, `confidence`.
    """
    example_output = {
        "steps": ["Step 1: ..."],
        "blocks_to_spawn": [{"type": "TimeDomainPanel", "source": "probeA"}],
        "reasoning": "...",
        "confidence": 0.0,
    }

    prompt = []
    prompt.append("You are a technical assistant for a VR instrumentation system.")
    prompt.append("You are given a structured JSON input describing a circuit, probes, symptoms, and signal features.")
    prompt.append("Produce a single JSON object and nothing else with these exact fields: `steps`, `blocks_to_spawn`, `reasoning`, `confidence`.")
    prompt.append("- `steps`: array of short human-readable recommended next steps (strings).")
    prompt.append("- `blocks_to_spawn`: array of objects `{ \"type\": <BlockName>, \"source\": <probeLabelOrModule> }`.")
    prompt.append("- `reasoning`: concise summary of your reasoning (string). If uncertain, say so explicitly.")
    prompt.append("- `confidence`: a number between 0.0 and 1.0 indicating confidence in recommendations.")
    prompt.append("")
    prompt.append("Important rules:")
    prompt.append("1) Always use block names exactly as listed below.")
    prompt.append("2) Do not hallucinate internal circuit behavior — explicitly say when information is insufficient.")
    prompt.append("3) If you cannot produce concrete steps, put an entry in `steps` like 'Insufficient information: <what is missing>'.")
    prompt.append("")
    prompt.append("Available blocks:\n" + _block_list_text())
    prompt.append("")
    prompt.append("Required JSON example:\n" + json.dumps(example_output, indent=2))
    prompt.append("")
    prompt.append("Input JSON (exact):")
    prompt.append(json.dumps(input_json, indent=2))

    # join with double newlines to make sections clear
    return "\n\n".join(prompt)


if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("Usage: python prompt_formatter.py <input.json>")
        raise SystemExit(1)
    path = sys.argv[1]
    with open(path, "r", encoding="utf-8") as f:
        inp = json.load(f)
    print(format_prompt(inp))
