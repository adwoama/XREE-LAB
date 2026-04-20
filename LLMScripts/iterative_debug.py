"""Iterative LLM-based circuit debugging simulation.

Runs a back-and-forth loop between the LLM and fake oscilloscope data
to simulate how the model would diagnose a circuit fault.

Usage:
    python -m LLMScripts.iterative_debug              # automatic mode
    python -m LLMScripts.iterative_debug --manual     # user picks each probe

Press Ctrl+C at any time to stop and save the session log.
"""
import json
import os
import sys
import signal
import logging
import datetime
from pathlib import Path
import re

from LLMScripts.ollama_runner import run_ollama_prompt

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

SCRIPT_DIR = Path(__file__).parent
FAKE_DATA_FILE = SCRIPT_DIR / "real_data.json"
DATASHEETS_FILE = SCRIPT_DIR / "snap_circuits_datasheet.json"
SESSION_DIR = SCRIPT_DIR / "debug_sessions"

MAX_ITERATIONS = 20
CONFIDENCE_THRESHOLD = 0.85
MODEL = "llama3.1:8b-instruct-q8_0"
EXPECTED_SIGNAL_TYPES = {
    "probeD": "DC",  # Music IC control should be a stable logic level in this scenario.
}

# --------------------------------------------------------------------------- #
# Graceful Ctrl+C handling                                                    #
# --------------------------------------------------------------------------- #
_quit_requested = False


def _handle_sigint(sig, frame):
    global _quit_requested
    _quit_requested = True
    print("\n[!] Ctrl+C detected. Finishing this iteration then saving the session log...")


signal.signal(signal.SIGINT, _handle_sigint)


# --------------------------------------------------------------------------- #
# Data loading                                                                #
# --------------------------------------------------------------------------- #

