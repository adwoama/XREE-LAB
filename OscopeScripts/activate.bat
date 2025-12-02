@echo off
setlocal

REM Bootstrap venv on Windows and activate it
set "VENV_DIR=%~dp0venv"
set "PYTHON=python"

if not exist "%VENV_DIR%\Scripts\activate.bat" (
    echo [setup] Creating Python virtual environment at "%VENV_DIR%"...
    %PYTHON% -m venv "%VENV_DIR%"
    if errorlevel 1 (
        echo [error] Failed to create venv. Ensure Python is installed and on PATH.
        exit /b 1
    )
    echo [setup] Installing requirements...
    call "%VENV_DIR%\Scripts\activate.bat"
    pip install --upgrade pip
    if exist "%~dp0requirements.txt" (
        pip install -r "%~dp0requirements.txt"
    ) else (
        echo [warn] requirements.txt not found; skipping package install.
    )
)

echo [info] Activating venv...
call "%VENV_DIR%\Scripts\activate.bat"
if errorlevel 1 (
    echo [error] Failed to activate venv.
    exit /b 1
)

echo [info] venv activated. Current Python:
python --version

echo.
echo To run the server:
echo   python tcp_streaming_server.py

endlocal