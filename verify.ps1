$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 .\verify.py
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python .\verify.py
} else {
    throw "Python 3.11+ not found."
}
if ($LASTEXITCODE -ne 0) { throw "Verification failed." }
