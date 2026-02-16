"""Simple wrapper to run Ollama locally and return model output.

Uses the project's `ollama.cmd` wrapper if available on PATH or relative path.
"""
import subprocess
import shutil
import os


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

    # Send the prompt on stdin to avoid relying on a specific CLI flag.
    cmd = [exe, "run", model]
    proc = subprocess.run(cmd, input=prompt, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"Ollama run failed: {proc.stderr}")
    return proc.stdout


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
    print(out)
