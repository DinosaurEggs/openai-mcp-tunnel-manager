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
# Hard verification gate. On Windows this also executes a real Credential Manager round-trip test.
& $python .\verify.py
if ($LASTEXITCODE -ne 0) { throw "Verification failed; package not created." }

& $python -m pip install --upgrade pip
& $python -m pip install "pyinstaller>=6.11,<7"
if ($LASTEXITCODE -ne 0) { throw "PyInstaller installation failed." }

Remove-Item -Recurse -Force build, dist -ErrorAction SilentlyContinue
& $python -m PyInstaller --noconfirm --clean --windowed --onedir `
    --name OpenAITunnelManager --paths src launcher.py
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed." }

if ($TunnelClientPath) {
    if (-not (Test-Path $TunnelClientPath)) { throw "TunnelClientPath does not exist: $TunnelClientPath" }
    Copy-Item $TunnelClientPath "dist\OpenAITunnelManager\tunnel-client.exe" -Force
} elseif (Test-Path ".\tunnel-client.exe") {
    Copy-Item ".\tunnel-client.exe" "dist\OpenAITunnelManager\tunnel-client.exe" -Force
}

$zip = "dist\OpenAITunnelManager-portable.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "dist\OpenAITunnelManager\*" -DestinationPath $zip
Write-Host "Built after successful verification: $zip"
