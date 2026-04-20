"""Parse LLM output into the required structured object and basic validation."""
import json
import re
from .schemas import ALL_BLOCK_TYPES


def _extract_json_block(text: str) -> str:
    # Try to find a JSON object in the text. This is tolerant to code fences.
    # First remove common fences
    text = re.sub(r"```[a-zA-Z0-9]*", "", text)
    text = text.replace("```", "")
    # Find the first '{' and the last '}'
    start = text.find("{")
    end = text.rfind("}")
    if start == -1 or end == -1 or end <= start:
        return ""
    return text[start:end+1]


def parse_llm_output(text: str, probe_labels: dict = None):
    """Parse an LLM text blob and validate structure.

    Returns a tuple (parsed: dict or None, errors: list).
    """
    errors = []
    js = None
    blob = _extract_json_block(text)
    if not blob:
        errors.append("No JSON object found in LLM output")
        return None, errors

    try:
        js = json.loads(blob)
    except Exception as e:
        errors.append(f"Failed to parse JSON: {e}")
        return None, errors

    # Basic shape checks
    if not isinstance(js, dict):
        errors.append("Parsed JSON is not an object")
        return None, errors

    # required keys
    for k in ("steps", "blocks_to_spawn", "reasoning", "confidence"):
        if k not in js:
            errors.append(f"Missing key: {k}")

    if errors:
        return js, errors

    # Validate blocks_to_spawn
    if not isinstance(js.get("blocks_to_spawn"), list):
        errors.append("blocks_to_spawn must be a list")
        return js, errors

    hallucinations = []
    for idx, b in enumerate(js.get("blocks_to_spawn", [])):
        if not isinstance(b, dict):
            errors.append(f"blocks_to_spawn[{idx}] must be an object")
            continue
        t = b.get("type")
        if t not in ALL_BLOCK_TYPES:
            hallucinations.append(f"Unknown block type: {t}")
        # check source exists in probe_labels if provided
        src = b.get("source")
        if probe_labels is not None and src not in probe_labels and src not in (None, ""):
            hallucinations.append(f"Unknown source '{src}' (not in probe_labels)")
        # Log the state of probe_labels and the source being validated
        print(f"Validating source: {src}, Available probes: {list(probe_labels.keys()) if probe_labels else 'None'}")

    if hallucinations:
        errors.extend(hallucinations)

    valid_parameters = ['stats', 'dominant_freq', 'moving_average', 'dc_offset']
    invalid_requests = [param for param in js.get('request_analysis', []) if param not in valid_parameters]
    if invalid_requests:
        errors.append(f"Invalid analysis parameters requested: {invalid_requests}")

    return js, errors


if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("Usage: python parser.py <llm_output.txt>")
        raise SystemExit(1)
    with open(sys.argv[1], "r", encoding="utf-8") as f:
        txt = f.read()
    parsed, errs = parse_llm_output(txt)
    print(parsed)
    print(errs)