def load_fake_data() -> dict:
    with open(FAKE_DATA_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def load_datasheets() -> dict:
    with open(DATASHEETS_FILE, "r", encoding="utf-8") as f:
        return json.load(f)


def load_netlist() -> str:
    with open(SCRIPT_DIR / "netlist.txt", "r", encoding="utf-8") as f:
        return f.read()

def load_snap_input() -> dict:
    with open(SCRIPT_DIR / "SnapCircuit_input.json", "r", encoding="utf-8") as f:
        return json.load(f)


# --------------------------------------------------------------------------- #
# Prompt construction                                                         #
# --------------------------------------------------------------------------- #

def _probe_summary_line(probe_id: str, probe: dict) -> str:
    return f"  {probe_id}: {probe['label']} (node: {probe['node']})"


def _datasheet_text(datasheets: dict) -> str:
    """Render datasheet specs as plain text for inclusion in the LLM prompt."""
    lines = []
    for part, spec in datasheets["components"].items():
        lines.append(f"[{spec['type']}]  {spec['part_number']} — {spec['description']}")
        # Include only the spec fields relevant for judgement; skip internal metadata keys
        skip = {"type", "manufacturer", "part_number", "description"}
        for k, v in spec.items():
            if k not in skip:
                lines.append(f"    {k}: {v}")
        lines.append("")
    lines.append("Circuit node connections:")
    for conn, node in datasheets["circuit_connections"].items():
        lines.append(f"  {conn}  ->  {node}")
    return "\n".join(lines)


def build_system_context(fake_data: dict, datasheets: dict) -> str:
    netlist = load_netlist()
    snap_input = load_snap_input()
    probe_lines = "\n".join(
        _probe_summary_line(pid, p)
        for pid, p in fake_data["probes"].items()
    )
    datasheet_text = _datasheet_text(datasheets)
    return f"""
    You are an expert electronics circuit debugger working in an iterative measurement loop.
    Circuit Topology: {snap_input['circuit_topology']}
    Symptoms: {snap_input['symptoms']}
    Netlist:
    {netlist}
    Probes:
    {probe_lines}
    Datasheet:
    {datasheet_text}
    """


def build_prompt(previous_data: dict, iteration: int, history: list, findings: list) -> str:
    """Build a prompt for the LLM to process."""
    history_text = json.dumps(history[-8:], ensure_ascii=True)
    findings_text = json.dumps(findings, ensure_ascii=True)
    prompt = (
        f"You are an expert debugging assistant for electronic circuits. "
        f"Your task is to analyze the provided data, eliminate hypotheses, and decide the next probe/analysis. "
        f"You must strictly follow this format in your response:\n\n"
        f"{{\n"
        f"  \"reasoning\": \"<Your reasoning here>\",\n"
        f"  \"next_probe\": \"<The next probe to measure>\",\n"
        f"  \"request_analysis\": [\"<Analysis 1>\", \"<Analysis 2>\"],\n"
        f"  \"confidence\": <0.0 to 1.0>,\n"
        f"  \"conclusion\": \"<Your conclusion or 'inconclusive' if no conclusion can be drawn>\"\n"
        f"}}\n\n"
        f"Rules:\n"
        f"1. Do not ask questions.\n"
        f"2. Only request data or analyses from probes and data sheets.\n"
        f"3. Use measurement history and do not repeat the exact same probe+analysis unless your reasoning explains why.\n"
        f"4. Explicitly narrow the diagnosis by ruling out at least one hypothesis each iteration when evidence allows.\n"
        f"5. If evidence is insufficient, state 'inconclusive' in conclusion and keep confidence <= 0.6.\n"
        f"6. Ensure the response is valid JSON.\n\n"
        f"Iteration: {iteration}\n"
        f"Previous Data: {json.dumps(previous_data)}\n"
        f"Recent Measurement History: {history_text}\n"
        f"Confirmed Findings: {findings_text}\n"
        f"Valid analysis parameters: ['stats', 'dominant_freq', 'moving_average', 'dc_offset', 'signal_type'].\n\n"
    )
    return prompt


def extract_signal_type(result_text: str) -> str | None:
    match = re.search(r"signal_type:\s*([^\n]+)", result_text)
    return match.group(1).strip() if match else None


def update_findings_from_measurement(probe_id: str, result_text: str, findings: list) -> None:
    expected = EXPECTED_SIGNAL_TYPES.get(probe_id)
    observed = extract_signal_type(result_text)
    if not expected or not observed:
        return

    key = f"{probe_id}:signal_type_mismatch"
    already_present = any(f.get("key") == key for f in findings)
    if already_present:
        return

    if observed != expected:
        findings.append({
            "key": key,
            "severity": "high",
            "type": "logic_signal_mismatch",
            "probe": probe_id,
            "expected_signal_type": expected,
            "observed_signal_type": observed,
            "implication": "Control/input behavior is unstable for a node expected to be steady.",
        })


# --------------------------------------------------------------------------- #
# Probe result formatting                                                     #
# --------------------------------------------------------------------------- #

def format_probe_result(probe_id: str, probe: dict, requested_analysis: list = None) -> str:
    """Return the measurement text shown to the LLM for one probe.

    The LLM receives ONLY pre-computed values from the loaded JSON.
    This avoids per-iteration recalculation overhead.

    If the probe has no raw samples (probeC / probeD) the LLM is told so it
    can adjust its strategy (e.g. ask for a different probe or conclude).
    """
    if requested_analysis is None:
        requested_analysis = []

    samples = probe.get("samples")
    has_raw = samples is not None and bool(samples.get("voltage_V"))
    rate = samples.get("sampling_rate_hz", 5000) if samples else 5000

    if not has_raw:
        return ("  [No raw sample array available for this probe.\n"
                "   Only computed analysis results can be reported once raw data exists.\n"
                "   Consider probing a different node or requesting a different analysis.]")

    if not requested_analysis:
        return ("  [Raw samples exist but no analysis was requested.\n"
                "   Use request_analysis with one or more of: \"stats\", \"dominant_freq\","
                " \"moving_average\"]")  

    v = samples["voltage_V"]
    precomputed = probe.get("stats", {})
    lines = [f"  Sampling rate: {rate} Hz,  {len(v)} samples  "
             f"({len(v)/rate*1000:.1f} ms window)"]
    lines.append("  [Analysis Results]")

    if "stats" in requested_analysis:
        needed = ["mean", "std", "min", "max", "peak_to_peak", "rms", "n"]
        if all(k in precomputed for k in needed):
            lines.append(
                f"    stats: mean={precomputed['mean']:.4f}V  std={precomputed['std']:.4f}V  "
                f"min={precomputed['min']:.4f}V  max={precomputed['max']:.4f}V  "
                f"p2p={precomputed['peak_to_peak']:.4f}V  rms={precomputed['rms']:.4f}V  n={precomputed['n']}"
            )
        else:
            lines.append("    stats: unavailable in JSON")

    if "dominant_freq" in requested_analysis:
        if "dominant_frequency_hz" in precomputed:
            lines.append(f"    dominant_freq: {precomputed['dominant_frequency_hz']} Hz")
        else:
            lines.append("    dominant_freq: unavailable in JSON")

    if "moving_average" in requested_analysis:
        # Intentionally not recomputed to keep runtime low; print only if present in JSON.
        if "moving_average" in precomputed:
            lines.append(f"    moving_average: {precomputed['moving_average']}")
        else:
            lines.append("    moving_average: unavailable in JSON")

    if "dc_offset" in requested_analysis:
        if "dc_offset_V" in precomputed:
            lines.append(f"    dc_offset: {precomputed['dc_offset_V']:.4f}V")
        else:
            lines.append("    dc_offset: unavailable in JSON")

    if "signal_type" in requested_analysis:
        if "signal_type" in precomputed:
            lines.append(f"    signal_type: {precomputed['signal_type']}")
        else:
            lines.append("    signal_type: unavailable in JSON")

    return "\n".join(lines)


# --------------------------------------------------------------------------- #
# LLM response parsing                                                        #
# --------------------------------------------------------------------------- #

def parse_llm_response(llm_output: str) -> dict:
    """Parse the LLM response, handling both JSON and structured text."""
    def sanitize_output(output: str) -> str:
        """Remove escape sequences, control characters, and fix line breaks."""
        # Remove ANSI escape sequences and other control characters
        sanitized = re.sub(r'(?:\x1B|\x9B)\[[0-?]*[ -/]*[@-~]', '', output)
        # Remove unnecessary line breaks within JSON strings
        sanitized = re.sub(r'\s*\\[dD]\[.*?\]', '', sanitized)
        sanitized = re.sub(r'\s+', ' ', sanitized)
        return sanitized.strip()

    # Sanitize the raw output
    sanitized_output = sanitize_output(llm_output)

    # Log sanitized output for debugging
    logging.debug(f"Sanitized LLM output: {sanitized_output}")

    try:
        # Attempt to parse as JSON
        return json.loads(sanitized_output)
    except json.JSONDecodeError:
        logging.warning("LLM returned non-JSON response. Attempting to parse manually.")

        # Initialize default response structure
        response = {
            "reasoning": None,
            "next_probe": None,
            "request_analysis": [],
            "confidence": 0.0,
            "conclusion": None,
            "recommended_fix": None,
        }

        # Extract fields from structured text
        try:
            if "reasoning:" in sanitized_output:
                reasoning_match = re.search(r'reasoning:\s*(.*?)(,|$)', sanitized_output)
                if reasoning_match:
                    response["reasoning"] = reasoning_match.group(1).strip()

            if "next_probe:" in sanitized_output:
                next_probe_match = re.search(r'next_probe:\s*(.*?)(,|$)', sanitized_output)
                if next_probe_match:
                    response["next_probe"] = next_probe_match.group(1).strip().strip("\"")

            if "request_analysis:" in sanitized_output:
                analysis_match = re.search(r'request_analysis:\s*(\[.*?\])', sanitized_output)
                if analysis_match:
                    response["request_analysis"] = json.loads(analysis_match.group(1))

            if "conclusion:" in sanitized_output:
                conclusion_match = re.search(r'conclusion:\s*(.*?)(,|$)', sanitized_output)
                if conclusion_match:
                    response["conclusion"] = conclusion_match.group(1).strip().strip("\"")
        except Exception as e:
            logging.error(f"Error parsing structured text response: {e}")

        return response

# --------------------------------------------------------------------------- #
# Main simulation loop                                                        #
# --------------------------------------------------------------------------- #

def run_session(auto: bool = True):
    SESSION_DIR.mkdir(exist_ok=True)
    fake_data = load_fake_data()
    datasheets = load_datasheets()
    probes = fake_data["probes"]
    system_context = build_system_context(fake_data, datasheets)

    history = []
    findings = []
    session_log = {
        "timestamp": datetime.datetime.now().isoformat(),
        "model": MODEL,
        "circuit": fake_data["circuit_topology"],
        "symptom": fake_data.get("_description", "Unknown symptom"),
        "ground_truth": "Buck converter output ~2.8V (expected 5V) with 1.48Vpp ripple at 200Hz",
        "mode": "auto" if auto else "manual",
        "iterations": [],
        "final_conclusion": None,
        "final_confidence": None,
    }

    print(f"\n{'=' * 62}")
    print("   ITERATIVE LLM CIRCUIT DEBUG SIMULATION")
    print(f"{'=' * 62}")
    print(f"  Model      : {MODEL}")
    print(f"  Max iters  : {MAX_ITERATIONS}")
    print(f"  Confidence : ≥{CONFIDENCE_THRESHOLD} to conclude")
    print(f"  Mode       : {'Automatic' if auto else 'Manual (you pick each probe)'}")
    print(f"  Ctrl+C     : stop and save session log")
    print(f"{'=' * 62}\n")

    measured_probes: set = set()
    peak_confidence: float = 0.0
    best_response: dict = {}

    for iteration in range(1, MAX_ITERATIONS + 1):
        if _quit_requested:
            print("[!] Stopping early by user request.")
            break

        all_probes_measured = len(measured_probes) >= len(probes)
        print(f"\n[Iteration {iteration}/{MAX_ITERATIONS}]  Querying LLM...")
        prompt = build_prompt(system_context, iteration, history, findings)

        try:
            raw = run_ollama_prompt(prompt, model=MODEL)
        except TimeoutError:
            print("[ERROR] LLM timed out. Stopping session.")
            break
        except Exception as e:
            print(f"[ERROR] LLM call failed: {e}")
            break

        try:
            response = parse_llm_response(raw)
        except json.JSONDecodeError:
            print(f"[WARNING] LLM returned non-JSON:\n{raw[:400]}")
            response = {
                "reasoning": raw,
                "next_probe": None,
                "request_analysis": [],
                "confidence": 0.0,
                "conclusion": None,
                "recommended_fix": None,
            }

        confidence = float(response.get("confidence", 0.0))
        conclusion = response.get("conclusion")
        fix = response.get("recommended_fix")
        reasoning = response.get("reasoning", "")

        # Confidence guardrails when model omits it.
        if "confidence" not in response:
            if findings:
                confidence = max(confidence, 0.75)
            if conclusion and str(conclusion).strip().lower() not in ("", "inconclusive"):
                confidence = max(confidence, 0.85)

        # Track peak confidence across the whole session
        if confidence > peak_confidence:
            peak_confidence = confidence
        if conclusion and confidence > 0:
            best_response = response

        print(f"  Reasoning  : {reasoning}")
        print(f"  Confidence : {confidence:.0%}  (session peak: {peak_confidence:.0%})")
        if conclusion:
            print(f"  Conclusion : {conclusion}")
        if fix:
            print(f"  Fix        : {fix}")

        iter_log = {
            "iteration": iteration,
            "llm_response": response,
            "probe_measured": None,
            "result_text": None,
        }

        # --- Confident conclusion reached ---
        if confidence >= CONFIDENCE_THRESHOLD and conclusion:
            session_log["final_conclusion"] = conclusion
            session_log["final_confidence"] = confidence
            session_log["iterations"].append(iter_log)
            print(f"\n{'=' * 62}")
            print("  LLM REACHED A CONFIDENT CONCLUSION")
            print(f"{'=' * 62}")
            print(f"  Diagnosis : {conclusion}")
            print(f"  Fix       : {fix or 'N/A'}")
            print(f"  After {iteration} iteration(s).")
            break

        # --- Determine which probe to measure next ---
        next_probe = response.get("next_probe")
        request_analysis = response.get("request_analysis", [])

        # Strip descriptive text from next_probe
        if isinstance(next_probe, str) and ':' in next_probe:
            next_probe = next_probe.split(':')[0].strip()
        if next_probe not in probes:
            print(f"[ERROR] next_probe '{next_probe}' is not a valid probe key. Ignoring.")
            next_probe = None

        if not next_probe:
            if auto:
                # Pick first unmeasured probe in topology order
                remaining = [p for p in probes if p not in measured_probes]
                if not remaining:
                    print("[INFO] All probes measured. Stopping.")
                    break
                next_probe = remaining[0]
                print(f"  [Auto] LLM gave no probe — selecting next unmeasured: {next_probe}")
            else:
                available = [p for p in probes if p not in measured_probes]
                print(f"  Available probes: {available}")
                raw_input = input("  Enter probe to measure (or 'quit'): ").strip()
                if raw_input.lower() in ("quit", "q", "exit"):
                    print("[!] User requested quit.")
                    break
                if raw_input not in probes:
                    print(f"  [ERROR] '{raw_input}' is not a valid probe. Skipping.")
                    continue
                next_probe = raw_input

        if not request_analysis:
            print("  [Auto] No analysis requested — defaulting to all analyses.")
            request_analysis = ["stats", "dominant_freq", "moving_average"]

        # --- Fetch and display probe data ---
        probe_data = probes[next_probe]
        if request_analysis:
            print(f"  [Analysis requested] {request_analysis}")
        print(f"\n  Measuring {next_probe} ({probe_data['label']})...")
        result_text = format_probe_result(next_probe, probe_data, request_analysis)
        print(result_text)

        # Deterministic narrowing: promote a confirmed mismatch into findings.
        update_findings_from_measurement(next_probe, result_text, findings)
        if findings:
            latest = findings[-1]
            print(
                f"  [Finding] {latest['probe']} expected {latest['expected_signal_type']} "
                f"but observed {latest['observed_signal_type']}"
            )

        measured_probes.add(next_probe)
        history.append({
            "probe_id": next_probe,
            "label": probe_data["label"],
            "requested_analysis": request_analysis,
            "result_text": result_text,
        })
        iter_log["probe_measured"] = next_probe
        iter_log["result_text"] = result_text
        session_log["iterations"].append(iter_log)

    # --- If session ended without a confident conclusion, surface the best one ---
    if not session_log["final_conclusion"]:
        if best_response.get("conclusion"):
            c = best_response.get("confidence", 0.0)
            label = "LOW-CONFIDENCE" if c < CONFIDENCE_THRESHOLD else "BEST"
            session_log["final_conclusion"] = best_response["conclusion"]
            session_log["final_confidence"] = c
            print(f"\n[!] Threshold never reached. {label} conclusion from session:")
            print(f"    Diagnosis  : {best_response['conclusion']}")
            print(f"    Confidence : {c:.0%}")
            print(f"    Fix        : {best_response.get('recommended_fix', 'N/A')}")
            if c < 0.4:
                print("    [!] Confidence is very low — diagnosis may not be reliable.")
        else:
            session_log["final_conclusion"] = "No conclusion reached"
            session_log["final_confidence"] = 0.0
            print("\n[!] Session ended without any conclusion being formed.")

    # --- Save session log ---
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    out_path = SESSION_DIR / f"session_{ts}.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(session_log, f, ensure_ascii=False, indent=4)
    print(f"\n[✓] Session log saved to {out_path.relative_to(SCRIPT_DIR.parent)}")


# --------------------------------------------------------------------------- #
# Entry point                                                                 #
# --------------------------------------------------------------------------- #

if __name__ == "__main__":
    mode = "auto"
    if "--manual" in sys.argv:
        mode = "manual"
    run_session(auto=(mode == "auto"))
