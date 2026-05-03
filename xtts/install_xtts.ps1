$ErrorActionPreference = "Stop"

$python = Get-Command py -ErrorAction SilentlyContinue
if ($python) {
    $pythonCmd = "py -3.11"
} else {
    $pythonCmd = "python"
}

Write-Host "Checking Python..."
$versionOutput = Invoke-Expression "$pythonCmd -c `"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')`""
Write-Host "Python $versionOutput"

if ($versionOutput -notin @("3.10", "3.11")) {
    Write-Host ""
    Write-Host "XTTS/Coqui TTS requires Python 3.10 or 3.11." -ForegroundColor Red
    Write-Host "Install Python 3.11 from https://www.python.org/downloads/release/python-3119/" -ForegroundColor Yellow
    Write-Host "Then run this script again."
    exit 1
}

Invoke-Expression "$pythonCmd -m venv .venv"
.\.venv\Scripts\python.exe -m pip install --upgrade pip setuptools wheel
.\.venv\Scripts\pip.exe install -r requirements.txt

Write-Host ""
Write-Host "XTTS environment is ready." -ForegroundColor Green
Write-Host "Run: .\.venv\Scripts\python.exe tts_server.py"
