$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 .\launcher.py
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python .\launcher.py
} else {
    throw "Python 3.11-3.13 not found. Install Python from python.org first."
}
