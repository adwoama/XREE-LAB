"""Simple wrapper to run Ollama locally and return model output.

Uses the project's `ollama.cmd` wrapper if available on PATH or relative path.
"""
import subprocess
import shutil
import os
import logging
import json


def _ollama_executable():
    # prefer local wrapper in the project folder
    local = os.path.join(os.path.dirname(__file__), "ollama.cmd")
    if os.path.exists(local):
        return local
    # fallback to system `ollama` if available
    which = shutil.which("ollama")
    return which


def run_ollama_prompt(prompt: str, model: str = "llama3.1:8b-instruct-q8_0") -> str:
    exe = _ollama_executable()
    if not exe:
        raise FileNotFoundError("No ollama executable found (looked for ollama.cmd and ollama in PATH)")

    cmd = [exe, "run", model]
    logging.debug(f"Running command: {cmd}")
    with subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8') as proc:
        try:
            stdout, stderr = proc.communicate(input=prompt, timeout=120)
            if proc.returncode != 0:
                logging.error(f"Ollama run failed with error: {stderr}")
                raise RuntimeError(f"Ollama run failed: {stderr}")
            logging.debug("Ollama run completed successfully.")
            return stdout
        except subprocess.TimeoutExpired:
            proc.kill()
            logging.error("Ollama process timed out.")
            raise TimeoutError("Ollama process timed out.")



if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print("Usage: python ollama_runner.py <prompt_file> [model]")
        raise SystemExit(1)
    pf = sys.argv[1]
    model = sys.argv[2] if len(sys.argv) > 2 else "llama3.1:8b-instruct-q8_0"

    def _read_text_file(path):
        # Try common encodings (Windows may have written file with cp1252)
        encodings = ("utf-8", "utf-8-sig", "cp1252", "latin-1")
        for enc in encodings:
            try:
                with open(path, "r", encoding=enc) as f:
                    return f.read()
            except UnicodeDecodeError:
                continue
        # Last resort: read bytes and decode with replacement characters
        with open(path, "rb") as f:
            return f.read().decode("utf-8", errors="replace")

    prompt = _read_text_file(pf)
    out = run_ollama_prompt(prompt, model=model)

    # Parse the output as JSON if possible
    try:
        parsed_output = json.loads(out)
    except json.JSONDecodeError:
        logging.error("Invalid JSON output from Ollama.")
        parsed_output = {"output": out}

    # Write the output to output.json
    output_file = "LLMScripts/output.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(parsed_output, f, ensure_ascii=False, indent=4)

    print(f"Output written to {output_file}")
