@echo off
:: Wrapper to forward calls to the Ollama executable installed in the user's AppData.
:: This allows running `ollama` from the project folder if PATH is not set system-wide.
"C:\Users\robot\AppData\Local\Programs\Ollama\ollama.exe" %*
