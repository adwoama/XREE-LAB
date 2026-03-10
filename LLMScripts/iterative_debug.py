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

from LLMScripts.ollama_runner import run_ollama_prompt
from LLMScripts import signal_math

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

SCRIPT_DIR = Path(__file__).parent
FAKE_DATA_FILE = SCRIPT_DIR / "fake_data.json"
DATASHEETS_FILE = SCRIPT_DIR / "datasheets.json"
SESSION_DIR = SCRIPT_DIR / "debug_sessions"

MAX_ITERATIONS = 10
CONFIDENCE_THRESHOLD = 0.85
MODEL = "llama3.1:8b-instruct-q8_0"

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
    probe_lines = "\n".join(
        _probe_summary_line(pid, p)
        for pid, p in fake_data["probes"].items()
    )
    datasheet_block = _datasheet_text(datasheets)
    return f"""You are an expert electronics circuit debugger working in an iterative measurement loop.

Circuit topology: {fake_data['circuit_topology']}
Symptom: Motor is not turning.

Available measurement probes:
{probe_lines}

--- COMPONENT DATASHEETS ---
{datasheet_block}
--- END DATASHEETS ---

Each turn you will receive the running measurement history.
Respond ONLY with a single valid JSON object — no extra text, no markdown fences.
Required fields:
{{
  "reasoning": "your analysis of what you know so far",
  "next_probe": "probeA | probeB | probeC | probeD | null",
  "request_analysis": [],
  "confidence": 0.0,
  "conclusion": null,
  "recommended_fix": null
}}

Rules:
- Set "next_probe" to the probe ID you want to measure next, or null if you have enough data.
- "request_analysis" is a list of math operations to run on the NEXT probe's raw sample array.
  Supported values: "stats", "dominant_freq", "moving_average".
  You will NOT receive any measurement values until you request them via analysis.
  Leave as [] if the probe has no raw samples (script will tell you).
- Use the datasheets above to determine whether measured values are within specification.
- Set "conclusion" to a concise fault diagnosis only when confidence >= {CONFIDENCE_THRESHOLD}.
- Set "recommended_fix" once you have a conclusion.
- Never guess at the fault without measurement evidence.
"""


def build_prompt(system_context: str, history: list,
                 iteration: int, max_iterations: int,
                 peak_confidence: float, all_probes_measured: bool) -> str:
    parts = [system_context, "--- MEASUREMENT HISTORY ---"]
    if not history:
        parts.append("No measurements taken yet. Choose the first probe to measure.")
    else:
        for i, entry in enumerate(history, 1):
            parts.append(f"\nStep {i} \u2014 Probe: {entry['probe_id']} ({entry['label']})")
            parts.append(entry["result_text"])

    is_final = (iteration >= max_iterations) or all_probes_measured
    parts.append("\n--- SESSION STATE ---")
    parts.append(f"Iteration: {iteration} of {max_iterations}")
    parts.append(f"Peak confidence reached this session: {peak_confidence:.0%}")
    parts.append("Confidence rule: only lower confidence if new evidence directly contradicts "
                 "prior findings. It should generally increase as more evidence is collected.")
    if is_final:
        parts.append("\nFINAL ITERATION: No more measurements will be taken after this. "
                     "Summarise what the collected evidence does and does not show. "
                     "If you have enough evidence to identify the fault, state it in 'conclusion' "
                     "with an honest confidence score. "
                     "If the evidence is genuinely insufficient, set 'conclusion' to "
                     "'Insufficient data to determine root cause' and set confidence "
                     "to a low value reflecting that uncertainty. "
                     "Do NOT invent a diagnosis you cannot support with the measurements taken.")
    parts.append("\n--- YOUR JSON RESPONSE ---")
    return "\n".join(parts)


# --------------------------------------------------------------------------- #
# Probe result formatting                                                     #
# --------------------------------------------------------------------------- #

def format_probe_result(probe_id: str, probe: dict, requested_analysis: list = None) -> str:
    """Return the measurement text shown to the LLM for one probe.

    The LLM receives ONLY the outputs of the math functions it explicitly
    requested via request_analysis.  No pre-computed features, signal_type,
    expected values, or notes are ever surfaced here.

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
    lines = [f"  Sampling rate: {rate} Hz,  {len(v)} samples  "
             f"({len(v)/rate*1000:.1f} ms window)"]
    lines.append("  [Analysis Results]")

    if "stats" in requested_analysis:
        s = signal_math.compute_stats(v)
        lines.append(f"    stats: mean={s['mean']:.4f}V  std={s['std']:.4f}V  "
                     f"min={s['min']:.4f}V  max={s['max']:.4f}V  "
                     f"p2p={s['peak_to_peak']:.4f}V  rms={s['rms']:.4f}V  n={s['n']}")
    if "dominant_freq" in requested_analysis:
        freq = signal_math.dominant_frequency_estimate(v, rate)
        lines.append(f"    dominant_freq: {freq} Hz")
    if "moving_average" in requested_analysis:
        ma = signal_math.moving_average(v, window=5)
        preview = ma[:5] + ["..."] + ma[-5:]
        lines.append(f"    moving_average (first5...last5): {preview}")

    return "\n".join(lines)


# --------------------------------------------------------------------------- #
# LLM response parsing                                                        #
# --------------------------------------------------------------------------- #

def parse_llm_response(raw: str) -> dict:
    clean = raw.strip()
    # Strip markdown code fences if the model wraps its output
    if clean.startswith("```"):
        lines = clean.splitlines()
        clean = "\n".join(lines[1:] if lines[0].startswith("```") else lines)
    if clean.endswith("```"):
        clean = "\n".join(clean.splitlines()[:-1])
    clean = clean.strip()
    return json.loads(clean)


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
    session_log = {
        "timestamp": datetime.datetime.now().isoformat(),
        "model": MODEL,
        "circuit": fake_data["circuit_topology"],
        "symptom": "Motor not turning",
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
        prompt = build_prompt(system_context, history,
                              iteration, MAX_ITERATIONS,
                              peak_confidence, all_probes_measured)

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
                "confidence": 0.0,
                "conclusion": None,
                "recommended_fix": None,
            }

        confidence = float(response.get("confidence", 0.0))
        conclusion = response.get("conclusion")
        fix = response.get("recommended_fix")
        reasoning = response.get("reasoning", "")

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
        if next_probe and next_probe not in probes:
            print(f"  [WARNING] LLM asked for unknown probe '{next_probe}', ignoring.")
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

        # --- Fetch and display probe data ---
        probe_data = probes[next_probe]
        requested_analysis = response.get("request_analysis") or []
        if requested_analysis:
            print(f"  [Analysis requested] {requested_analysis}")
        print(f"\n  Measuring {next_probe} ({probe_data['label']})...")
        result_text = format_probe_result(next_probe, probe_data, requested_analysis)
        print(result_text)

        measured_probes.add(next_probe)
        history.append({
            "probe_id": next_probe,
            "label": probe_data["label"],
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
