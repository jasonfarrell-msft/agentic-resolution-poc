# Fix Python PATH so python.exe is found in new terminal sessions (Windows)
$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "User") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "Machine")

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$env:PYTHONPATH = $scriptDir

Write-Host "Starting DevUI at http://localhost:8080 ..."
python devui_serve.py
