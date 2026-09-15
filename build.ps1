param([string]$TunnelClientPath = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path ".venv\Scripts\python.exe")) {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 -m venv .venv
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv .venv
    } else {
        throw "Python 3.11+ not found."
    }
    if ($LASTEXITCODE -ne 0) { throw "Failed to create virtual environment." }
}
$python = (Resolve-Path ".venv\Scripts\python.exe").Path

& $python -m pip install --upgrade pip
& $python -m pip install -r .\requirements.txt
if ($LASTEXITCODE -ne 0) { throw "Runtime dependency installation failed." }

# Hard verification gate. On Windows this also executes a real Credential Manager round-trip test.
& $python .\verify.py
if ($LASTEXITCODE -ne 0) { throw "Verification failed; package not created." }

& $python -m pip install "pyinstaller>=6.11,<7"
if ($LASTEXITCODE -ne 0) { throw "PyInstaller installation failed." }

Remove-Item -Recurse -Force build, dist -ErrorAction SilentlyContinue
& $python -m PyInstaller --noconfirm --clean --windowed --onefile --collect-submodules pystray `
    --name OpenAITunnelManager --paths src launcher.py
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed." }

if ($TunnelClientPath) {
    if (-not (Test-Path $TunnelClientPath)) { throw "TunnelClientPath does not exist: $TunnelClientPath" }
    Copy-Item $TunnelClientPath "dist\tunnel-client.exe" -Force
} elseif (Test-Path ".\tunnel-client.exe") {
    Copy-Item ".\tunnel-client.exe" "dist\tunnel-client.exe" -Force
}

Write-Host "Built after successful verification: dist\OpenAITunnelManager.exe"
