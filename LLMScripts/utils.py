"""Utilities for reasoning evaluation and simple checks."""
from .schemas import ALL_BLOCK_TYPES


def evaluate_reasoning(llm_output: dict, input_json: dict) -> dict:
    """Run lightweight checks on the LLM output and return a report.

    Checks performed:
    - blocks_to_spawn types are known
    - block sources reference provided probes if possible
    - steps exist and are non-empty
    - confidence is within [0,1]
    """
    report = {
        "valid_blocks": True,
        "unknown_blocks": [],
        "unknown_sources": [],
        "steps_count": 0,
        "confidence_valid": True,
    }

    probes = set(input_json.get("probe_labels", {}).keys())

    steps = llm_output.get("steps") if isinstance(llm_output, dict) else None
    if isinstance(steps, list):
        report["steps_count"] = len(steps)
    else:
        report["steps_count"] = 0

    blocks = llm_output.get("blocks_to_spawn") if isinstance(llm_output, dict) else None
    if isinstance(blocks, list):
        for b in blocks:
            t = b.get("type")
            if t not in ALL_BLOCK_TYPES:
                report["unknown_blocks"].append(t)
            src = b.get("source")
            if src and src not in probes:
                report["unknown_sources"].append(src)
        if report["unknown_blocks"] or report["unknown_sources"]:
            report["valid_blocks"] = False
    else:
        report["valid_blocks"] = False

    conf = llm_output.get("confidence")
    if not isinstance(conf, (int, float)) or not (0.0 <= float(conf) <= 1.0):
        report["confidence_valid"] = False

    return report
