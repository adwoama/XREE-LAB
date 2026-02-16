"""Input JSON validation utilities for the LLM interface."""
import json
from .schemas import INPUT_SCHEMA

try:
    import jsonschema
    _HAS_JSONSCHEMA = True
except Exception:
    _HAS_JSONSCHEMA = False


def validate_input(data):
    """Validate input JSON against a minimal schema.

    Returns (is_valid: bool, errors: list[str]).
    """
    errors = []
    if _HAS_JSONSCHEMA:
        try:
            jsonschema.validate(instance=data, schema=INPUT_SCHEMA)
            return True, []
        except jsonschema.ValidationError as e:
            return False, [str(e.message)]

    # Fallback lightweight validation
    if not isinstance(data, dict):
        return False, ["Input must be a JSON object"]

    for key in INPUT_SCHEMA.get("required", []):
        if key not in data:
            errors.append(f"Missing required key: {key}")

    if "task_type" in data and data.get("task_type") not in ("debug", "analysis"):
        errors.append("task_type must be 'debug' or 'analysis'")

    if errors:
        return False, errors
    return True, []


def load_json_file(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)
